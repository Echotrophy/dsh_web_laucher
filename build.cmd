@echo off
rem ============================================================
rem  dsh-web-launcher build script
rem  Uses the built-in .NET Framework csc.exe - no install needed
rem  Usage: double-click build.cmd, or run "build.cmd nopause"
rem  (pass nopause to skip the pause prompt, used by package.cmd)
rem ============================================================
setlocal
cd /d "%~dp0"

set "CSC="
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not defined CSC (
    echo [ERROR] csc.exe not found ^(requires .NET Framework 4.x^).
    if /i not "%~1"=="nopause" pause
    exit /b 1
)

echo [INFO] Compiler: %CSC%
"%CSC%" /nologo /target:winexe /optimize+ /out:dsh-web-launcher.exe ^
    /win32icon:DeepSeekHarness-WhaleGirl.ico ^
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
    dsh-web-launcher.cs

if errorlevel 1 (
    echo [ERROR] Build failed - see messages above.
    if /i not "%~1"=="nopause" pause
    exit /b 1
)

echo [OK] dsh-web-launcher.exe generated - double-click to run.
if /i not "%~1"=="nopause" pause
