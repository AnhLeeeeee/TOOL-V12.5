@echo off
setlocal
cd /d "%~dp0"
echo [1/2] Build V11.5 worker...
dotnet build ".\ToolTikTokWorkerV125\ToolTikTokWorkerV125.csproj" -c Release
if errorlevel 1 goto :fail
echo [2/2] Build V12.5 manager...
dotnet build ".\ToolTikTokManagerV125\ToolTikTokManagerV125.csproj" -c Release
if errorlevel 1 goto :fail
echo.
echo BUILD OK - output: %CD%\dist_v125
exit /b 0
:fail
echo.
echo BUILD FAILED
exit /b 1
