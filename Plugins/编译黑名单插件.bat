@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ============================================
echo  编译 黑名单插件（BlacklistPlugin）
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

REM 插件项目就在 bat 同目录
set "PLUGIN_DIR=%~dp0"
set "CSPROJ=%PLUGIN_DIR%BlacklistPlugin.csproj"

REM 弹幕姬项目根目录（写死）
set "AC_ROOT=D:\ac_live_master"

if not exist "%CSPROJ%" (
    echo [错误] 找不到 %CSPROJ%
    pause
    exit /b 1
)

echo [信息] MSBuild: %MSBUILD%
echo [信息] 插件项目: %CSPROJ%
echo.

echo [1/2] 还原 NuGet 包...
"%MSBUILD%" "%CSPROJ%" /t:Restore /v:minimal /nologo
if !errorlevel! neq 0 goto :fail

echo.
echo [2/2] 编译插件...
"%MSBUILD%" "%CSPROJ%" /p:Configuration=Release /v:minimal /nologo
if !errorlevel! neq 0 goto :fail

set "DLL=%PLUGIN_DIR%bin\Release\BlacklistPlugin.dll"
set "PLUGDIR_D=%AC_ROOT%\Bililive_dm\bin\Debug\Plugins"
set "PLUGDIR_R=%AC_ROOT%\Bililive_dm\bin\Release\Plugins"

if exist "%AC_ROOT%\Bililive_dm\bin\Debug" (
    if not exist "%PLUGDIR_D%" mkdir "%PLUGDIR_D%"
    copy /y "%DLL%" "%PLUGDIR_D%\" >nul
    echo [3/3] 已复制到: %PLUGDIR_D%
)
if exist "%AC_ROOT%\Bililive_dm\bin\Release" (
    if not exist "%PLUGDIR_R%" mkdir "%PLUGDIR_R%"
    copy /y "%DLL%" "%PLUGDIR_R%\" >nul
    echo       已复制到: %PLUGDIR_R%
)

echo.
echo ============================================
echo  编译成功！
echo ============================================
pause
exit /b 0

:fail
echo.
echo  编译失败，把上面的错误整段发给助手
pause
exit /b 1