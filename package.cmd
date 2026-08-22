@echo off
rem ============================================================
rem  dsh-web-launcher release packaging script
rem  Generates: dist\dsh-web-launcher.exe (single file, ready to use;
rem  port etc. can be changed via tray menu "Settings..." which saves
rem  to %%LOCALAPPDATA%%\dsh-web-launcher\config.json)
rem  Usage: double-click package.cmd, or run "package.cmd"
rem  Requires: built-in .NET Framework csc.exe
rem ============================================================
setlocal
cd /d "%~dp0"

set "DIST=dist"
if exist "%DIST%" rd /s /q "%DIST%"
mkdir "%DIST%"

rem 1) Build exe (reuse build.cmd with nopause)
call build.cmd nopause
if errorlevel 1 (
    echo [ERROR] Build failed - packaging aborted.
    exit /b 1
)

rem 2) Copy release exe
copy /y dsh-web-launcher.exe "%DIST%\dsh-web-launcher.exe" >nul

echo.
echo [OK] Packaging done. Upload this file to GitHub Release:
echo   %DIST%\dsh-web-launcher.exe
exit /b 0
