using PluginShelf.Models;
using PluginShelf.Services;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

static PluginCandidate Candidate(string name, string vendor, PluginFormat format, PluginArchitecture architecture,
    string? path = null, string? version = null)
{
    var normalized = PluginNameNormalizer.Normalize(name, vendor);
    return new PluginCandidate
    {
        Name = name,
        Vendor = vendor,
        Version = version ?? "",
        Format = format,
        Architecture = architecture,
        ArchitectureDetail = architecture switch
        {
            PluginArchitecture.X64 => "64-bit (x64)",
            PluginArchitecture.X86 => "32-bit (x86)",
            _ => "Unknown"
        },
        Path = path ?? $@"C:\Audio\{format}\{name}.{(format == PluginFormat.Vst ? "dll" : format == PluginFormat.Vst3 ? "vst3" : "clap")}",
        IsLikelyPlugin = true,
        NameKey = normalized.NameKey,
        CoreNameKey = normalized.CoreNameKey,
        VendorKey = normalized.VendorKey
    };
}

var generation3 = Candidate("FabFilter Pro-Q 3", "FabFilter", PluginFormat.Vst3, PluginArchitecture.X64);
var generation4 = Candidate("FabFilter Pro-Q 4", "FabFilter", PluginFormat.Vst, PluginArchitecture.X64);
Assert(PluginGroupBuilder.Build([generation3, generation4]).Count == 0,
    "Pro-Q 3 and Pro-Q 4 remain separate products");

var vst3 = Candidate("FabFilter Pro-Q 4", "FabFilter", PluginFormat.Vst3, PluginArchitecture.X64);
var vst2 = Candidate("FabFilter Pro-Q 4", "FabFilter", PluginFormat.Vst, PluginArchitecture.X64);
var clap = Candidate("FabFilter Pro-Q 4", "FabFilter", PluginFormat.Clap, PluginArchitecture.X64);
var group = PluginGroupBuilder.Build([vst2, clap, vst3]).Single();
Assert(group.RecommendedKeepId == vst3.Id, "64-bit VST3 wins over 64-bit VST and CLAP");

var noVst3 = PluginGroupBuilder.Build([
    Candidate("Nova", "Acme Audio", PluginFormat.Vst, PluginArchitecture.X64),
    Candidate("Nova", "Acme Audio", PluginFormat.Clap, PluginArchitecture.X64)
]).Single();
Assert(noVst3.Candidates.Single(c => c.Id == noVst3.RecommendedKeepId).Format == PluginFormat.Vst,
    "VST/DLL 64 wins when no VST3 is present");

var x86Formats = PluginGroupBuilder.Build([
    Candidate("Tone32", "Acme Audio", PluginFormat.Vst3, PluginArchitecture.X86, version: "1.0"),
    Candidate("Tone32", "Acme Audio", PluginFormat.Vst, PluginArchitecture.X86, version: "9.0"),
    Candidate("Tone32", "Acme Audio", PluginFormat.Clap, PluginArchitecture.X86, version: "10.0")
]).Single();
Assert(x86Formats.Candidates.Single(c => c.Id == x86Formats.RecommendedKeepId).Format == PluginFormat.Vst3,
    "When only 32-bit candidates exist, the same VST3 > VST/DLL > CLAP priority applies");

var archFirst = PluginGroupBuilder.Build([
    Candidate("Tone", "Acme Audio", PluginFormat.Vst3, PluginArchitecture.X86),
    Candidate("Tone", "Acme Audio", PluginFormat.Vst, PluginArchitecture.X64)
]).Single();
Assert(archFirst.Candidates.Single(c => c.Id == archFirst.RecommendedKeepId).Format == PluginFormat.Vst,
    "64-bit VST is preferred over a 32-bit VST3 candidate");

var formatFirst = PluginGroupBuilder.Build([
    Candidate("Volcano 3", "FabFilter", PluginFormat.Vst3, PluginArchitecture.X64, version: "3.07"),
    Candidate("Volcano 3", "FabFilter", PluginFormat.Vst, PluginArchitecture.X64, version: "3.03"),
    Candidate("Volcano 3", "FabFilter", PluginFormat.Clap, PluginArchitecture.X64, version: "3.08")
]).Single();
Assert(formatFirst.Candidates.Single(c => c.Id == formatFirst.RecommendedKeepId).Format == PluginFormat.Vst3,
    "Preferred format wins over newer releases in lower-priority formats");

