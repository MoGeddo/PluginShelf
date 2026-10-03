@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 10 SDK was not found.
  echo Install the .NET 10 SDK, then run this file again.
  pause
  exit /b 1
)
dotnet --list-sdks | findstr /R "^10\." >nul
if errorlevel 1 (
  echo A .NET 10 SDK is required to rebuild this project.
  pause
  exit /b 1
)

echo Building the self-contained Windows x64 version...
dotnet publish "PluginShelf.csproj" -c Release -r win-x64 --self-contained true ^
  -p:EnableWindowsTargeting=true ^
  -p:PublishSingleFile=true ^
  -p:IncludeAllContentForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o "%~dp0portable"
if errorlevel 1 (
  echo Build failed. Read the messages above.
  pause
  exit /b 1
)

echo.
echo Done: "%~dp0portable\PluginShelf.exe"
echo Copy PluginShelf.exe anywhere to use the portable app.
pause
