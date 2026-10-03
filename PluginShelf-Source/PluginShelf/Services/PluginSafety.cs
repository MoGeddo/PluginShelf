using System.Text.RegularExpressions;

namespace PluginShelf.Services;

public static class PluginSafety
{
    private static readonly Regex TokenSeparator = new("[^\\p{L}\\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsProtectedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = path.Replace('/', '\\').TrimEnd('\\');
        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Any(p => p.Equals("WPAPI", StringComparison.OrdinalIgnoreCase))) return true;
        if (parts.Any(ContainsWavesToken)) return true;
        if (parts.Any(p => p.Equals("PluginShelf_Quarantine", StringComparison.OrdinalIgnoreCase))) return true;
        return false;
    }

    public static bool IsProtectedCandidate(string path, string? vendor = null, string? name = null)
    {
        if (IsProtectedPath(path)) return true;
        if (ContainsWavesToken(vendor) || ContainsWavesToken(name)) return true;
        return false;
    }

    public static bool IsSupportedPath(string path, bool isDirectory)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".vst3", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".clap", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReasonableScanRoot(string? path, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "Folder path is empty.";
            return false;
        }
        try
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (new[] { ".dll", ".vst3", ".clap" }.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase))
            {
                reason = "Choose a containing search folder, not an individual plug-in file or VST3 package.";
                return false;
            }
            var driveRoot = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(full, driveRoot, StringComparison.OrdinalIgnoreCase))
            {
                reason = "A drive root is too broad for a safe plug-in scan.";
                return false;
            }
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrWhiteSpace(windows) &&
                (string.Equals(full, windows, StringComparison.OrdinalIgnoreCase) ||
                 full.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                reason = "Windows system folders are never scanned.";
                return false;
            }
            var broadRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            }.Where(p => !string.IsNullOrWhiteSpace(p));
            if (broadRoots.Any(p => string.Equals(full, p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)))
            {
                reason = "Choose a specific plug-in folder, not the entire Program Files directory.";
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            reason = "Folder path is invalid.";
            return false;
        }
    }

    private static bool ContainsWavesToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return TokenSeparator.Split(value).Any(IsWavesToken);
    }

    private static bool IsWavesToken(string token) =>
        token.Equals("Waves", StringComparison.OrdinalIgnoreCase) ||
        token.StartsWith("WaveShell", StringComparison.OrdinalIgnoreCase);
}
