@echo off
rem ============================================================
rem  dsh-web-launcher 一键编译脚本
rem  使用 Windows 自带的 .NET Framework csc.exe，无需安装任何东西
rem  用法：双击 build.cmd（或在本目录命令行执行 build.cmd）
rem        传参 nopause 时不暂停（供 package.cmd 调用）：build.cmd nopause
rem ============================================================
setlocal
cd /d "%~dp0"

set "CSC="
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not defined CSC (
    echo [ERROR] 未找到 .NET Framework 4.x 的 csc.exe，无法编译。
    if /i not "%~1"=="nopause" pause
    exit /b 1
)

echo [INFO] 使用编译器: %CSC%
"%CSC%" /nologo /target:winexe /optimize+ /out:dsh-web-launcher.exe ^
    /win32icon:DeepSeekHarness-WhaleGirl.ico ^
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
    dsh-web-launcher.cs

if errorlevel 1 (
    echo [ERROR] 编译失败，请检查上面的错误信息。
    if /i not "%~1"=="nopause" pause
    exit /b 1
)

echo [OK] 已生成 dsh-web-launcher.exe，双击即可运行。
if /i not "%~1"=="nopause" pause
