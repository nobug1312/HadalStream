@echo off
rem Builds releases\HadalStream-win-Setup.exe. Usage: release.cmd [version]
setlocal EnableDelayedExpansion
cd /d "%~dp0.."
where vpk >nul 2>nul || (echo vpk is missing. Install it with: dotnet tool install -g vpk & pause & exit /b 1)
set "v=%~1"
if not defined v set /p "v=Version, higher than any in releases\ (e.g. 1.0.1): "
echo(!v!| findstr /r "^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$" >nul || (echo Use a version like 1.0.1 & pause & exit /b 1)
dotnet publish src\HadalStream.Desktop -c Release -r win-x64 --self-contained -p:Version=!v! -o publish || (pause & exit /b 1)
vpk pack -u HadalStream -v !v! -p publish -e HadalStream.exe --packTitle HadalStream -i src\HadalStream.Desktop\app.ico --framework webview2 -o releases || (pause & exit /b 1)
explorer releases
pause
