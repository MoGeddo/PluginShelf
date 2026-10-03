# Plugin Shelf — portable Windows plug-in format manager

Plugin Shelf is a Windows x64 desktop utility for reviewing duplicate plug-in **formats**. It is deliberately conservative: it scans metadata without loading plug-in code, previews every proposed action, and moves approved extras to a restorable quarantine instead of deleting them.

## Current rules

- Ranking is explicit: architecture first (x64, then x86 only if no x64 candidate exists), then format (VST3 > VST/DLL > CLAP), then the highest readable embedded plug-in release within that architecture/format tier and the exact product-generation/I/O identity. A newer release in a lower-priority format does not outrank a higher-priority format. If two or more candidates share the winning architecture/format tier, the keeper resolves deterministically: a readable embedded release outranks missing metadata, then the newest readable release, then the nearest/shorter canonical path. Only ambiguous aliases require a manual choice. File modification dates are never used.
- Product/model numbers and I/O qualifiers remain part of identity: for example, Pro-Q 3 and Pro-Q 4, Volcano 2 and Volcano 3, Mono and Stereo, and side-chain variants are not merged just because metadata names are similar.
- VST3 bundles are treated as a single package. If a bundle contains x64 and x86 binaries, the entire bundle is retained; its internal files are not split or deleted. If plug-in metadata reports a generic wrapper or omits a model/channel qualifier, the VST3 bundle or binary file's own name supplies the missing identity detail.
- FabFilter legacy VST2 suffixes such as `(Mono)`, `(SC)`, and `(Mono SC)` are retained as distinct I/O variants. The ordinary Volcano 2 VST2 and VST3 versions adapt to the track's mono/stereo layout; the fixed-mono VST2 wrappers exist for compatibility. Side-chain wrappers remain distinct because they expose different I/O.
- A plug-in with only one recognized candidate is left alone.
- Exact vendor + normalized product, generation, and I/O variant can form a review group. Possible name aliases (such as a vendor-prefixed name versus a short name) are suggestions only and require explicit confirmation in the UI.
- If multiple candidates in the winning architecture/format tier tie on release version or their versions cannot safely be compared, the keeper is resolved deterministically (readable release first, then nearest/shorter path); there is no timestamp fallback and no manual tie prompt.
- Waves, WaveShell and WPAPI paths/items are excluded. The supplied WPAPI directory is not included in the scan roots.
- This first release supports VST/DLL, VST3 and CLAP. Custom folders can be added in Settings. Adding new format types is intentionally deferred until their detection rules are defined.

## Safety behavior

1. **Scan is read-only.** The scanner reads PE headers, file-version resources, and VST3 `moduleinfo.json`; it does not load or execute any plug-in DLL/CLAP binary.
2. **No automatic action.** Groups are not included in the plan until you opt in; the "Select strong recommendations" shortcut excludes ambiguous aliases and equal-priority ties. Ambiguous name matches require a separate identity confirmation, and you choose the item to keep.
3. **No permanent deletion.** Apply moves the selected extras into `PluginShelf_Quarantine` on the same drive and records the original path in `%LOCALAPPDATA%\PluginShelf\quarantine-index.json` and a per-item `manifest.json` beside the quarantined payload.
4. **Restore is non-overwriting.** If the original path is occupied, Plugin Shelf stops instead of replacing anything.
5. **Waves/WPAPI are protected twice:** they are omitted from defaults, and safety checks reject matching paths/vendor/name values during scan, save, apply, and restore.
6. **UAC is requested only at apply/restore time.** If Windows elevation is canceled, the approved plan is not carried out.
7. The tool does not uninstall plug-ins, edit the Windows registry, modify FL Studio's plug-in database, or change the user's FL Studio project files. After applying a plan, rescan plug-ins in FL Studio.

## Run the portable app

A self-contained Windows x64 executable is provided in the `PluginShelf-Portable` folder in the delivery package. It needs no .NET runtime installation. Copy `PluginShelf.exe` to a convenient folder and run it. Windows may show a SmartScreen warning because this personal utility is not code-signed; only run the binary you received from this project.

To rebuild from source on Windows:

1. Install the **.NET 10 SDK**.
2. Run `build_portable.bat`.
3. The self-contained output appears in `PluginShelf/portable/PluginShelf.exe`.

