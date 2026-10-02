@echo off
rem Builds the UI when needed, then starts HadalStream.
cd /d "%~dp0.."
pushd src\web
if not exist node_modules (call npm ci || (pause & exit /b 1))
call npm run build || (pause & exit /b 1)
popd
dotnet run --project src\HadalStream.Desktop || pause
