# SPDX-License-Identifier: AGPL-3.0-only
#
# Build Syntetika Engine on Windows (x64): native Nota engine, openVJ visual engine and
# the .NET/Avalonia app, then put everything where the app looks for it.
#
# Usage (Windows PowerShell 5.1 or PowerShell 7):
#   powershell -ExecutionPolicy Bypass -File scripts/build-win.ps1
#   ... -SkipNative     # engine DLL already built
#   ... -SkipVisual     # openVJ already built
#   ... -SkipManaged    # only the native parts
#
# Requirements: Visual Studio 2022 Build Tools (MSVC v143 + Windows SDK), CMake, Ninja,
# .NET SDK 10 (https://dot.net/v1/dotnet-install.ps1), Rust (MSVC toolchain), Node 22
# only when the Remote page is built (skipped here).

param(
    [switch]$SkipNative,
    [switch]$SkipVisual,
    [switch]$SkipManaged,
    [switch]$WithLink
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$nota = Join-Path $root 'src/nota'
$openvj = Join-Path $root 'src/openvj'
$nativeBuild = Join-Path $nota 'src/native/nota.engine/build'
$appOut = Join-Path $nota 'src/managed/Nota.App/bin/Release/net10.0'

function Find-VsDevShell {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found: install Visual Studio Build Tools 2022.' }
    $path = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $path) { throw 'Visual Studio Build Tools with the C++ workload not found.' }
    return $path
}

function Enter-MsvcEnvironment([string]$vsPath) {
    $devCmd = Join-Path $vsPath 'Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja'
    $env:PATH = "$devCmd;$env:PATH"
    $dotnet = Join-Path $env:USERPROFILE '.dotnet'
    if (Test-Path (Join-Path $dotnet 'dotnet.exe')) {
        $env:DOTNET_ROOT = $dotnet
        $env:PATH = "$dotnet;$env:PATH"
    }
    # Imports MSVC's environment into this process: repeat cl.exe's usual discovery so
    # cmake finds the compiler without a Developer PowerShell.
    $vcvars = Join-Path $vsPath 'VC/Auxiliary/Build/vcvars64.bat'
    $env:VSCMD_ARG_TGT_ARCH = 'x64'
    cmd /c "`"$vcvars`" >nul && set" | ForEach-Object {
        if ($_ -match '^([^=]+)=(.*)$') { Set-Item -Path "env:$($matches[1])" -Value $matches[2] }
    }
}

function Copy-VcRuntime([string]$vsPath, [string]$target) {
    # The engine DLL and the visual engine import the VC++ 2015-2022 runtime. The app folder
    # carries it app-local so a PC without the redistributable still starts.
    $crt = Get-ChildItem (Join-Path $vsPath 'VC/Redist/MSVC') -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'Microsoft.VC1*.CRT' -and $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $crt) {
        Write-Warning 'VC++ runtime DLLs not found under Visual Studio; a clean PC may not load the engine.'
        return
    }
    Copy-Item (Join-Path $crt.FullName '*.dll') $target -Force
    Write-Host "==> bundled the Visual C++ runtime from $($crt.FullName)"
}

$vs = Find-VsDevShell
Enter-MsvcEnvironment $vs
Write-Host "==> Visual Studio: $vs"

if (-not $SkipNative) {
    Write-Host '==> Nota native engine (x64/WASAPI)'
    cmake -G Ninja -S (Join-Path $nota 'src/native/nota.engine') -B $nativeBuild '-DCMAKE_BUILD_TYPE=Release'
    if ($LASTEXITCODE -ne 0) { throw 'cmake configure failed' }
    cmake --build $nativeBuild -j 8
    if ($LASTEXITCODE -ne 0) { throw 'native engine build failed' }
    & (Join-Path $nativeBuild 'nota-scanner-selftest.exe')
    if ($LASTEXITCODE -ne 0) { throw 'scanner self-test failed' }
}

if (-not $SkipVisual) {
    Write-Host '==> openVJ visual engine (release)'
    # Syphon is macOS-only; the Windows build links camera input, Spout output and, with
    # -WithLink, Ableton Link (needs a C++ compiler, which a Developer PowerShell has).
    $features = if ($WithLink) { 'camera,spout,link' } else { 'camera,spout' }
    Push-Location $openvj
    try {
        cargo build --release -p openvj-app --no-default-features --features $features
        if ($LASTEXITCODE -ne 0) { throw 'openVJ build failed' }
    }
    finally { Pop-Location }
}

if (-not $SkipManaged) {
    Write-Host '==> Nota managed app (.NET 10)'
    dotnet build (Join-Path $nota 'Nota.sln') -c Release --nologo -p:NotaSkipWebBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'managed build failed' }

    # The app starts the visual engine from its own folder (VisualEngineProcess.FindBinary).
    $visual = Join-Path $openvj 'target/release/syntetika-visual.exe'
    if (Test-Path $visual) {
        Copy-Item $visual (Join-Path $appOut 'syntetika-visual.exe') -Force
        Write-Host "==> bundled $(Split-Path -Leaf $visual) into the app folder"
    }
    else {
        Write-Warning 'syntetika-visual.exe not found: run without -SkipVisual, or the app starts without visuals.'
    }

    Copy-VcRuntime $vs $appOut

    if (Test-Path $appOut) {
        $ffmpeg = & (Join-Path $PSScriptRoot 'get-ffmpeg-win.ps1')
        if ($ffmpeg) {
            Copy-Item (Join-Path $ffmpeg '*') $appOut -Force
            Copy-Item (Join-Path (Split-Path -Parent $ffmpeg) 'LICENSE.txt') (Join-Path $appOut 'FFMPEG-LICENSE.txt') -Force -ErrorAction SilentlyContinue
            Write-Host '==> bundled ffmpeg/ffprobe'
        }
    }
}

Write-Host ''
Write-Host 'Done. Start the app with:'
Write-Host "  & '$appOut/Nota.App.exe'"