The app targets the .NET 10 LTS line. Microsoft's support policy lists .NET 10 as supported through November 14, 2028; the portable build bundles its runtime and needs no separate .NET installation: <https://dotnet.microsoft.com/en-us/platform/support/policy>.

## Default search folders

The initial list is based on the supplied `plugin-paths.txt`, with the Waves WPAPI entry intentionally excluded:

- `C:\Program Files\Common Files\VST3`
- `C:\Program Files\Common Files\VST2`
- `C:\Program Files\VSTPlugins`
- `C:\Program Files\VstPlugins`
- `C:\Program Files\Steinberg\VSTPlugins`
- `C:\Program Files (x86)\Common Files\VST2`
- `C:\Program Files (x86)\Common Files\VST3`
- `C:\Program Files (x86)\VSTPlugins`
- `C:\Program Files (x86)\VstPlugins`
- `C:\Program Files (x86)\Steinberg\VSTPlugins`
- `C:\Program Files\Common Files\CLAP`

Missing folders are reported and skipped. Custom folders can be added in Settings; drive roots, Windows system folders, and individual plug-in files/VST3 bundles are rejected as unsafe scan roots.

## Run the core checks

From the source package root, with the .NET 10 SDK installed:

```sh
dotnet run --project tests/PluginShelf.CoreTests/PluginShelf.CoreTests.csproj -c Release
```

The dependency-free checks cover generation/channel identity, semantic-version ranking, deterministic tie-breaking and aliases, protected paths, static PE headers, JSON5 VST3 metadata, controller-class handling, and recursive VST3/Waves/WPAPI scans.

## Technical notes and references

- Image-Line's FL Studio manual lists Windows support for 32/64-bit VST 1/2, VST3 and CLAP: <https://www.image-line.com/fl-studio-learning/fl-studio-online-manual/html/plugins_supported.htm>
- Steinberg documents Windows VST3 as a bundle-like `.vst3` package with architecture-specific locations such as `x86-win` and `x86_64-win`: <https://steinbergmedia.github.io/vst3_dev_portal/pages/Technical+Documentation/Locations+Format/Plugin+Format.html>
- Steinberg's `moduleinfo.json` is JSON5-compatible and separates module metadata from class metadata (including audio-component and controller classes): <https://steinbergmedia.github.io/vst3_dev_portal/pages/Technical+Documentation/VST+Module+Architecture/ModuleInfo-JSON.html>
- Steinberg's standard Windows VST3 locations: <https://steinbergmedia.github.io/vst3_dev_portal/pages/Technical+Documentation/Locations+Format/Plugin+Locations.html>
- FabFilter's Volcano 2 manual documents four VST2 I/O wrappers: adaptive channel layout with/without side-chain, plus fixed-mono versions with/without side-chain: <https://www.fabfilter.com/downloads/pdf/help/ffvolcano2-manual.pdf>
- FabFilter says Volcano 3's VST2/VST3 plug-ins adapt to the track's mono/stereo layout and that Volcano 2 and 3 can coexist: <https://www.fabfilter.com/help/volcano/support/vstplugins> · <https://www.fabfilter.com/help/volcano/support/upgrading>
- FabFilter's FAQ explains why side-chain and mono wrappers can be separate for backward compatibility: <https://www.fabfilter.com/support/faq>
- Waves support identifies WPAPI as the Waves Public API folder and gives a separate Waves plug-in folder location: <https://www.waves.com/support/how-to-find-your-plugins-in-studiorack>
- CLAP API repository: <https://github.com/free-audio/clap>

## Project layout

- `Models/` — scan groups, candidates, settings and quarantine records.
- `Services/PluginScanner.cs` — recursive, format-aware scanner.
- `Services/PeInspector.cs` — static PE architecture/export inspection without loading plug-ins.
- `Services/PluginGroupBuilder.cs` — conservative identity grouping and format/architecture ranking.
- `Services/QuarantineService.cs` — same-volume move, UAC helper plans, and no-overwrite restore.
- `MainWindow.xaml` / `.xaml.cs` — bilingual Arabic/English WPF interface.
- `build_portable.bat` — self-contained Windows x64 publish command.
- `../tests/PluginShelf.CoreTests` — dependency-free checks for ranking, alias confirmation, generation-number preservation and Waves/WPAPI exclusions.
