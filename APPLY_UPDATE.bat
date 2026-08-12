@echo off
setlocal
cd /d "%~dp0"
echo ==========================================
echo V12.5 - UPDATE DYNAMIC PROFILE PATH
echo ==========================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Apply-DynamicProfilePathFix.ps1" -Root "%CD%"
if errorlevel 1 (
  echo.
  echo UPDATE FAILED.
  echo Neu goi update nam trong thu muc con, hay copy 2 file BAT/PS1 vao thu muc goc SOURCE roi chay lai.
  pause
  exit /b 1
)
echo.
echo UPDATE OK.
echo Bay gio chay BUILD_V12_5.bat cua source V12.5.
pause
