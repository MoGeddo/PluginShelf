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
                .GroupBy(r => Path.GetFullPath(r.Path.Trim()), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (loaded.Roots.Count == 0) loaded.Roots = AppSettings.CreateDefaultRoots();
            loaded.Language = loaded.Language is "en" ? "en" : "ar";
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
            Roots = settings.Roots
                .Where(r => !string.IsNullOrWhiteSpace(r.Path) && !PluginSafety.IsProtectedPath(r.Path))
                .Select(r => new ScanRoot { Path = r.Path.Trim(), Kind = r.Kind, Enabled = r.Enabled })
                .ToList()
        };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(safeSettings, JsonOptions));
    }
}
