# SPDX-License-Identifier: AGPL-3.0-only
#
# Fetches the LGPL FFmpeg build used by the Windows app (ffmpeg.exe, ffprobe.exe and their
# DLLs) and returns the folder holding them. Cached: later builds reuse the download.
#
# LGPL-2.1-or-later build from BtbN (no GPL components), matching the macOS bundle's
# decode-only FFmpeg. FFmpeg is a separate executable, so the app's AGPL/GPL licences are
# unaffected; the build's LICENSE.txt is shipped next to the binaries.

param(
    [string]$CacheDir = (Join-Path $env:LOCALAPPDATA 'syntetika-build\ffmpeg')
)

$ErrorActionPreference = 'Stop'
$bin = Join-Path $CacheDir 'bin'
$ffmpeg = Join-Path $bin 'ffmpeg.exe'
$ffprobe = Join-Path $bin 'ffprobe.exe'

if ((Test-Path $ffmpeg) -and (Test-Path $ffprobe)) {
    Write-Host "    ffmpeg: $bin (cached)"
    return $bin
}

$url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl-shared.zip'
$zip = Join-Path $CacheDir 'ffmpeg-lgpl-shared.zip'
New-Item -ItemType Directory -Force -Path $CacheDir | Out-Null
Write-Host "    ffmpeg: downloading $url"
Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

$unpacked = Join-Path $CacheDir 'unpacked'
Remove-Item -Recurse -Force $unpacked -ErrorAction SilentlyContinue
Expand-Archive -Path $zip -DestinationPath $unpacked -Force

$src = Get-ChildItem $unpacked -Directory | Select-Object -First 1
$srcBin = Join-Path $src.FullName 'bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
Copy-Item (Join-Path $srcBin 'ffmpeg.exe') $bin -Force
Copy-Item (Join-Path $srcBin 'ffprobe.exe') $bin -Force
Get-ChildItem $srcBin -Filter '*.dll' | Copy-Item -Destination $bin -Force
Copy-Item (Join-Path $src.FullName 'LICENSE.txt') (Join-Path $CacheDir 'LICENSE.txt') -Force
Remove-Item -Recurse -Force $unpacked -ErrorAction SilentlyContinue
Remove-Item $zip -Force -ErrorAction SilentlyContinue

Write-Host "    ffmpeg: $bin"
return $bin
