@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ============================================
echo  编译 BiliDMLib 项目（.NET Framework 4.6.1）
echo ============================================
echo.

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo [错误] 未找到 Visual Studio Installer
    pause
    exit /b 1
)

set "MSBUILD="
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"

if not defined MSBUILD (
    echo [错误] 未找到 MSBuild
    pause
    exit /b 1
)

echo [信息] 使用 MSBuild: !MSBUILD!
echo.

cd /d "D:\bililive_dm-master\BiliDMLib"

echo [1/2] 还原 NuGet 包...
"!MSBUILD!" BiliDMLib.csproj /t:Restore /v:minimal /nologo
if !errorlevel! neq 0 goto :fail

echo.
echo [2/2] 编译...
"!MSBUILD!" BiliDMLib.csproj /p:Configuration=Debug /v:minimal /nologo
if !errorlevel! neq 0 goto :fail

echo.
echo ============================================
echo  编译成功！
echo ============================================
echo.
echo 输出文件在: D:\bililive_dm-master\BiliDMLib\bin\Debug\
pause
exit /b 0

:fail
echo.
echo ============================================
echo  编译失败
echo ============================================
echo.
echo 请把上面的错误信息完整复制发给助手
pause
exit /b 1