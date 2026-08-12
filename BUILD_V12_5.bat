@echo off
setlocal
cd /d "%~dp0"
echo ========================================
echo BUILD TOOL TIKTOK V12.5 - OPTIMIZED
echo ========================================
echo.
echo [1/2] Build V11.5 worker...
dotnet build ".\ToolTikTokWorkerV125\ToolTikTokWorkerV125.csproj" -c Release
if errorlevel 1 goto :fail
echo.
echo [2/2] Build V12.5 manager...
dotnet build ".\ToolTikTokManagerV125\ToolTikTokManagerV125.csproj" -c Release
if errorlevel 1 goto :fail
echo.
echo ========================================
echo BUILD OK
echo Output: %CD%\dist_v125
echo ========================================
echo.
pause
exit /b 0

:fail
echo.
echo ========================================
echo BUILD FAILED
echo ========================================
echo.
pause
exit /b 1
