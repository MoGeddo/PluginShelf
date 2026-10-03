using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using PluginShelf.Models;

namespace PluginShelf.Services;

public sealed class QuarantineApplyResult
{
    public List<QuarantineEntry> Moved { get; } = new();
    public List<string> Errors { get; } = new();
}

public static class QuarantineService
{
    private static readonly JsonSerializerOptions JsonOptions = SettingsService.CreateOptions();
    private static readonly object IndexLock = new();

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static string SavePlan(ApplyPlan plan)
    {
        Directory.CreateDirectory(SettingsService.DataDirectory);
        var path = Path.Combine(SettingsService.DataDirectory, $"pending-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(plan, JsonOptions));
        return path;
    }

    public static async Task<QuarantineApplyResult> ExecutePlanFileAsync(string planPath)
    {
        if (!IsAdministrator()) throw new UnauthorizedAccessException("Administrator approval is required to apply this plan.");
        if (!IsManagedPlanFile(planPath, "pending-")) throw new InvalidDataException("The apply plan is not a PluginShelf-created plan file.");
        var json = await File.ReadAllTextAsync(planPath);
        var plan = JsonSerializer.Deserialize<ApplyPlan>(json, JsonOptions)
                   ?? throw new InvalidDataException("The quarantine plan is empty or invalid.");
        var result = await ApplyAsync(plan);
        try { File.Delete(planPath); } catch { }
        return result;
    }

    public static string SaveRestorePlan(IEnumerable<QuarantineEntry> entries)
    {
        Directory.CreateDirectory(SettingsService.DataDirectory);
        var path = Path.Combine(SettingsService.DataDirectory, $"restore-{Guid.NewGuid():N}.json");
        var plan = new RestorePlan { EntryIds = entries.Select(e => e.Id).Distinct().ToList() };
        File.WriteAllText(path, JsonSerializer.Serialize(plan, JsonOptions));
        return path;
    }

    public static async Task<(int Restored, List<string> Errors)> ExecuteRestorePlanFileAsync(string planPath)
    {
        if (!IsAdministrator()) throw new UnauthorizedAccessException("Administrator approval is required to restore these items.");
        if (!IsManagedPlanFile(planPath, "restore-")) throw new InvalidDataException("The restore plan is not a PluginShelf-created plan file.");
        var json = await File.ReadAllTextAsync(planPath);
        var plan = JsonSerializer.Deserialize<RestorePlan>(json, JsonOptions)
                   ?? throw new InvalidDataException("The restore plan is empty or invalid.");
        var entries = LoadEntries().Where(e => plan.EntryIds.Contains(e.Id)).ToList();
        var result = await RestoreAsync(entries);
        try { File.Delete(planPath); } catch { }
        return result;
    }

    public static Task<QuarantineApplyResult> ApplyAsync(ApplyPlan plan) => Task.Run(() =>
    {
        var result = new QuarantineApplyResult();
        var settings = SettingsService.Load();
        foreach (var item in plan.Items.DistinctBy(i => i.OriginalPath, StringComparer.OrdinalIgnoreCase))
        {
            QuarantineEntry? pendingEntry = null;
            try
            {
                var fullPath = Path.GetFullPath(item.OriginalPath);
                if (PluginSafety.IsProtectedCandidate(fullPath, null, item.PluginName))
                    throw new InvalidOperationException("Protected Waves/WPAPI item; it will not be moved.");
                if (!PluginSafety.IsSupportedPath(fullPath, Directory.Exists(fullPath)))
                    throw new InvalidOperationException("The path is not a supported plug-in item.");
                if (!IsInsideEnabledScanRoot(fullPath, item.Format, settings.Roots))
                    throw new InvalidOperationException("The item is outside the enabled scan folders; the plan was rejected.");
                if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                    throw new FileNotFoundException("The original item no longer exists.", fullPath);

                var root = Path.GetPathRoot(fullPath);
                if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Could not determine the source drive.");
                var runId = plan.CreatedAt.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
                var runRoot = Path.Combine(root, "PluginShelf_Quarantine", runId);
                var quarantineBase = Path.Combine(runRoot, "payload");
                var manifestPath = Path.Combine(runRoot, "manifest.json");
                var relative = Path.GetRelativePath(root, fullPath);
                var destination = Path.GetFullPath(Path.Combine(quarantineBase, relative));
                if (!destination.StartsWith(Path.GetFullPath(quarantineBase) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The quarantine destination escaped its protected root.");
                var destinationParent = Path.GetDirectoryName(destination);
                if (string.IsNullOrWhiteSpace(destinationParent)) throw new InvalidOperationException("Could not create a safe destination path.");
                Directory.CreateDirectory(destinationParent);
                if (File.Exists(destination) || Directory.Exists(destination))
                    throw new IOException("A quarantine item with the same path already exists.");

                pendingEntry = new QuarantineEntry
                {
                    PluginName = item.PluginName,
                    Format = item.Format,
                    Architecture = item.Architecture,
                    OriginalPath = fullPath,
                    QuarantinedPath = destination,
                    ManifestPath = manifestPath,
                    MovedAt = DateTimeOffset.Now,
                    Status = "Pending"
                };
                AddEntry(pendingEntry); // Persist recovery information before touching the original.
                WriteRunManifest(pendingEntry);

                // The quarantine is created on the source volume, avoiding a copy/delete move.
                if (Directory.Exists(fullPath)) Directory.Move(fullPath, destination);
                else File.Move(fullPath, destination);
                pendingEntry.Status = "Quarantined";
                UpdateEntry(pendingEntry);
                WriteRunManifest(pendingEntry);
                result.Moved.Add(pendingEntry);
            }
            catch (Exception ex)
            {
                if (pendingEntry is not null &&
                    !File.Exists(pendingEntry.QuarantinedPath) && !Directory.Exists(pendingEntry.QuarantinedPath))
                {
                    try { RemoveEntry(pendingEntry.Id); } catch { }
                }
                result.Errors.Add($"{item.PluginName}: {ex.Message}");
            }
        }
        return result;
    });

    public static List<QuarantineEntry> LoadEntries()
    {
        try
        {
            lock (IndexLock)
            {
                if (!File.Exists(SettingsService.QuarantineIndexPath)) return new List<QuarantineEntry>();
                return JsonSerializer.Deserialize<List<QuarantineEntry>>(
                           File.ReadAllText(SettingsService.QuarantineIndexPath), JsonOptions) ?? new();
            }
        }
        catch { return new List<QuarantineEntry>(); }
    }

    public static Task<(int Restored, List<string> Errors)> RestoreAsync(IEnumerable<QuarantineEntry> entries) => Task.Run(() =>
    {
        var restored = 0;
        var errors = new List<string>();
        foreach (var selected in entries.ToList())
        {
            try
            {
                var entry = LoadEntries().FirstOrDefault(e => e.Id == selected.Id);
                if (entry is null) continue;
                if (PluginSafety.IsProtectedCandidate(entry.OriginalPath, null, entry.PluginName))
                    throw new InvalidOperationException("Protected Waves/WPAPI paths cannot be restored by this tool.");
                if (!PluginSafety.IsSupportedPath(entry.OriginalPath, Directory.Exists(entry.QuarantinedPath)) ||
                    !IsQuarantinePayloadPath(entry.OriginalPath, entry.QuarantinedPath))
                    throw new InvalidOperationException("The quarantine record failed its path safety checks.");
                if (!File.Exists(entry.QuarantinedPath) && !Directory.Exists(entry.QuarantinedPath))
                    throw new FileNotFoundException("The quarantined item was not found.", entry.QuarantinedPath);
                if (File.Exists(entry.OriginalPath) || Directory.Exists(entry.OriginalPath))
                    throw new IOException("The original path is occupied. Nothing was overwritten.");

                var parent = Path.GetDirectoryName(entry.OriginalPath);
                if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
                if (Directory.Exists(entry.QuarantinedPath)) Directory.Move(entry.QuarantinedPath, entry.OriginalPath);
                else File.Move(entry.QuarantinedPath, entry.OriginalPath);
                entry.Status = "Restored";
                try { WriteRunManifest(entry); } catch { }
                RemoveEntry(entry.Id);
                restored++;
            }
            catch (Exception ex)
            {
                errors.Add($"{selected.PluginName}: {ex.Message}");
            }
        }
        return (restored, errors);
    });

    public static Process? StartElevatedApply(string planPath)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return null;
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = $"--apply-plan \"{planPath}\"",
            UseShellExecute = true,
            Verb = "runas"
        };
        return Process.Start(startInfo);
    }

    public static Process? StartElevatedRestore(string planPath)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return null;
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = $"--restore-plan \"{planPath}\"",
            UseShellExecute = true,
            Verb = "runas"
        };
        return Process.Start(startInfo);
    }

    private static bool IsQuarantinePayloadPath(string originalPath, string quarantinePath)
    {
        try
        {
            var originalFull = Path.GetFullPath(originalPath);
            var quarantineFull = Path.GetFullPath(quarantinePath);
            var originalRoot = Path.GetPathRoot(originalFull);
            var quarantineRoot = Path.GetPathRoot(quarantineFull);
            if (string.IsNullOrWhiteSpace(originalRoot) ||
                !string.Equals(originalRoot, quarantineRoot, StringComparison.OrdinalIgnoreCase)) return false;
            var expectedRoot = Path.Combine(originalRoot, "PluginShelf_Quarantine") + Path.DirectorySeparatorChar;
            if (!quarantineFull.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase)) return false;
            return quarantineFull.Contains(Path.DirectorySeparatorChar + "payload" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsManagedPlanFile(string path, string requiredPrefix)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetFullPath(SettingsService.DataDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase) &&
                   Path.GetFileName(fullPath).StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase) &&
                   File.Exists(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsInsideEnabledScanRoot(string candidatePath, string format, IEnumerable<ScanRoot> roots)
    {
        var fullCandidate = Path.GetFullPath(candidatePath);
        var extension = Path.GetExtension(fullCandidate);
        var formatMatches = extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                ? format.StartsWith("VST", StringComparison.OrdinalIgnoreCase)
                : extension.Equals(".vst3", StringComparison.OrdinalIgnoreCase)
                    ? format.Equals("VST3", StringComparison.OrdinalIgnoreCase)
                    : extension.Equals(".clap", StringComparison.OrdinalIgnoreCase) &&
                      format.Equals("CLAP", StringComparison.OrdinalIgnoreCase);
        if (!formatMatches) return false;

        foreach (var root in roots.Where(r => r.Enabled && !PluginSafety.IsProtectedPath(r.Path)))
        {
            try
            {
                var expanded = Environment.ExpandEnvironmentVariables(root.Path.Trim());
                if (!PluginSafety.IsReasonableScanRoot(expanded, out _)) continue;
                var fullRoot = Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var prefix = fullRoot + Path.DirectorySeparatorChar;
                if (!fullCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var kindAllows = root.Kind switch
                {
                    RootKind.Vst => extension.Equals(".dll", StringComparison.OrdinalIgnoreCase),
                    RootKind.Vst3 => extension.Equals(".vst3", StringComparison.OrdinalIgnoreCase),
                    RootKind.Clap => extension.Equals(".clap", StringComparison.OrdinalIgnoreCase),
                    _ => extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                         extension.Equals(".vst3", StringComparison.OrdinalIgnoreCase) ||
                         extension.Equals(".clap", StringComparison.OrdinalIgnoreCase)
                };
                if (kindAllows) return true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }
        }
        return false;
    }

    private static void WriteRunManifest(QuarantineEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ManifestPath)) return;
        var manifest = Path.GetFullPath(entry.ManifestPath);
        var expectedRoot = Path.Combine(Path.GetPathRoot(entry.OriginalPath) ?? "", "PluginShelf_Quarantine") + Path.DirectorySeparatorChar;
        if (!manifest.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) ||
            !manifest.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The quarantine manifest path failed its safety check.");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllText(manifest, JsonSerializer.Serialize(entry, JsonOptions));
    }

    private static void AddEntry(QuarantineEntry entry)
    {
        lock (IndexLock)
        {
            var entries = LoadEntries();
            entries.Insert(0, entry);
            Directory.CreateDirectory(SettingsService.DataDirectory);
            File.WriteAllText(SettingsService.QuarantineIndexPath, JsonSerializer.Serialize(entries, JsonOptions));
        }
    }

    private static void UpdateEntry(QuarantineEntry updated)
    {
        lock (IndexLock)
        {
            var entries = LoadEntries();
            var index = entries.FindIndex(e => e.Id == updated.Id);
            if (index < 0) entries.Insert(0, updated);
            else entries[index] = updated;
            Directory.CreateDirectory(SettingsService.DataDirectory);
            File.WriteAllText(SettingsService.QuarantineIndexPath, JsonSerializer.Serialize(entries, JsonOptions));
        }
    }

    private static void RemoveEntry(Guid id)
    {
        lock (IndexLock)
        {
            var entries = LoadEntries();
            entries.RemoveAll(e => e.Id == id);
            File.WriteAllText(SettingsService.QuarantineIndexPath, JsonSerializer.Serialize(entries, JsonOptions));
        }
    }
}
