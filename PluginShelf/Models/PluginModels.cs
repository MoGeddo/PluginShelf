using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace PluginShelf.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PluginFormat
{
    Vst,
    Vst3,
    Clap
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RootKind
{
    Auto,
    Vst,
    Vst3,
    Clap
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PluginArchitecture
{
    X64,
    X86,
    Arm64,
    Unknown
}

public sealed class ScanRoot
{
    public string Path { get; set; } = "";
    public RootKind Kind { get; set; } = RootKind.Auto;
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        RootKind.Vst => "VST / DLL",
        RootKind.Vst3 => "VST3",
        RootKind.Clap => "CLAP",
        _ => "Auto (known formats)"
    };
}

public sealed class PluginCandidate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Unknown plug-in";
    public string Vendor { get; init; } = "";
    public string Version { get; init; } = "";
    public string Path { get; init; } = "";
    public string BinaryPath { get; init; } = "";
    public PluginFormat Format { get; init; }
    public PluginArchitecture Architecture { get; init; } = PluginArchitecture.Unknown;
    public string ArchitectureDetail { get; init; } = "Unknown";
    public bool IsBundle { get; init; }
    public bool IsMultiComponent { get; init; }
    public bool IsLikelyPlugin { get; init; }
    public string DetectionNote { get; init; } = "";
    public string NameKey { get; init; } = "";
    public string CoreNameKey { get; init; } = "";
    public string VendorKey { get; init; } = "";

    [JsonIgnore]
    public string FormatLabel => Format switch
    {
        PluginFormat.Vst3 => "VST3",
        PluginFormat.Vst => "VST (.dll)",
        PluginFormat.Clap => "CLAP",
        _ => Format.ToString()
    };

    [JsonIgnore]
    public string ArchitectureLabel => string.IsNullOrWhiteSpace(ArchitectureDetail)
        ? Architecture switch
        {
            PluginArchitecture.X64 => "64-bit",
            PluginArchitecture.X86 => "32-bit",
            PluginArchitecture.Arm64 => "ARM64",
            _ => "Unknown"
        }
        : ArchitectureDetail;

    [JsonIgnore]
    public int ArchitectureRank => Architecture switch
    {
        PluginArchitecture.X64 => 0,
        PluginArchitecture.X86 => 1,
        _ => 9
    };

    [JsonIgnore]
    public int FormatRank => Format switch
    {
        PluginFormat.Vst3 => 0,
        PluginFormat.Vst => 1,
        PluginFormat.Clap => 2,
        _ => 9
    };
}

public sealed class PluginGroup
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string DisplayName { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string MatchSummary { get; init; } = "";
    public bool NeedsIdentityConfirmation { get; init; }
    public bool IsIdentityConfirmed { get; set; }
    public bool IncludeInPlan { get; set; }
    public bool IsSuggestedAlias { get; init; }
    public bool HasConflictingTopCandidates { get; init; }
    public ObservableCollection<PluginCandidate> Candidates { get; init; } = new();
    public Guid? RecommendedKeepId { get; init; }
    public Guid? SelectedKeepId { get; set; }

    [JsonIgnore]
    public string CandidateCountLabel => $"{Candidates.Count} candidates";
}

public sealed class ScanProgress
{
    public int RootsCompleted { get; init; }
    public int RootsTotal { get; init; }
    public int CandidatesFound { get; init; }
    public string CurrentPath { get; init; } = "";
}

public sealed class ScanResult
{
    public List<PluginCandidate> Candidates { get; } = new();
    public List<string> Warnings { get; } = new();
    public int RootsScanned { get; set; }
    public int RootsMissing { get; set; }
}

public sealed class AppSettings
{
    public string Language { get; set; } = "ar";
    public double UiScale { get; set; } = 1.0;
    public List<ScanRoot> Roots { get; set; } = CreateDefaultRoots();

    public static List<ScanRoot> CreateDefaultRoots() =>
    [
        new() { Path = @"C:\Program Files\Common Files\VST3", Kind = RootKind.Vst3 },
        new() { Path = @"C:\Program Files\Common Files\VST2", Kind = RootKind.Vst },
        new() { Path = @"C:\Program Files\VSTPlugins", Kind = RootKind.Auto },
        new() { Path = @"C:\Program Files\VstPlugins", Kind = RootKind.Auto },
        new() { Path = @"C:\Program Files\Steinberg\VSTPlugins", Kind = RootKind.Auto },
        new() { Path = @"C:\Program Files (x86)\Common Files\VST2", Kind = RootKind.Vst },
        new() { Path = @"C:\Program Files (x86)\Common Files\VST3", Kind = RootKind.Vst3 },
        new() { Path = @"C:\Program Files (x86)\VSTPlugins", Kind = RootKind.Auto },
        new() { Path = @"C:\Program Files (x86)\VstPlugins", Kind = RootKind.Auto },
        new() { Path = @"C:\Program Files (x86)\Steinberg\VSTPlugins", Kind = RootKind.Auto },
        new() { Path = @"C:\Program Files\Common Files\CLAP", Kind = RootKind.Clap }
    ];
}

public sealed class QuarantineEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PluginName { get; set; } = "";
    public string Format { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public string QuarantinedPath { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public DateTimeOffset MovedAt { get; set; } = DateTimeOffset.Now;
    public string Status { get; set; } = "Quarantined";
}

public sealed class ApplyPlanItem
{
    public string OriginalPath { get; set; } = "";
    public string PluginName { get; set; } = "";
    public string Format { get; set; } = "";
    public string Architecture { get; set; } = "";
}

public sealed class ApplyPlan
{
    public string CreatedBy { get; set; } = "PluginShelf";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public List<ApplyPlanItem> Items { get; set; } = new();
}

public sealed class RestorePlan
{
    public List<Guid> EntryIds { get; set; } = new();
}