var latestWithinFormat = PluginGroupBuilder.Build([
    Candidate("Volcano 3", "FabFilter", PluginFormat.Vst3, PluginArchitecture.X64, version: "3.06"),
    Candidate("Volcano 3", "FabFilter", PluginFormat.Vst3, PluginArchitecture.X64, version: "3.07"),
    Candidate("Volcano 3", "FabFilter", PluginFormat.Vst, PluginArchitecture.X64, version: "3.50"),
    Candidate("Volcano 3", "FabFilter", PluginFormat.Clap, PluginArchitecture.X64, version: "4.00")
]).Single();
Assert(latestWithinFormat.Candidates.Single(c => c.Id == latestWithinFormat.RecommendedKeepId).Version == "3.07",
    "Newest embedded release is chosen within the preferred architecture and format; file dates are ignored");

var versionTie = PluginGroupBuilder.Build([
    Candidate("Filter", "Acme Audio", PluginFormat.Vst3, PluginArchitecture.X64,
        @"C:\Audio\Vst3\Filter-a.vst3", version: "3.08"),
    Candidate("Filter", "Acme Audio", PluginFormat.Vst3, PluginArchitecture.X64,
        @"D:\Audio\Vst3\Filter-b.vst3", version: "3.8.0")
]).Single();
Assert(versionTie.HasConflictingTopCandidates && versionTie.RecommendedKeepId is null,
    "Same-format candidates with equal release versions require manual review");

var uncertainVersion = PluginGroupBuilder.Build([
    Candidate("Filter", "Acme Audio", PluginFormat.Vst3, PluginArchitecture.X64,
        @"C:\Audio\Vst3\Filter-readable.vst3", version: "3.08"),
    Candidate("Filter", "Acme Audio", PluginFormat.Vst3, PluginArchitecture.X64,
        @"D:\Audio\Vst3\Filter-unknown.vst3")
]).Single();
Assert(uncertainVersion.HasConflictingTopCandidates && uncertainVersion.RecommendedKeepId is null,
    "Mixed readable and missing releases in the preferred format require manual review");

var aliasA = Candidate("FabFilter Pro-Q 4", "FabFilter", PluginFormat.Vst3, PluginArchitecture.X64);
var aliasB = Candidate("Pro-Q4", "FabFilter", PluginFormat.Vst, PluginArchitecture.X64);
var alias = PluginGroupBuilder.Build([aliasA, aliasB]).Single();
Assert(alias.NeedsIdentityConfirmation && alias.SelectedKeepId is null,
    "Vendor-prefixed aliases are suggested but require manual confirmation");

var n3 = PluginNameNormalizer.Normalize("Pro-Q 3", "FabFilter");
var n4 = PluginNameNormalizer.Normalize("Pro-Q 4", "FabFilter");
Assert(n3.CoreNameKey != n4.CoreNameKey, "Model generation digits are preserved during normalization");
var bitnessUnderscore = PluginNameNormalizer.Normalize("Synth_64-bit", "Acme Audio");
var bitnessX64 = PluginNameNormalizer.Normalize("Synth_x64", "Acme Audio");
var bitnessPlain = PluginNameNormalizer.Normalize("Synth", "Acme Audio");
Assert(bitnessUnderscore.CoreNameKey == bitnessPlain.CoreNameKey && bitnessX64.CoreNameKey == bitnessPlain.CoreNameKey,
    "Underscore-attached bitness qualifiers are stripped cleanly during normalization");
var model64 = PluginNameNormalizer.Normalize("C64 Synth", "Acme Audio");
Assert(model64.CoreNameKey.Contains("c64", StringComparison.Ordinal),
    "Model numbers like C64 remain intact during normalization");
var volcano2 = PluginNameNormalizer.Normalize("FabFilter Volcano 2", "FabFilter");
var volcano3 = PluginNameNormalizer.Normalize("FabFilter Volcano 3", "FabFilter");
var volcanoMono = PluginNameNormalizer.Normalize("FabFilter Volcano 3 (Mono)", "FabFilter");
var volcanoStereo = PluginNameNormalizer.Normalize("FabFilter Volcano 3 (Stereo)", "FabFilter");
var volcanoMonoSc = PluginNameNormalizer.Normalize("FabFilter Volcano 3 (Mono SC)", "FabFilter");
Assert(volcano2.CoreNameKey != volcano3.CoreNameKey,
    "Volcano 2 and Volcano 3 remain separate product generations");
