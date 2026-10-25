# Builds the installer, the portable zip and the extension zips from an already-published app.
# Usage: scripts/package.ps1 -Arch x64 -Version 1.0.0 -PublishDir <dotnet publish folder> -OutDir <folder>
param(
    [Parameter(Mandatory = $true)][ValidateSet("x64", "arm64")][string]$Arch,
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$PublishDir,
    [Parameter(Mandatory = $true)][string]$OutDir,
    # Extension version (browsers need plain numbers); defaults to the app version
    [string]$ExtensionVersion = "",
    # Signed Firefox extension (.xpi) to ship inside UniFetcher, if the release workflow made one
    [string]$FirefoxXpi = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
$PublishDir = [System.IO.Path]::GetFullPath($PublishDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

if (-not $ExtensionVersion) { $ExtensionVersion = ($Version -split "-")[0] }

# 0. Browser extension: built from extension/src; a signed Firefox copy ships inside the app so it can be
#    added to the UniFetcher Firefox profile automatically
$extDir = Join-Path $env:RUNNER_TEMP "unifetcher-ext-$Arch"
& (Join-Path $PSScriptRoot "build-extension.ps1") -Version $ExtensionVersion -OutDir $extDir
if ($FirefoxXpi -and (Test-Path $FirefoxXpi)) {
    New-Item -ItemType Directory -Force -Path (Join-Path $PublishDir "extension") | Out-Null
    Copy-Item $FirefoxXpi (Join-Path $PublishDir "extension\unifetcher-firefox.xpi") -Force
}

# 1. Bundled programs (yt-dlp, ffmpeg, deno) go next to the installer script
& (Join-Path $PSScriptRoot "fetch-tools.ps1") -Arch $Arch -OutDir (Join-Path $root "inno")

# 2. Installer: UniFetcher-<version>-Setup-<arch>.exe
# The in-app updater looks for an asset ending in "setup-x64.exe" / "setup-arm64.exe", so keep that ending.
Push-Location (Join-Path $root "inno")
$env:APP_FILES_PATH = [System.IO.Path]::GetRelativePath($root, $PublishDir)
$numeric = ($Version -split "-")[0]
iscc "/DMyAppVersion=$Version" "/DMyNumericVersion=$numeric" "/DMyArch=$Arch" setup.iss
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
Move-Item "UniFetcher-$Version-Setup-$Arch.exe" $OutDir -Force
Pop-Location

# 3. Portable zip: run UniFetcher.bat; settings are kept next to the app instead of in your user folder
$portable = Join-Path $env:RUNNER_TEMP "UniFetcher-portable-$Arch"
Remove-Item -Recurse -Force $portable -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$portable\Release" | Out-Null
Copy-Item -Path "$PublishDir\*" -Destination "$portable\Release" -Recurse
foreach ($tool in "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe") {
    Copy-Item (Join-Path $root "inno\$tool") "$portable\Release\$tool"
}
Set-Content -Path "$portable\UniFetcher.bat" -Value "@echo off`r`nstart `"`" `"%~dp0Release\UniFetcher.exe`" --portable" -NoNewline
Compress-Archive -Path "$portable\*" -DestinationPath (Join-Path $OutDir "UniFetcher-$Version-Portable-$Arch.zip") -Force

# 4. Browser extension packages (same for every architecture, so only built once with x64)
if ($Arch -eq "x64") {
    Compress-Archive -Path (Join-Path $extDir "firefox\*") -DestinationPath (Join-Path $OutDir "UniFetcher-Extension-$Version-Firefox-unsigned.zip") -Force
    Compress-Archive -Path (Join-Path $extDir "chrome\*") -DestinationPath (Join-Path $OutDir "UniFetcher-Extension-$Version-Chrome-Brave-Edge.zip") -Force
    if ($FirefoxXpi -and (Test-Path $FirefoxXpi)) {
        Copy-Item $FirefoxXpi (Join-Path $OutDir "UniFetcher-Extension-$Version-Firefox.xpi") -Force
    }
}

Get-ChildItem $OutDir | Format-Table Name, Length
