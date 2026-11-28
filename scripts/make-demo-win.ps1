# SPDX-License-Identifier: AGPL-3.0-only
#
# Windows counterpart of scripts/make-demo.sh: builds the "Syntetika Demo" bundle from the
# canonical audio manifest in demo/project.json and the ink-kaleido recipes in openVJ's
# demo_visuals example. The repository's demo/ folder already ships visuals/composition.json,
# so this is only needed to regenerate them.
#
# Usage:
#   powershell -File scripts/make-demo-win.ps1 ["C:\path\Syntetika Demo.nota"]

param(
    [string]$Destination = (Join-Path $env:USERPROFILE 'Documents\Syntetika Projects\Syntetika Demo.nota')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

New-Item -ItemType Directory -Force -Path (Join-Path $Destination 'visuals') | Out-Null
$manifest = Join-Path $Destination 'project.json'
if (Test-Path $manifest) {
    $backups = Join-Path $Destination 'backups'
    New-Item -ItemType Directory -Force -Path $backups | Out-Null
    Copy-Item $manifest (Join-Path $backups ("project-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json'))
}
Copy-Item (Join-Path $root 'demo/project.json') $manifest -Force

Write-Host "==> Generating visuals in $Destination"
Push-Location (Join-Path $root 'src/openvj')
try {
    # Syphon (macOS) and the Link C++ SDK are not needed to write the composition file.
    cargo run --release -p openvj-app --example demo_visuals --no-default-features --features camera -- $Destination
    if ($LASTEXITCODE -ne 0) { throw 'demo_visuals failed' }
}
finally { Pop-Location }

Write-Host "==> Demo ready: $Destination"
