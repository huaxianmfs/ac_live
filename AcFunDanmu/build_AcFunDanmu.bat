@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ============================================
echo  编译 AcFunDanmu 项目（.NET Standard 2.0）
echo ============================================
echo.

set "PROJ=D:\bililive_dm-master\AcFunDanmu\AcFunDanmu.csproj"

if not exist "%PROJ%" (
    echo [错误] 找不到项目文件: %PROJ%
    pause
    exit /b 1
)

cd /d "D:\bililive_dm-master\AcFunDanmu"

where dotnet >nul 2>&1
if %errorlevel% equ 0 (
    echo [信息] 使用 dotnet CLI
    echo.
    echo [1/2] 还原 NuGet 包...
    dotnet restore AcFunDanmu.csproj
    if !errorlevel! neq 0 goto :fail

    echo.
    echo [2/2] 编译...
    dotnet build AcFunDanmu.csproj -c Debug --no-restore
    if !errorlevel! neq 0 goto :fail
    goto :success
)

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo [错误] 未找到 dotnet 或 Visual Studio Installer
    echo 请先安装 .NET SDK 或 Visual Studio
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
echo [1/2] 还原 NuGet 包...
"!MSBUILD!" AcFunDanmu.csproj /t:Restore /v:minimal /nologo
if !errorlevel! neq 0 goto :fail

echo.
echo [2/2] 编译...
"!MSBUILD!" AcFunDanmu.csproj /p:Configuration=Debug /v:minimal /nologo
if !errorlevel! neq 0 goto :fail

:success
echo.
echo ============================================
echo  编译成功！
echo ============================================
echo.
echo 输出文件在: D:\bililive_dm-master\AcFunDanmu\bin\Debug\
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