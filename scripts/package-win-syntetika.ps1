# SPDX-License-Identifier: AGPL-3.0-only
#
# Package Syntetika Engine for Windows: a self-contained publish (no .NET needed on the
# target), the openVJ visual engine, FFmpeg, the Visual C++ runtime and the licence files
# beside the app, then an Inno Setup installer (or a portable ZIP when Inno Setup is not
# installed).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts/package-win-syntetika.ps1 [x64|arm64]
#   ... -NoFfmpeg    # skip the FFmpeg download/bundle
# Output:
#   dist/Syntetika-Engine-Setup-<version>-<arch>.exe
#   dist/Syntetika-Engine-<version>-portable-<arch>.zip

param(
    [ValidateSet('x64', 'arm64')][string]$Arch = 'x64',
    [switch]$NoFfmpeg
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$nota = Join-Path $root 'src/nota'
$openvj = Join-Path $root 'src/openvj'
$rid = "win-$Arch"
$nativeBuild = Join-Path $nota 'src/native/nota.engine/build'
$pubDir = Join-Path $root "dist/publish-$Arch"

# Product version from Branding.cs; the engine's VERSION file is Nota's, not ours.
$branding = Get-Content (Join-Path $nota 'src/managed/Nota.Application/Branding.cs') -Raw
$version = [regex]::Match($branding, 'ProductVersion\s*=\s*"([^"]+)"').Groups[1].Value
if (-not $version) { throw 'ProductVersion not found in Branding.cs' }

$dll = Join-Path $nativeBuild 'nota_engine.dll'
if (-not (Test-Path $dll)) {
    throw "nota_engine.dll not found at $dll - run scripts/build-win.ps1 first."
}
$scanWorker = Join-Path $nativeBuild 'nota-scanworker.exe'

Write-Host "==> Publishing $rid (self-contained)"
if (Test-Path $pubDir) { Remove-Item -Recurse -Force $pubDir }
dotnet publish (Join-Path $nota 'src/managed/Nota.App') -c Release -r $rid --self-contained true `
    "-p:NotaNativeDir=$nativeBuild" -p:NotaSkipWebBuild=true -p:DebugType=none -p:DebugSymbols=false -o $pubDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Copy-Item $dll $pubDir -Force
if (Test-Path $scanWorker) { Copy-Item $scanWorker $pubDir -Force }

# The visual engine the app starts (VisualEngineProcess.FindBinary looks next to itself).
$visual = Join-Path $openvj 'target/release/syntetika-visual.exe'
if (-not (Test-Path $visual)) { throw "syntetika-visual.exe not found - run scripts/build-win.ps1 first." }
Copy-Item $visual $pubDir -Force

# Visual C++ runtime, app-local: the engine DLL and the visual engine import it, and a clean
# Windows has no VC++ redistributable.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vsPath = $null
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -products * -property installationPath
}
if ($vsPath) {
    $crt = Get-ChildItem (Join-Path $vsPath 'VC/Redist/MSVC') -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like 'Microsoft.VC1*.CRT' -and $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($crt) {
        Copy-Item (Join-Path $crt.FullName '*.dll') $pubDir -Force
        Write-Host "==> bundled the Visual C++ runtime"
    }
    else {
        Write-Warning 'VC++ runtime DLLs not found; a clean PC may not load the engine.'
    }
}
else {
    Write-Warning 'Visual Studio not found; VC++ runtime not bundled.'
}

# FFmpeg (LGPL), beside the engine so both the app and openVJ find it there.
if (-not $NoFfmpeg) {
    $ffmpeg = & (Join-Path $PSScriptRoot 'get-ffmpeg-win.ps1')
    if ($ffmpeg) {
        Copy-Item (Join-Path $ffmpeg '*') $pubDir -Force
        Write-Host '==> bundled ffmpeg/ffprobe'
    }
}

# Licence files: AGPL/GPL notices, third-party notices, attribution and the source offer.
$lic = Join-Path $pubDir 'licenses'
New-Item -ItemType Directory -Force -Path $lic | Out-Null
Copy-Item (Join-Path $root 'LICENSES/*.txt') $lic -Force
Copy-Item (Join-Path $nota 'LICENSES/THIRD-PARTY-NOTICES.md') $lic -Force
Copy-Item (Join-Path $nota 'LICENSES/third-party.md') (Join-Path $lic 'NOTA-THIRD-PARTY.md') -Force
Copy-Item (Join-Path $nota 'LICENSES/OFL-1.1-Geist.txt') $lic -Force
Copy-Item (Join-Path $openvj 'LICENSE') (Join-Path $lic 'OPENVJ-GPL-3.0-or-later.txt') -Force
Copy-Item (Join-Path $root 'ATTRIBUTION.md') $lic -Force
Copy-Item (Join-Path $root 'TRADEMARKS.md') $lic -Force
Copy-Item (Join-Path $root 'README.md') $lic -Force
if (-not $NoFfmpeg) {
    $ffmpegLic = Join-Path $env:LOCALAPPDATA 'syntetika-build\ffmpeg\LICENSE.txt'
    if (Test-Path $ffmpegLic) { Copy-Item $ffmpegLic (Join-Path $lic 'FFMPEG-LGPL.txt') -Force }
}
@"
Syntetika Engine $version

This program is free software under the GNU Affero General Public License v3.0
(see AGPL-3.0-only.txt). It integrates:

  Nota      (c) Egor Khindikaynen, AGPL-3.0-only
  openVJ    openwave-studio and contributors, GPL-3.0-or-later
  FFmpeg    LGPL-2.1-or-later (separate executables, licenses/FFMPEG-LGPL.txt)

The complete corresponding source code is available at:

  https://github.com/danartriyudistira/syntetika-engine

and on request from the distributor for at least three years after this build
was distributed (AGPL-3.0 section 13, GPL-3.0 section 6d).
"@ | Set-Content (Join-Path $lic 'SOURCE.txt') -Encoding UTF8

Get-ChildItem -Path $pubDir -Recurse -Filter *.pdb | Remove-Item -Force

function Find-SignTool {
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    if (-not (Test-Path $kits)) { return $null }
    Get-ChildItem $kits -Directory | Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64/signtool.exe' } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}

# Code signing is optional: point SYNTETIKA_SIGN_CERT at a PFX (with
# SYNTETIKA_SIGN_PASSWORD) or at a certificate thumbprint in the store. Without it the
# build is unsigned and SmartScreen warns, as it does today.
function Sign-Files([string[]]$files) {
    $cert = $env:SYNTETIKA_SIGN_CERT
    if (-not $cert) {
        Write-Host '    code signing: skipped (set SYNTETIKA_SIGN_CERT and SYNTETIKA_SIGN_PASSWORD)'
        return
    }
    $signTool = Find-SignTool
    if (-not $signTool) { throw 'signtool.exe not found: install the Windows SDK signing tools.' }
    $signArgs = @('sign', '/fd', 'SHA256', '/tr', 'http://timestamp.digicert.com', '/td', 'SHA256')
    if (Test-Path $cert) {
        $signArgs += @('/f', $cert)
        if ($env:SYNTETIKA_SIGN_PASSWORD) { $signArgs += @('/p', $env:SYNTETIKA_SIGN_PASSWORD) }
    }
    else {
        $signArgs += @('/sha1', $cert)
    }
    foreach ($file in $files) {
        & $signTool @signArgs $file
        if ($LASTEXITCODE -ne 0) { throw "signing failed: $file" }
    }
    Write-Host "    code signing: $($files.Count) file(s) signed"
}

function Write-Checksum([string]$file) {
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    $line = "$hash  $(Split-Path -Leaf $file)"
    Set-Content -Path "$file.sha256" -Value $line -Encoding ASCII
    Write-Host "    $([System.IO.Path]::GetFileName($file)).sha256"
}

Write-Host '==> Signing the engine, app and visual engine'
$signPayload = @(
    'Nota.App.exe', 'nota_engine.dll', 'nota-scanworker.exe', 'syntetika-visual.exe',
    'ffmpeg.exe', 'ffprobe.exe'
) | ForEach-Object { Join-Path $pubDir $_ } | Where-Object { Test-Path $_ }
Sign-Files $signPayload

function Find-ISCC {
    $cmd = (Get-Command iscc -ErrorAction SilentlyContinue).Source
    if ($cmd) { return $cmd }
    foreach ($base in @(${env:ProgramFiles}, ${env:ProgramFiles(x86)}, (Join-Path $env:LOCALAPPDATA 'Programs'))) {
        if (-not $base) { continue }
        $candidate = Join-Path $base 'Inno Setup 6/ISCC.exe'
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

$iscc = Find-ISCC
if ($iscc) {
    Write-Host "==> Installer with $iscc"
    & $iscc "/DAppVersion=$version" "/DArch=$Arch" "/DPubDir=$pubDir" (Join-Path $PSScriptRoot 'syntetika.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }
    $installer = Join-Path $root "dist/Syntetika-Engine-Setup-$version-$Arch.exe"
    if ($env:SYNTETIKA_SIGN_CERT) { Sign-Files @($installer) }
    Write-Checksum $installer
    Write-Host "==> $installer"
}
else {
    Write-Host '==> Inno Setup not found: writing a portable ZIP instead'
    $zip = Join-Path $root "dist/Syntetika-Engine-$version-portable-$Arch.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $pubDir '*') -DestinationPath $zip
    Write-Checksum $zip
    Write-Host "==> $zip"
}
