@echo off
setlocal
cd /d "%~dp0"
call BUILD_V12_5.bat
if errorlevel 1 exit /b 1
start "" ".\dist_v125\ToolTikTokManagerV125.exe"
