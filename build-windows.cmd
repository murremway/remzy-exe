@echo off
setlocal

REM Convenience wrapper for Windows users who double-click or prefer cmd.exe.
REM For options, run: powershell -ExecutionPolicy Bypass -File .\build-windows.ps1 -?

cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-windows.ps1" %*
set EXITCODE=%ERRORLEVEL%

if not "%EXITCODE%"=="0" (
  echo.
  echo Windows build failed with exit code %EXITCODE%.
  echo.
  pause
)

exit /b %EXITCODE%
