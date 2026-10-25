# Builds the "Send to UniFetcher" extension for Firefox and Brave/Chrome/Edge from one source folder.
# Usage: scripts/build-extension.ps1 -Version 1.2.3 -OutDir ext
#   -> ext/firefox (load or sign this) and ext/chrome (load unpacked in Brave/Chrome/Edge)
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$OutDir
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "extension/src"
# Browsers want 1 to 4 numbers: "1.2.3-beta1" -> "1.2.3"
$extVersion = ($Version -split "-")[0]
foreach ($browser in "firefox", "chrome") {
    $dest = Join-Path $OutDir $browser
    Remove-Item -Recurse -Force $dest -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item -Path (Join-Path $src "*") -Destination $dest -Recurse
    $manifest = Get-Content (Join-Path $root "extension/manifest.$browser.json") -Raw | ConvertFrom-Json
    $manifest.version = $extVersion
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $dest "manifest.json") -Encoding utf8NoBOM
}
Write-Host "Extension $extVersion built in $OutDir"