Assert(volcanoMono.CoreNameKey != volcanoStereo.CoreNameKey && volcanoMono.CoreNameKey != volcanoMonoSc.CoreNameKey,
    "Mono, stereo, and mono-sidechain wrappers are separate identities");
var monoLatest = PluginGroupBuilder.Build([
    Candidate("FabFilter Volcano 2 (Mono)", "FabFilter", PluginFormat.Vst3, PluginArchitecture.X64, version: "2.39"),
    Candidate("FabFilter Volcano 2 (Mono)", "FabFilter", PluginFormat.Vst, PluginArchitecture.X64, version: "2.32")
]).Single();
Assert(monoLatest.Candidates.Single(c => c.Id == monoLatest.RecommendedKeepId).Version == "2.39",
    "Latest release is selected independently inside the fixed-mono variant");
Assert(PluginGroupBuilder.Build([
        Candidate("FabFilter Volcano 2 (Mono)", "FabFilter", PluginFormat.Vst, PluginArchitecture.X64),
        Candidate("FabFilter Volcano 2 (Stereo)", "FabFilter", PluginFormat.Vst3, PluginArchitecture.X64)
    ]).Count == 0,
    "Mono and stereo copies are never grouped as duplicates");
Assert(AppSettings.CreateDefaultRoots().Count == 11, "The supplied search list is loaded without the protected WPAPI path");
Assert(AppSettings.CreateDefaultRoots().All(r => !PluginSafety.IsProtectedPath(r.Path)), "Default search folders contain no Waves/WPAPI path");
Assert(PluginSafety.IsProtectedPath(@"C:\Program Files (x86)\Common Files\WPAPI"), "WPAPI is protected");
Assert(PluginSafety.IsProtectedCandidate(@"C:\Audio\VST3\WaveShell.vst3", "Waves Audio", "WaveShell"),
    "Waves and WaveShell items are excluded");

var sameVstA = Candidate("Equalizer", "Acme Audio", PluginFormat.Vst, PluginArchitecture.X64,
    @"C:\Audio\Vst\Equalizer.dll");
var sameVstB = Candidate("Equalizer", "Acme Audio", PluginFormat.Vst, PluginArchitecture.X64,
    @"C:\Audio\AltVst\Equalizer.dll");
var sameFormatGroup = PluginGroupBuilder.Build([sameVstA, sameVstB]).Single();
Assert(sameFormatGroup.HasConflictingTopCandidates && sameFormatGroup.RecommendedKeepId is null,
    "Same-format, same-architecture duplicates are shown for manual review, not ignored or auto-selected");

var tiedVst3A = Candidate("Equalizer", "Acme Audio", PluginFormat.Vst3, PluginArchitecture.X64,
    @"C:\Audio\Vst3\Equalizer.vst3");
var tiedVst3B = Candidate("Equalizer", "Acme Audio", PluginFormat.Vst3, PluginArchitecture.X64,
    @"C:\Audio\AltVst3\Equalizer.vst3");
var tiedVst2 = Candidate("Equalizer", "Acme Audio", PluginFormat.Vst, PluginArchitecture.X64);
var tieGroup = PluginGroupBuilder.Build([tiedVst3A, tiedVst3B, tiedVst2]).Single();
Assert(tieGroup.HasConflictingTopCandidates && tieGroup.RecommendedKeepId is null,
    "Equal-priority copies require a manual keeper choice");

var x64Fixture = CreatePeFixture(machine: 0x8664, optionalMagic: 0x20B);
var x86Fixture = CreatePeFixture(machine: 0x014C, optionalMagic: 0x10B);
try
{
    var x64Path = Path.Combine(Path.GetTempPath(), $"pluginshelf-x64-{Guid.NewGuid():N}.dll");
    var x86Path = Path.Combine(Path.GetTempPath(), $"pluginshelf-x86-{Guid.NewGuid():N}.dll");
    File.WriteAllBytes(x64Path, x64Fixture);
    File.WriteAllBytes(x86Path, x86Fixture);
    Assert(PeInspector.Inspect(x64Path)?.Architecture == PluginArchitecture.X64, "Static PE header inspection detects x64");
    Assert(PeInspector.Inspect(x86Path)?.Architecture == PluginArchitecture.X86, "Static PE header inspection detects x86");
    File.Delete(x64Path);
    File.Delete(x86Path);
}
finally { }

