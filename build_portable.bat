@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] .NET 10 SDK was not found in PATH.
  echo Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 and run this script again.
  pause
  exit /b 1
)

dotnet --list-sdks | findstr /R "^10\." >nul
if errorlevel 1 (
  echo [ERROR] A .NET 10 SDK is required to build this project.
  pause
  exit /b 1
)

echo [1/4] Building PluginShelf (Release)...
dotnet build "PluginShelf/PluginShelf.csproj" -c Release -p:EnableWindowsTargeting=true --nologo
if errorlevel 1 (
  echo [ERROR] Build failed.
  pause
  exit /b 1
)

echo.
echo [2/4] Running Core Tests (Fixtures Only)...
dotnet run --project "tests/PluginShelf.CoreTests/PluginShelf.CoreTests.csproj" -c Release
if errorlevel 1 (
  echo [ERROR] Core tests failed.
  pause
  exit /b 1
)

echo.
echo [3/4] Publishing Portable Windows x64 Single-File Executable...
dotnet publish "PluginShelf/PluginShelf.csproj" -c Release -r win-x64 --self-contained true ^
  -p:EnableWindowsTargeting=true ^
  -p:PublishSingleFile=true ^
  -p:IncludeAllContentForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o "%~dp0artifacts\PluginShelf-Portable"
if errorlevel 1 (
  echo [ERROR] Publish failed.
  pause
  exit /b 1
)

if exist "%~dp0PluginShelf-Portable\تشغيل.txt" (
  copy /Y "%~dp0PluginShelf-Portable\تشغيل.txt" "%~dp0artifacts\PluginShelf-Portable\تشغيل.txt" >nul
)
if exist "%~dp0PluginShelf\README.md" (
  copy /Y "%~dp0PluginShelf\README.md" "%~dp0artifacts\PluginShelf-Portable\README.md" >nul
)

echo.
echo [4/4] Computing SHA-256 for artifacts\PluginShelf-Portable\PluginShelf.exe...
powershell -NoProfile -Command ^
  "$exe = '%~dp0artifacts\PluginShelf-Portable\PluginShelf.exe'; " ^
  "$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant(); " ^
  "$size = (Get-Item $exe).Length; " ^
  "Set-Content -Path '%~dp0artifacts\PluginShelf-Portable\SHA256SUMS.txt' -Value ($hash + '  PluginShelf.exe') -Encoding UTF8; " ^
  "Write-Host ('Executable : ' + $exe); " ^
  "Write-Host ('Size       : ' + $size + ' bytes'); " ^
  "Write-Host ('SHA-256    : ' + $hash);"

echo.
echo Done! Portable package is ready in: "%~dp0artifacts\PluginShelf-Portable"
pause
