@echo off
rem Hot-reload UI in its own window, then the app pointing at it. Close both windows to stop.
cd /d "%~dp0.."
if not exist src\web\node_modules (pushd src\web & call npm ci & popd)
start "HadalStream UI (Vite)" /d src\web cmd /k npm run dev
curl -s -o nul --retry 30 --retry-connrefused --retry-delay 1 http://localhost:5173 || (echo The UI dev server did not start. & pause & exit /b 1)
dotnet run --project src\HadalStream.Desktop --launch-profile dev || pause
