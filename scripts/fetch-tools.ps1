# Downloads the programs UniFetcher bundles, only from their official GitHub releases:
#   yt-dlp  -> github.com/yt-dlp/yt-dlp
#   ffmpeg  -> github.com/yt-dlp/FFmpeg-Builds (the builds yt-dlp recommends)
#   deno    -> github.com/denoland/deno (yt-dlp needs it for YouTube)
# Usage: scripts/fetch-tools.ps1 -Arch x64|arm64 -OutDir <folder>
# Needs GH_TOKEN set (GitHub Actions provides it) for the `gh` command.
param(
    [Parameter(Mandatory = $true)][ValidateSet("x64", "arm64")][string]$Arch,
    [Parameter(Mandatory = $true)][string]$OutDir
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# yt-dlp: newest stable release at build time (the app then follows your chosen channel by itself).
# deno: keep in step with DenoBundledVersion in the Shared project.
$DenoVersion = "2.8.0"

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$tmp = Join-Path $env:RUNNER_TEMP "unifetcher-tools-$Arch"
Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

# yt-dlp
$ytdlpAsset = if ($Arch -eq "arm64") { "yt-dlp_arm64.exe" } else { "yt-dlp.exe" }
gh release download -R yt-dlp/yt-dlp -p $ytdlpAsset -D $tmp
if ($LASTEXITCODE -ne 0) { throw "Could not download the latest $ytdlpAsset" }
Move-Item (Join-Path $tmp $ytdlpAsset) (Join-Path $OutDir "yt-dlp.exe") -Force

# ffmpeg + ffprobe
$ffName = if ($Arch -eq "arm64") { "ffmpeg-master-latest-winarm64-gpl" } else { "ffmpeg-master-latest-win64-gpl" }
gh release download latest -R yt-dlp/FFmpeg-Builds -p "$ffName.zip" -D $tmp
if ($LASTEXITCODE -ne 0) { throw "Could not download $ffName.zip from yt-dlp/FFmpeg-Builds" }
Expand-Archive -Force (Join-Path $tmp "$ffName.zip") (Join-Path $tmp "ffmpeg")
$ffBin = Get-ChildItem -Recurse -Path (Join-Path $tmp "ffmpeg") -Filter "ffmpeg.exe" | Select-Object -First 1
if (-not $ffBin) { throw "ffmpeg.exe not found in $ffName.zip" }
Copy-Item $ffBin.FullName (Join-Path $OutDir "ffmpeg.exe") -Force
Copy-Item (Join-Path $ffBin.DirectoryName "ffprobe.exe") (Join-Path $OutDir "ffprobe.exe") -Force

# deno
$denoArch = if ($Arch -eq "arm64") { "aarch64" } else { "x86_64" }
gh release download "v$DenoVersion" -R denoland/deno -p "deno-$denoArch-pc-windows-msvc.zip" -D $tmp
if ($LASTEXITCODE -ne 0) { throw "Could not download deno $DenoVersion" }
Expand-Archive -Force (Join-Path $tmp "deno-$denoArch-pc-windows-msvc.zip") (Join-Path $tmp "deno")
Copy-Item (Join-Path $tmp "deno\deno.exe") (Join-Path $OutDir "deno.exe") -Force

Get-ChildItem $OutDir | Format-Table Name, Length
