# PluginShelf

A portable Windows x64 utility for reviewing duplicate plug-in formats in FL Studio libraries. It scans metadata without loading plug-in code and moves approved extras only to a recoverable quarantine; it never deletes files permanently.

## Keeper recommendation

1. x64 first; x86 is considered only when no x64 candidate exists.
2. Then format preference: **VST3 > VST/DLL > CLAP**.
3. Within the winning architecture and format, recommend the newest readable plug-in release for the exact product generation and I/O identity.

A newer version in a lower-priority format does not outrank the preferred format. Ties or versions that cannot safely be compared within the winning tier require manual review. File modification dates are never used. Product generations and Mono/Stereo/side-chain variants stay separate, and Waves/WaveShell/WPAPI are excluded.

See [`PluginShelf/README.md`](PluginShelf/README.md) for safety details, scan paths, references, and build notes. Portable run instructions are in [`PluginShelf-Portable/تشغيل.txt`](PluginShelf-Portable/تشغيل.txt).

## Build and test

Requires the .NET 10 SDK.

```sh
dotnet build PluginShelf/PluginShelf.csproj -c Release -p:EnableWindowsTargeting=true --nologo
dotnet run --project tests/PluginShelf.CoreTests/PluginShelf.CoreTests.csproj -c Release
dotnet publish PluginShelf/PluginShelf.csproj -c Release -r win-x64 --self-contained true -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts/PluginShelf-Portable
```

The GitHub Actions workflow at `.github/workflows/windows-ci.yml` runs the Windows build and core checks on `windows-latest`, then publishes a downloadable portable artifact. CI checks build and core behavior; it does **not** interactively launch the WPF desktop UI. Run the downloaded app on Windows for that final manual check.
