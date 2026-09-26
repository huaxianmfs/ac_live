@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ============================================
echo  编译 Bililive_dm 主程序
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

cd /d "D:\bililive_dm-master\Bililive_dm"

echo [1/2] 还原 NuGet 包...
"!MSBUILD!" Bililive_dm.csproj /t:Restore /v:minimal /nologo
if !errorlevel! neq 0 goto :fail

echo.
echo [2/2] 编译...
"!MSBUILD!" Bililive_dm.csproj /p:Configuration=Debug /v:minimal /nologo
if !errorlevel! neq 0 goto :fail

echo.
echo ============================================
echo  编译成功！
echo ============================================
echo.
echo 输出文件在: D:\bililive_dm-master\Bililive_dm\bin\Debug\
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