var tempRoot = Path.Combine(Path.GetTempPath(), $"pluginshelf-scan-{Guid.NewGuid():N}");
try
{
    var bundle = Path.Combine(tempRoot, "Example EQ.vst3");
    var binaryFolder = Path.Combine(bundle, "Contents", "x86_64-win");
    var resourceFolder = Path.Combine(bundle, "Contents", "Resources");
    Directory.CreateDirectory(binaryFolder);
    Directory.CreateDirectory(resourceFolder);
    File.WriteAllBytes(Path.Combine(binaryFolder, "Example EQ.vst3"), x64Fixture);
    File.WriteAllText(Path.Combine(resourceFolder, "moduleinfo.json"), """
        {
          // JSON5-style module metadata: comments, bare keys, single quotes, trailing commas.
          Name: 'Example EQ',
          Vendor: 'Acme Audio',
          Version: '1.0',
          Classes: [
            { Category: 'Audio Module Class', Name: 'Example EQ', Vendor: 'Acme Audio', Version: '1.0', },
            { Category: 'Component Controller Class', Name: 'Example EQ Controller', Vendor: 'Acme Audio', Version: '1.0', },
          ],
        }
        """);

    // Roland's bundles can share a generic moduleinfo class name even though each
    // bundle is a different product. The folder name must win over that wrapper label.
    foreach (var product in new[] { "BOSS Effects Pedals", "D-50" })
    {
        var productBundle = Path.Combine(tempRoot, "Roland", product + ".vst3");
        var productBinary = Path.Combine(productBundle, "Contents", "x86_64-win");
        var productResources = Path.Combine(productBundle, "Contents", "Resources");
        Directory.CreateDirectory(productBinary);
        Directory.CreateDirectory(productResources);
        File.WriteAllBytes(Path.Combine(productBinary, product + ".vst3"), x64Fixture);
        File.WriteAllText(Path.Combine(productResources, "moduleinfo.json"),
            "{\"Name\":\"Roland VST3 Wrapper\",\"Vendor\":\"Roland\",\"Version\":\"2.0.0.1\",\"Classes\":[{\"Name\":\"Roland VST3 Wrapper\",\"Vendor\":\"Roland\",\"Version\":\"2.0.0.1\"}]}");
    }

    // File/package names retain the generation even when the manufacturer's metadata omits it.
    foreach (var product in new[] { "FabFilter Volcano 2", "FabFilter Volcano 3" })
    {
        var release = product.EndsWith("2", StringComparison.Ordinal) ? "2.39" : "3.07";
        var productBundle = Path.Combine(tempRoot, product + ".vst3");
        var productBinary = Path.Combine(productBundle, "Contents", "x86_64-win");
        var productResources = Path.Combine(productBundle, "Contents", "Resources");
        Directory.CreateDirectory(productBinary);
        Directory.CreateDirectory(productResources);
        File.WriteAllBytes(Path.Combine(productBinary, product + ".vst3"), x64Fixture);
        File.WriteAllText(Path.Combine(productResources, "moduleinfo.json"),
            $"{{\"Name\":\"FabFilter Volcano\",\"Vendor\":\"FabFilter\",\"Version\":\"{release}\",\"Classes\":[{{\"Name\":\"FabFilter Volcano\",\"Vendor\":\"FabFilter\",\"Version\":\"{release}\"}}]}}");
    }

    // Shared non-distinguishing VST3 metadata (even without the word "Wrapper") falls back to the bundle name.
    foreach (var product in new[] { "Alpha Lead", "Beta Pad" })
    {
        var productBundle = Path.Combine(tempRoot, "SynthCo", product + ".vst3");
        var productBinary = Path.Combine(productBundle, "Contents", "x86_64-win");
        var productResources = Path.Combine(productBundle, "Contents", "Resources");
        Directory.CreateDirectory(productBinary);
        Directory.CreateDirectory(productResources);
        File.WriteAllBytes(Path.Combine(productBinary, product + ".vst3"), x64Fixture);
        File.WriteAllText(Path.Combine(productResources, "moduleinfo.json"),
            "{\"Name\":\"SynthCo Sound Engine\",\"Vendor\":\"SynthCo\",\"Version\":\"1.2.0\",\"Classes\":[{\"Category\":\"Component Controller Class\",\"Name\":\"SynthCo Controller\",\"Vendor\":\"SynthCo\",\"Version\":\"1.2.0\"},{\"Category\":\"Audio Module Class\",\"Name\":\"SynthCo Sound Engine\",\"Vendor\":\"SynthCo\",\"Version\":\"1.2.0\"}]}");
    }

    var wpapiFolder = Path.Combine(tempRoot, "WPAPI");
    Directory.CreateDirectory(wpapiFolder);
    File.WriteAllBytes(Path.Combine(wpapiFolder, "WavesPublicApi.clap"), x64Fixture);
    var wavesFolder = Path.Combine(tempRoot, "Waves");
    Directory.CreateDirectory(wavesFolder);
    File.WriteAllBytes(Path.Combine(wavesFolder, "WaveShell.vst3"), x64Fixture);

    Assert(!PluginSafety.IsReasonableScanRoot(bundle, out _), "A VST3 package cannot be treated as a recursive search root");
    var scanSettings = new AppSettings
    {
        Roots = new List<ScanRoot> { new() { Path = tempRoot, Kind = RootKind.Auto, Enabled = true } }
    };
    var scanResult = await new PluginScanner().ScanAsync(scanSettings, null, CancellationToken.None);
    Assert(scanResult.Candidates.Count == 7 && scanResult.Candidates.All(c => c.Format == PluginFormat.Vst3),
        "Recursive scan treats each VST3 bundle as one item and excludes WPAPI/Waves");
    Assert(scanResult.Candidates.All(c => c.Architecture == PluginArchitecture.X64 && c.IsBundle),
        "VST3 bundle architecture is detected from its architecture folder");
    Assert(!scanResult.Candidates.Single(c => c.Name == "Example EQ").IsMultiComponent,
        "VST3 controller classes are not mistaken for multiple plug-in products");
    var rolandProducts = scanResult.Candidates.Where(c => c.Vendor == "Roland").ToList();
    Assert(rolandProducts.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
               .SetEquals(new[] { "BOSS Effects Pedals", "D-50" }),
        "Generic Roland wrapper metadata falls back to each product-specific bundle folder name");
    Assert(PluginGroupBuilder.Build(rolandProducts).Count == 0,
        "Distinct Roland products with generic wrapper metadata are never grouped as duplicates");
    var synthCoProducts = scanResult.Candidates.Where(c => c.Vendor == "SynthCo").ToList();
    Assert(synthCoProducts.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
               .SetEquals(new[] { "Alpha Lead", "Beta Pad" }),
        "Shared non-distinguishing VST3 metadata falls back to each bundle's own name");
    Assert(synthCoProducts.All(c => !c.IsMultiComponent),
        "Controller class listed before Audio Module Class is not treated as an extra audio plug-in");
    Assert(PluginGroupBuilder.Build(synthCoProducts).Count == 0,
        "Distinct VST3 bundles sharing generic metadata are never merged");
    var volcanoProducts = scanResult.Candidates.Where(c => c.Vendor == "FabFilter").ToList();
    Assert(volcanoProducts.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
               .SetEquals(new[] { "FabFilter Volcano 2", "FabFilter Volcano 3" }),
        "Product filenames restore Volcano generations missing from generic file metadata");
    Assert(PluginGroupBuilder.Build(volcanoProducts).Count == 0,
        "Volcano 2 and Volcano 3 are never merged despite identical generic module metadata");
}
finally
{
    try { Directory.Delete(tempRoot, recursive: true); } catch { }
}

Console.WriteLine("All PluginShelf core checks passed.");

static byte[] CreatePeFixture(ushort machine, ushort optionalMagic)
{
    var bytes = new byte[0x200];
    void U16(int offset, ushort value) => Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 2);
    void U32(int offset, uint value) => Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
    U16(0x00, 0x5A4D); // MZ
    U32(0x3C, 0x80);   // e_lfanew
    U32(0x80, 0x00004550); // PE\0\0
    U16(0x84, machine);
    U16(0x86, 1); // section count
    U16(0x94, 0xF0); // SizeOfOptionalHeader
    U16(0x98, optionalMagic);
    return bytes;
}
