@echo off
rem Runs all tests. Live tests against the real sites need HADAL_LIVE=1.
cd /d "%~dp0.."
dotnet test HadalStream.slnx
pause
