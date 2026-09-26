@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ============================================
echo  编译 Release 版本
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

echo [1/3] 编译 AcFunDanmu (Release)...
pushd "D:\bililive_dm-master\AcFunDanmu"
dotnet build AcFunDanmu.csproj -c Release
if !errorlevel! neq 0 (popd & goto :fail)
popd

echo.
echo [2/3] 编译 BiliDMLib (Release)...
pushd "D:\bililive_dm-master\BiliDMLib"
"!MSBUILD!" BiliDMLib.csproj /p:Configuration=Release /v:minimal /nologo
if !errorlevel! neq 0 (popd & goto :fail)
popd

echo.
echo [3/3] 编译 Bililive_dm (Release)...
pushd "D:\bililive_dm-master\Bililive_dm"
"!MSBUILD!" Bililive_dm.csproj /p:Configuration=Release /v:minimal /nologo
if !errorlevel! neq 0 (popd & goto :fail)
popd

echo.
echo ============================================
echo  Release 编译成功！
echo ============================================
echo.
echo 输出目录: D:\bililive_dm-master\Bililive_dm\bin\Release\
pause
exit /b 0

:fail
echo.
echo ============================================
echo  编译失败
echo ============================================
pause
exit /b 1