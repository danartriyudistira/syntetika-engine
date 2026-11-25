# SPDX-License-Identifier: AGPL-3.0-only
#
# Builds the Windows .ico (16-256 px) from a square PNG, for the exe, taskbar and
# installer. Every entry is PNG-compressed, which Windows Vista and later read.
#
# Usage: powershell -File scripts/make-win-icon.ps1 [-Source <png>] [-Destination <ico>]

param(
    [string]$Source = "$PSScriptRoot/../assets/syntetika-icon.svg.png",
    [string]$Destination = "$PSScriptRoot/../src/nota/assets/icons/windows/syntetika.ico",
    [int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
)

Add-Type -AssemblyName System.Drawing

$src = (Resolve-Path $Source).Path
$dest = [System.IO.Path]::GetFullPath($Destination)
$image = [System.Drawing.Image]::FromFile($src)

$frames = @()
foreach ($size in $Sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($image, 0, 0, $size, $size)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$ms.ToArray()
    $bmp.Dispose()
    $ms.Dispose()
}
$image.Dispose()

$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($out)
$writer.Write([UInt16]0)               # reserved
$writer.Write([UInt16]1)               # type: icon
$writer.Write([UInt16]$Sizes.Count)
$offset = 6 + 16 * $Sizes.Count
for ($i = 0; $i -lt $Sizes.Count; $i++) {
    $size = $Sizes[$i]
    $dim = if ($size -ge 256) { 0 } else { $size }   # 0 means 256 in the directory
    $writer.Write([byte]$dim)
    $writer.Write([byte]$dim)
    $writer.Write([byte]0)             # palette
    $writer.Write([byte]0)             # reserved
    $writer.Write([UInt16]1)           # colour planes
    $writer.Write([UInt16]32)          # bits per pixel
    $writer.Write([UInt32]$frames[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($dest, $out.ToArray())
$writer.Dispose()
$out.Dispose()

Write-Host "wrote $dest ($((Get-Item $dest).Length) bytes)"
