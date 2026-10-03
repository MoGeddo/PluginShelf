using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using PluginShelf.Models;

namespace PluginShelf.Services;

public sealed class PluginScanner
{
    private static readonly Regex IdentityTokenPattern = new("[\\p{L}]+|\\d+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ChannelQualifierTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "mono", "stereo", "sc", "sidechain", "side", "chain"
    };
    private static readonly HashSet<string> ArchitectureTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "x86", "x64", "i386", "i686", "win32", "win64", "32", "64", "bit", "bits"
    };

    public Task<ScanResult> ScanAsync(AppSettings settings, IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => ScanCore(settings, progress, cancellationToken), cancellationToken);

    private static ScanResult ScanCore(AppSettings settings, IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new ScanResult();
        var roots = settings.Roots.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Path) &&
                                               !PluginSafety.IsProtectedPath(r.Path)).ToList();
        var rootIndex = 0;
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rootIndex++;
            var normalizedRoot = Environment.ExpandEnvironmentVariables(root.Path.Trim());
            if (!PluginSafety.IsReasonableScanRoot(normalizedRoot, out var rootProblem))
            {
                result.RootsMissing++;
                result.Warnings.Add($"Skipped unsafe scan folder: {normalizedRoot} ({rootProblem})");
                continue;
            }
            progress?.Report(new ScanProgress
            {
                RootsCompleted = rootIndex - 1,
                RootsTotal = roots.Count,
                CandidatesFound = result.Candidates.Count(c => c.IsLikelyPlugin),
                CurrentPath = normalizedRoot
            });

            if (!Directory.Exists(normalizedRoot))
            {
                result.RootsMissing++;
                result.Warnings.Add($"Folder not found: {normalizedRoot}");
                continue;
            }

            result.RootsScanned++;
            ScanRoot(normalizedRoot, root.Kind, result, cancellationToken);
            progress?.Report(new ScanProgress
            {
                RootsCompleted = rootIndex,
                RootsTotal = roots.Count,
                CandidatesFound = result.Candidates.Count(c => c.IsLikelyPlugin),
                CurrentPath = normalizedRoot
            });
        }

        var uniqueCandidates = DisambiguateGenericVst3BundleNames(
            result.Candidates
                .GroupBy(c => Path.GetFullPath(c.Path), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList());
        result.Candidates.Clear();
        result.Candidates.AddRange(uniqueCandidates);
        return result;
    }

    private static List<PluginCandidate> DisambiguateGenericVst3BundleNames(List<PluginCandidate> candidates)
    {
        var rewrites = new Dictionary<Guid, PluginCandidate>();
        var vst3Groups = candidates
            .Where(c => c.Format == PluginFormat.Vst3 && !string.IsNullOrWhiteSpace(c.CoreNameKey))
            .GroupBy(c => $"{c.VendorKey}::{c.CoreNameKey}", StringComparer.OrdinalIgnoreCase);

        foreach (var group in vst3Groups)
        {
            var members = group.ToList();
            if (members.Count < 2) continue;

            var stemInfos = members
                .Select(c =>
                {
                    var stem = Path.GetFileNameWithoutExtension(
                        c.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    var norm = PluginNameNormalizer.Normalize(stem, c.Vendor);
                    return (Candidate: c, Stem: stem, Normalized: norm);
                })
                .Where(x => Useful(x.Stem) && !string.IsNullOrWhiteSpace(x.Normalized.CoreNameKey))
                .ToList();

            var distinctStemKeys = stemInfos
                .Select(x => x.Normalized.CoreNameKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            if (distinctStemKeys <= 1) continue;

            foreach (var item in stemInfos)
            {
                rewrites[item.Candidate.Id] = new PluginCandidate
                {
                    Id = item.Candidate.Id,
                    Name = item.Stem.Trim(),
                    Vendor = item.Candidate.Vendor,
                    Version = item.Candidate.Version,
                    Path = item.Candidate.Path,
                    BinaryPath = item.Candidate.BinaryPath,
                    Format = item.Candidate.Format,
                    Architecture = item.Candidate.Architecture,
                    ArchitectureDetail = item.Candidate.ArchitectureDetail,
                    IsBundle = item.Candidate.IsBundle,
                    IsMultiComponent = item.Candidate.IsMultiComponent,
                    IsLikelyPlugin = item.Candidate.IsLikelyPlugin,
                    DetectionNote = item.Candidate.DetectionNote,
                    NameKey = item.Normalized.NameKey,
                    CoreNameKey = item.Normalized.CoreNameKey,
                    VendorKey = item.Normalized.VendorKey
                };
            }
        }

        if (rewrites.Count == 0) return candidates;
        return candidates.Select(c => rewrites.TryGetValue(c.Id, out var updated) ? updated : c).ToList();
    }

    private static void ScanRoot(string rootPath, RootKind rootKind, ScanResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            if ((File.GetAttributes(rootPath) & FileAttributes.ReparsePoint) != 0)
            {
                result.Warnings.Add($"Skipped linked scan root: {rootPath}");
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Warnings.Add($"Could not inspect scan root: {rootPath} ({ex.Message})");
            return;
        }

        var pending = new Stack<string>();
        pending.Push(rootPath);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            if (PluginSafety.IsProtectedPath(current)) continue;
            if (!visited.Add(current)) continue;

            IEnumerable<string> directories;
            try { directories = Directory.EnumerateDirectories(current).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Warnings.Add($"Could not read folder: {current} ({ex.Message})");
                continue;
            }

            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (PluginSafety.IsProtectedPath(directory)) continue;
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.Warnings.Add($"Could not inspect folder: {directory} ({ex.Message})");
                    continue;
                }
                var extension = Path.GetExtension(directory);
                if (extension.Equals(".vst3", StringComparison.OrdinalIgnoreCase))
                {
                    if (Allows(rootKind, PluginFormat.Vst3))
                    {
                        var candidate = ReadVst3(directory);
                        if (candidate is not null && !PluginSafety.IsProtectedCandidate(candidate.Path, candidate.Vendor, candidate.Name))
                            result.Candidates.Add(candidate);
                    }
                    continue; // A VST3 bundle is one package; never scan its internal binary as another plug-in.
                }

                pending.Push(directory);
            }

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(current).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Warnings.Add($"Could not list files: {current} ({ex.Message})");
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (PluginSafety.IsProtectedPath(file)) continue;
                try
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.Warnings.Add($"Could not inspect file: {file} ({ex.Message})");
                    continue;
                }
                var extension = Path.GetExtension(file);
                PluginCandidate? candidate = null;

                if (extension.Equals(".vst3", StringComparison.OrdinalIgnoreCase) && Allows(rootKind, PluginFormat.Vst3))
                    candidate = ReadVst3(file);
                else if (extension.Equals(".clap", StringComparison.OrdinalIgnoreCase) && Allows(rootKind, PluginFormat.Clap))
                    candidate = ReadBinaryPlugin(file, PluginFormat.Clap);
                else if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) && Allows(rootKind, PluginFormat.Vst))
                    candidate = ReadBinaryPlugin(file, PluginFormat.Vst);

                if (candidate is not null && !PluginSafety.IsProtectedCandidate(candidate.Path, candidate.Vendor, candidate.Name))
                    result.Candidates.Add(candidate);
            }
        }
    }

    private static bool Allows(RootKind kind, PluginFormat format) => kind switch
    {
        RootKind.Vst => format == PluginFormat.Vst,
        RootKind.Vst3 => format == PluginFormat.Vst3,
        RootKind.Clap => format == PluginFormat.Clap,
        _ => true
    };

    private static PluginCandidate? ReadBinaryPlugin(string path, PluginFormat format)
    {
        var pe = PeInspector.Inspect(path);
        if (pe is null) return null;

        var isLikely = format switch
        {
            PluginFormat.Vst => pe.Exports.Any(IsVst2Entry),
            PluginFormat.Clap => pe.Exports.Any(e => e.TrimStart('_').Equals("clap_entry", StringComparison.OrdinalIgnoreCase)),
            _ => false
        };

        var versionInfo = SafeVersionInfo(path);
        var fileName = Path.GetFileNameWithoutExtension(path);
        var companyName = versionInfo?.CompanyName;
        var vendor = Useful(companyName) ? companyName!.Trim() : "";
        var nameCandidates = new[] { versionInfo?.ProductName, versionInfo?.FileDescription, fileName };
        var name = ResolveIdentityName(nameCandidates, fileName, vendor);
        var version = FirstUseful(versionInfo?.ProductVersion, versionInfo?.FileVersion);
        var normalized = PluginNameNormalizer.Normalize(name, vendor);

        return new PluginCandidate
        {
            Name = name,
            Vendor = vendor,
            Version = version,
            Path = path,
            BinaryPath = path,
            Format = format,
            Architecture = pe.Architecture,
            ArchitectureDetail = ArchitectureText(pe.Architecture),
            IsBundle = false,
            IsLikelyPlugin = isLikely,
            DetectionNote = isLikely ? "Plug-in entry point found" : "PE file in a plug-in folder; entry point not confirmed",
            NameKey = normalized.NameKey,
            CoreNameKey = normalized.CoreNameKey,
            VendorKey = normalized.VendorKey
        };
    }

    private static PluginCandidate? ReadVst3(string path)
    {
        var isDirectory = Directory.Exists(path);
        var binaryPath = isDirectory ? FindVst3Binary(path) : path;
        var pe = !string.IsNullOrWhiteSpace(binaryPath) ? PeInspector.Inspect(binaryPath) : null;
        var moduleInfo = isDirectory ? ReadModuleInfo(path) : null;
        var fileName = Path.GetFileNameWithoutExtension(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var fileInfo = !string.IsNullOrWhiteSpace(binaryPath) ? SafeVersionInfo(binaryPath) : null;

        var classInfo = moduleInfo?.Classes.FirstOrDefault(IsAudioModuleClass)
                        ?? moduleInfo?.Classes.FirstOrDefault(c => !IsNonAudioHelperClass(c))
                        ?? moduleInfo?.Classes.FirstOrDefault();
        var vendor = FirstUseful(classInfo?.Vendor, moduleInfo?.Vendor, fileInfo?.CompanyName);
        var nameCandidates = new[]
        {
            classInfo?.Name, moduleInfo?.Name,
            fileInfo?.ProductName, fileInfo?.FileDescription, fileName
        };
        // Prefer product/generation/channel detail from the bundle name when metadata is generic.
        var name = ResolveIdentityName(nameCandidates, fileName, vendor);
        var version = FirstUseful(classInfo?.Version,
            fileInfo?.ProductVersion, fileInfo?.FileVersion, moduleInfo?.Version);

        var architecture = DetectVst3Architecture(path, isDirectory, pe?.Architecture, out var architectureDetail);
        var hasVst3Evidence = moduleInfo is not null || pe?.Exports.Any(IsVst3Entry) == true;
        var normalized = PluginNameNormalizer.Normalize(name, vendor);

        return new PluginCandidate
        {
            Name = name,
            Vendor = vendor,
            Version = version,
            Path = path,
            BinaryPath = binaryPath ?? path,
            Format = PluginFormat.Vst3,
            Architecture = architecture,
            ArchitectureDetail = architectureDetail,
            IsBundle = isDirectory,
            IsMultiComponent = HasMultiplePluginComponents(moduleInfo),
            IsLikelyPlugin = hasVst3Evidence,
            DetectionNote = hasVst3Evidence
                ? "VST3 bundle metadata or entry point found"
                : "VST3-shaped item; metadata was not confirmed",
            NameKey = normalized.NameKey,
            CoreNameKey = normalized.CoreNameKey,
            VendorKey = normalized.VendorKey
        };
    }

    private static Vst3ModuleInfo? ReadModuleInfo(string bundlePath)
    {
        var path = Path.Combine(bundlePath, "Contents", "Resources", "moduleinfo.json");
        if (!File.Exists(path)) return null;
        try
        {
            var text = File.ReadAllText(path).TrimStart('\uFEFF');
            JsonDocument document;
            try { document = JsonDocument.Parse(text); }
            catch (JsonException) { document = JsonDocument.Parse(NormalizeJson5(text)); }
            using (document)
            {
                var root = document.RootElement;
                var classes = new List<Vst3ClassInfo>();
                if (TryGet(root, "Classes", out var classesElement) && classesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in classesElement.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        classes.Add(new Vst3ClassInfo
                        {
                            Category = GetString(item, "Category"),
                            Name = GetString(item, "Name"),
                            Vendor = GetString(item, "Vendor"),
                            Version = GetString(item, "Version"),
                            ClassId = GetString(item, "CID")
                        });
                    }
                }
                var vendor = GetString(root, "Vendor");
                if (string.IsNullOrWhiteSpace(vendor) && TryGet(root, "Factory Info", out var factoryInfo))
                    vendor = GetString(factoryInfo, "Vendor");
                return new Vst3ModuleInfo
                {
                    Name = GetString(root, "Name"),
                    Vendor = vendor,
                    Version = GetString(root, "Version"),
                    Classes = classes
                };
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string NormalizeJson5(string text) =>
        RemoveJson5TrailingCommas(QuoteJson5Keys(ConvertJson5StringsAndStripComments(text)));

    private static string ConvertJson5StringsAndStripComments(string input)
    {
        var output = new System.Text.StringBuilder(input.Length);
        var inDouble = false;
        var inSingle = false;
        var escaped = false;
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (inDouble)
            {
                output.Append(c);
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inDouble = false;
                continue;
            }
            if (inSingle)
            {
                if (escaped)
                {
                    escaped = false;
                    switch (c)
                    {
                        case '\'': output.Append('\''); break;
                        case '"': output.Append("\\\""); break;
                        case '\\': output.Append("\\\\"); break;
                        case 'b': output.Append("\\b"); break;
                        case 'f': output.Append("\\f"); break;
                        case 'n': output.Append("\\n"); break;
                        case 'r': output.Append("\\r"); break;
                        case 't': output.Append("\\t"); break;
                        case 'v': output.Append("\\u000B"); break;
                        case '0': output.Append("\\u0000"); break;
                        case 'x' when i + 2 < input.Length && byte.TryParse(input.AsSpan(i + 1, 2),
                            System.Globalization.NumberStyles.HexNumber, null, out var hexByte):
                            output.Append("\\u00").Append(hexByte.ToString("X2"));
                            i += 2;
                            break;
                        case 'u' when i + 4 < input.Length && ushort.TryParse(input.AsSpan(i + 1, 4),
                            System.Globalization.NumberStyles.HexNumber, null, out var hexWord):
                            output.Append("\\u").Append(hexWord.ToString("X4"));
                            i += 4;
                            break;
                        case '\r':
                            if (i + 1 < input.Length && input[i + 1] == '\n') i++;
                            break; // JSON5 line continuation.
                        case '\n': break;
                        default: output.Append(c); break;
                    }
                    continue;
                }
                if (c == '\\') { escaped = true; continue; }
                if (c == '\'') { inSingle = false; output.Append('"'); continue; }
                if (c == '"') { output.Append("\\\""); continue; }
                if (c == '\r') { output.Append("\\r"); continue; }
                if (c == '\n') { output.Append("\\n"); continue; }
                output.Append(c);
                continue;
            }

            if (c == '"') { inDouble = true; output.Append(c); continue; }
            if (c == '\'') { inSingle = true; output.Append('"'); continue; }
            if (c == '/' && i + 1 < input.Length && input[i + 1] == '/')
            {
                i += 2;
                while (i < input.Length && input[i] is not '\r' and not '\n') i++;
                if (i < input.Length) output.Append(input[i]);
                continue;
            }
            if (c == '/' && i + 1 < input.Length && input[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < input.Length && !(input[i] == '*' && input[i + 1] == '/')) i++;
                if (i + 1 < input.Length) i++;
                output.Append(' ');
                continue;
            }
            output.Append(c);
        }
        return output.ToString();
    }

    private static string QuoteJson5Keys(string input)
    {
        var output = new System.Text.StringBuilder(input.Length + 32);
        var inString = false;
        var escaped = false;
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (inString)
            {
                output.Append(c);
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; output.Append(c); continue; }
            output.Append(c);
            if (c is not '{' and not ',') continue;

            var keyStart = i + 1;
            while (keyStart < input.Length && char.IsWhiteSpace(input[keyStart])) keyStart++;
            if (keyStart >= input.Length || !(char.IsLetter(input[keyStart]) || input[keyStart] is '_' or '$')) continue;
            var keyEnd = keyStart + 1;
            while (keyEnd < input.Length && (char.IsLetterOrDigit(input[keyEnd]) || input[keyEnd] is '_' or '$')) keyEnd++;
            var colon = keyEnd;
            while (colon < input.Length && char.IsWhiteSpace(input[colon])) colon++;
            if (colon >= input.Length || input[colon] != ':') continue;

            output.Append(input.AsSpan(i + 1, keyStart - (i + 1)));
            output.Append('"').Append(input.AsSpan(keyStart, keyEnd - keyStart)).Append('"');
            output.Append(input.AsSpan(keyEnd, colon - keyEnd)).Append(':');
            i = colon;
        }
        return output.ToString();
    }

    private static string RemoveJson5TrailingCommas(string input)
    {
        var output = new System.Text.StringBuilder(input.Length);
        var inString = false;
        var escaped = false;
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (inString)
            {
                output.Append(c);
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; output.Append(c); continue; }
            if (c == ',')
            {
                var next = i + 1;
                while (next < input.Length && char.IsWhiteSpace(input[next])) next++;
                if (next < input.Length && input[next] is '}' or ']')
                {
                    output.Append(input.AsSpan(i + 1, next - (i + 1)));
                    i = next - 1;
                    continue;
                }
            }
            output.Append(c);
        }
        return output.ToString();
    }

    private static bool TryGet(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
        value = default;
        return false;
    }

    private static string GetString(JsonElement element, string propertyName) =>
        TryGet(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string? FindVst3Binary(string bundlePath)
    {
        var contents = Path.Combine(bundlePath, "Contents");
        var preferred = new[]
        {
            Path.Combine(contents, "x86_64-win"),
            Path.Combine(contents, "x86-win"),
            Path.Combine(contents, "arm64ec-win"),
            Path.Combine(contents, "arm64x-win"),
            Path.Combine(contents, "arm64-win")
        };
        foreach (var directory in preferred)
        {
            if (!Directory.Exists(directory)) continue;
            try
            {
                var binary = Directory.EnumerateFiles(directory, "*.vst3", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (binary is not null) return binary;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    private static PluginArchitecture DetectVst3Architecture(string path, bool isDirectory,
        PluginArchitecture? binaryArchitecture, out string detail)
    {
        if (isDirectory)
        {
            var contents = Path.Combine(path, "Contents");
            var x64 = Directory.Exists(Path.Combine(contents, "x86_64-win"));
            var x86 = Directory.Exists(Path.Combine(contents, "x86-win"));
            var arm64 = Directory.Exists(Path.Combine(contents, "arm64ec-win")) ||
                        Directory.Exists(Path.Combine(contents, "arm64x-win")) ||
                        Directory.Exists(Path.Combine(contents, "arm64-win"));
            if (x64 && x86)
            {
                detail = "64-bit + 32-bit bundle (kept whole)";
                return PluginArchitecture.X64;
            }
            if (x64)
            {
                detail = "64-bit (x64-win)";
                return PluginArchitecture.X64;
            }
            if (x86)
            {
                detail = "32-bit (x86-win)";
                return PluginArchitecture.X86;
            }
            if (arm64)
            {
                detail = "ARM64 / ARM64EC (manual review)";
                return PluginArchitecture.Arm64;
            }
        }

        var architecture = binaryArchitecture ?? PluginArchitecture.Unknown;
        detail = ArchitectureText(architecture);
        return architecture;
    }

    private static bool IsAudioModuleClass(Vst3ClassInfo classInfo) =>
        classInfo.Category.Equals("Audio Module Class", StringComparison.OrdinalIgnoreCase);

    private static bool IsNonAudioHelperClass(Vst3ClassInfo classInfo) =>
        classInfo.Category.Equals("Component Controller Class", StringComparison.OrdinalIgnoreCase) ||
        classInfo.Category.Equals("Plugin Compatibility Class", StringComparison.OrdinalIgnoreCase) ||
        classInfo.Category.Equals("Test Class", StringComparison.OrdinalIgnoreCase);

    private static bool HasMultiplePluginComponents(Vst3ModuleInfo? moduleInfo)
    {
        var classes = moduleInfo?.Classes ?? new List<Vst3ClassInfo>();
        if (classes.Count <= 1) return false;
        var audioClassCount = classes.Count(IsAudioModuleClass);
        if (audioClassCount == 1 && classes.Where(c => !IsAudioModuleClass(c)).All(IsNonAudioHelperClass))
            return false;
        var hasUnclassified = classes.Any(c => string.IsNullOrWhiteSpace(c.Category));
        return audioClassCount > 1 || audioClassCount == 0 || hasUnclassified;
    }

    private static bool IsVst2Entry(string export)
    {
        var normalized = export.TrimStart('_');
        var at = normalized.IndexOf('@');
        if (at >= 0) normalized = normalized[..at];
        return normalized.Equals("VSTPluginMain", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("main", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVst3Entry(string export) =>
        export.Equals("GetPluginFactory", StringComparison.OrdinalIgnoreCase) ||
        export.Equals("ModuleEntry", StringComparison.OrdinalIgnoreCase);

    private static string ArchitectureText(PluginArchitecture architecture) => architecture switch
    {
        PluginArchitecture.X64 => "64-bit (x64)",
        PluginArchitecture.X86 => "32-bit (x86)",
        PluginArchitecture.Arm64 => "ARM64 (manual review)",
        _ => "Unknown (manual review)"
    };

    private static string ResolveIdentityName(string?[] candidates, string fileName, string? vendor)
    {
        var metadataName = candidates.FirstOrDefault(n => Useful(n) && !IsGenericWrapperName(n, vendor));
        if (string.IsNullOrWhiteSpace(metadataName)) return FirstUseful(candidates);
        if (!string.IsNullOrWhiteSpace(fileName) && Useful(fileName) &&
            !IsGenericWrapperName(fileName, vendor) && AddsProductQualifier(fileName, metadataName, vendor))
            return fileName.Trim();
        return metadataName.Trim();
    }

    private static bool AddsProductQualifier(string fileName, string metadataName, string? vendor)
    {
        var fileKey = PluginNameNormalizer.Normalize(fileName, vendor).CoreNameKey;
        var metadataKey = PluginNameNormalizer.Normalize(metadataName, vendor).CoreNameKey;
        if (string.Equals(fileKey, metadataKey, StringComparison.OrdinalIgnoreCase)) return false;

        var metadataTokens = IdentityTokenPattern.Matches(metadataName)
            .Select(m => m.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fileTokens = IdentityTokenPattern.Matches(fileName)
            .Select(m => m.Value)
            .ToList();
        return fileTokens.Where(t => !metadataTokens.Contains(t)).Any(t =>
            ChannelQualifierTokens.Contains(t) ||
            (t.Any(char.IsDigit) && !ArchitectureTokens.Contains(t)));
    }

    private static bool IsGenericWrapperName(string? value, string? vendor = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var compact = new string(value.Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        if (compact.Contains("vst3wrapper", StringComparison.Ordinal) ||
            compact.Contains("vstwrapper", StringComparison.Ordinal) ||
            compact.Contains("pluginwrapper", StringComparison.Ordinal) ||
            compact.Contains("audiowrapper", StringComparison.Ordinal) ||
            compact.Equals("wrapper", StringComparison.Ordinal) ||
            compact.Equals("vst3plugin", StringComparison.Ordinal) ||
            compact.Equals("audioplugin", StringComparison.Ordinal))
            return true;

        if (!string.IsNullOrWhiteSpace(vendor))
        {
            var normalized = PluginNameNormalizer.Normalize(value, vendor);
            if (!string.IsNullOrWhiteSpace(normalized.VendorKey) &&
                string.Equals(normalized.NameKey, normalized.VendorKey, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static FileVersionInfo? SafeVersionInfo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return FileVersionInfo.GetVersionInfo(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static bool Useful(string? value) => !string.IsNullOrWhiteSpace(value) &&
        !value.Trim().Equals("VST PlugIn", StringComparison.OrdinalIgnoreCase) &&
        !value.Trim().Equals("Plug-in", StringComparison.OrdinalIgnoreCase) &&
        !value.Trim().Equals("Plugin", StringComparison.OrdinalIgnoreCase);

    private static string FirstUseful(params string?[] values) =>
        values.FirstOrDefault(Useful)?.Trim() ?? "";

    private sealed class Vst3ModuleInfo
    {
        public string Name { get; init; } = "";
        public string Vendor { get; init; } = "";
        public string Version { get; init; } = "";
        public List<Vst3ClassInfo> Classes { get; init; } = new();
    }

    private sealed class Vst3ClassInfo
    {
        public string Category { get; init; } = "";
        public string Name { get; init; } = "";
        public string Vendor { get; init; } = "";
        public string Version { get; init; } = "";
        public string ClassId { get; init; } = "";
    }
}
