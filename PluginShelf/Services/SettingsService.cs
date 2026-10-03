using System.Text.Json;
using System.Text.Json.Serialization;
using PluginShelf.Models;

namespace PluginShelf.Services;

public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PluginShelf");

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public static string QuarantineIndexPath => Path.Combine(DataDirectory, "quarantine-index.json");

    public static JsonSerializerOptions CreateOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions);
            if (loaded is null) return new AppSettings();
            loaded.Roots ??= AppSettings.CreateDefaultRoots();
            loaded.Roots = loaded.Roots
                .Where(r => !string.IsNullOrWhiteSpace(r.Path) && !PluginSafety.IsProtectedPath(r.Path))
                .GroupBy(r => SafeNormalizePath(r.Path), StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .Select(g => g.First())
                .ToList();
            if (loaded.Roots.Count == 0) loaded.Roots = AppSettings.CreateDefaultRoots();
            loaded.Language = loaded.Language is "en" ? "en" : "ar";
            loaded.UiScale = double.IsFinite(loaded.UiScale) ? Math.Clamp(loaded.UiScale, 0.85, 1.5) : 1.0;
            return loaded;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var safeSettings = new AppSettings
        {
            Language = settings.Language == "en" ? "en" : "ar",
            UiScale = double.IsFinite(settings.UiScale) ? Math.Clamp(settings.UiScale, 0.85, 1.5) : 1.0,
            Roots = settings.Roots
                .Where(r => !string.IsNullOrWhiteSpace(r.Path) && !PluginSafety.IsProtectedPath(r.Path))
                .Select(r => new ScanRoot { Path = r.Path.Trim(), Kind = r.Kind, Enabled = r.Enabled })
                .ToList()
        };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(safeSettings, JsonOptions));
    }

    private static string SafeNormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
        }
        catch
        {
            return "";
        }
    }
}
