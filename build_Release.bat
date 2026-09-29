@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ============================================
echo  编译 Release 版本
echo ============================================
echo.

REM ========== 自动判断项目根目录 ==========
set "SCRIPT_DIR=%~dp0"
if exist "%SCRIPT_DIR%Bililive_dm.csproj" (
    REM bat 在 Bililive_dm 文件夹里
    set "ROOT_RAW=%SCRIPT_DIR%..\"
) else if exist "%SCRIPT_DIR%Bililive_dm\Bililive_dm.csproj" (
    REM bat 在 ac_live_master 根目录
    set "ROOT_RAW=%SCRIPT_DIR%"
) else (
    echo [错误] 找不到 Bililive_dm.csproj
    echo         已检查: %SCRIPT_DIR%Bililive_dm.csproj
    echo         已检查: %SCRIPT_DIR%Bililive_dm\Bililive_dm.csproj
    pause
    exit /b 1
)

for %%I in ("%ROOT_RAW%") do set "ROOT=%%~fI"
set "BROTLI_DST_RAW=%ROOT%\BiliDMLib\bin\Release"
for %%I in ("%BROTLI_DST_RAW%") do set "BROTLI_DST=%%~fI"

echo [信息] 根目录: %ROOT%
echo [信息] brolib 目标: %BROTLI_DST%
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

REM ========== 自动获取 brolib 原生库（Release） ==========
if not exist "%BROTLI_DST%\brolib_x64.dll" (
    echo [信息] 正在获取 brolib_x64.dll / brolib_x86.dll ...
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
      "$ErrorActionPreference='Stop';" ^
      "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12;" ^
      "$ver='2.0.2';" ^
      "$url='https://api.nuget.org/v3-flatcontainer/brotli.net/' + $ver + '/brotli.net.' + $ver + '.nupkg';" ^
      "$tmp=Join-Path $env:TEMP ('brotli_' + [guid]::NewGuid().ToString('N'));" ^
      "New-Item -ItemType Directory -Path $tmp | Out-Null;" ^
      "$nupkg=Join-Path $tmp 'brotli.nupkg';" ^
      "Invoke-WebRequest -Uri $url -OutFile $nupkg;" ^
      "$zip=Join-Path $tmp 'brotli.zip';" ^
      "Copy-Item $nupkg $zip;" ^
      "Expand-Archive -Path $zip -DestinationPath $tmp -Force;" ^
      "$srcX64=Get-ChildItem -Path $tmp -Recurse -Filter 'brolib_x64.dll' | Select-Object -First 1;" ^
      "$srcX86=Get-ChildItem -Path $tmp -Recurse -Filter 'brolib_x86.dll' | Select-Object -First 1;" ^
      "if (-not $srcX64 -or -not $srcX86) { Write-Error '在下载的包中找不到 brolib 文件。'; exit 1 };" ^
      "$dst='%BROTLI_DST%';" ^
      "[System.IO.Directory]::CreateDirectory($dst) | Out-Null;" ^
      "Copy-Item -LiteralPath $srcX64.FullName -Destination $dst -Force;" ^
      "Copy-Item -LiteralPath $srcX86.FullName -Destination $dst -Force;" ^
      "Remove-Item $tmp -Recurse -Force"
    if !errorlevel! neq 0 (
        echo [错误] 获取 brolib 失败。
        echo.
        echo [备选方案] 手动下载 Brotli.NET 并复制文件：
        echo   1. 打开 https://www.nuget.org/packages/Brotli.NET/2.0.2
        echo   2. 下载 .nupkg 文件，改后缀为 .zip 并解压
        echo   3. 搜索 brolib_x64.dll 和 brolib_x86.dll
        echo   4. 复制到: %BROTLI_DST%
        echo.
        pause
        exit /b 1
    )
    echo [信息] brolib 已就绪。
    echo.
)

echo [1/3] 编译 AcFunDanmu (Release)...
pushd "%ROOT%\AcFunDanmu"
dotnet build AcFunDanmu.csproj -c Release
if !errorlevel! neq 0 (popd & goto :fail)
popd

echo.
echo [2/3] 编译 BiliDMLib (Release)...
pushd "%ROOT%\BiliDMLib"
"!MSBUILD!" BiliDMLib.csproj /p:Configuration=Release /v:minimal /nologo
if !errorlevel! neq 0 (popd & goto :fail)
popd

echo.
echo [3/3] 编译 Bililive_dm (Release)...
pushd "%ROOT%\Bililive_dm"
"!MSBUILD!" Bililive_dm.csproj /p:Configuration=Release /v:minimal /nologo
if !errorlevel! neq 0 (popd & goto :fail)
popd

echo.
echo ============================================
echo  Release 编译成功！
echo ============================================
echo.
echo 输出目录: %ROOT%\Bililive_dm\bin\Release\
pause
exit /b 0

:fail
echo.
echo ============================================
echo  编译失败
echo ============================================
pause
exit /b 1