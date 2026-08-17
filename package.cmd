@echo off
rem ============================================================
rem  dsh-web-launcher 发布打包脚本
rem  一键生成 Release 附件：
rem    dist\dsh-web-launcher.exe                   （便捷版，开箱即用）
rem    dist\dsh-web-launcher-v<版本>-config.zip    （配置版，含 exe + config.json）
rem  用法：package.cmd [版本号]   例如：package.cmd 1.0.3
rem  依赖：Windows 自带 .NET Framework csc.exe 与 PowerShell Compress-Archive
rem ============================================================
setlocal
cd /d "%~dp0"

set "VER=%~1"
if "%VER%"=="" set "VER=dev"

set "DIST=dist"
if exist "%DIST%" rd /s /q "%DIST%"
mkdir "%DIST%"

rem 1) 编译 exe（复用 build.cmd，nopause 避免交互暂停）
call build.cmd nopause
if errorlevel 1 (
    echo [ERROR] 编译失败，终止打包。
    exit /b 1
)

rem 2) 复制便捷版 exe 与配置模板
copy /y dsh-web-launcher.exe "%DIST%\dsh-web-launcher.exe" >nul
copy /y config.example.json "%DIST%\config.json" >nul

rem 3) 生成配置版 zip（PowerShell Compress-Archive，Windows 10/11 自带）
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%DIST%\dsh-web-launcher.exe','%DIST%\config.json' -DestinationPath '%DIST%\dsh-web-launcher-v%VER%-config.zip' -CompressionLevel Optimal"
if errorlevel 1 (
    echo [ERROR] zip 打包失败。
    exit /b 1
)
del "%DIST%\config.json"

echo.
echo [OK] 打包完成，请上传以下附件到 GitHub Release：
echo   %DIST%\dsh-web-launcher.exe
echo   %DIST%\dsh-web-launcher-v%VER%-config.zip
exit /b 0
