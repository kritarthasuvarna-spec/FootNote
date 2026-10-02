# FootNote release build: publishes self-contained binaries, produces the
# distributable zip AND the Setup wizard exe.
#
# Pro monetization is Microsoft Store IAP only (see FootNote.Msix\package-
# msix.ps1 -Tier Pro -Channel Store) — there is no paid unlock path outside
# the Store. This script only ever produces: plain -Tier Free (what ships
# to GitHub Releases — free forever, no Pro code linked in at all), and
# -Tier Pro -Channel Owner (the personal/sideload build, always unlocked,
# never distributed).
#
# Output layout (kept organized so multiple tiers/versions don't collide):
#   dist\<Tier>\v<Version>\FootNote-Setup-v<Version>[-Owner].exe
#   dist\<Tier>\v<Version>\FootNote-v<Version>[-Owner]-win-x64.zip
#   dist\_work\           <- transient staging, always wiped at the start of a build
#   dist\_archive\        <- old artifacts from before this layout existed
param(
    [string]$Version = "1.3.1",
    [ValidateSet("Free", "Pro")][string]$Tier = "Free",
    [ValidateSet("Owner")][string]$Channel = "Owner"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$suffix = if ($Tier -ne "Pro") { "" } else { "-Owner" }
$tierFolder = if ($Tier -ne "Pro") { "Free" } else { "Owner" }

$dist = Join-Path $PSScriptRoot "dist"
$work = Join-Path $dist "_work"
$releaseDir = Join-Path $dist "$tierFolder\v$Version"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Path $work -Force | Out-Null
# Only remove this script's own outputs — package-msix.ps1 shares this same
# folder for the .msix/.cer, and a blanket wipe here deletes those too.
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
Remove-Item (Join-Path $releaseDir "FootNote-Setup-v$Version$suffix.exe") -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $releaseDir "FootNote-v$Version$suffix-win-x64.zip") -Force -ErrorAction SilentlyContinue

$appOut = Join-Path $work "app"

Write-Host "Publishing FootNote.App ($Tier/$Channel, framework-dependent, single file)..."
# Framework-dependent, not self-contained: relies on the .NET 8 Desktop Runtime
# already being on the machine (FootNote.Setup detects/installs it if missing —
# Setup itself STAYS self-contained below, precisely so it can run that check
# on a bare machine with nothing installed yet).
dotnet publish FootNote.App -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true `
    -p:Version=$Version -p:FootNoteTier=$Tier -p:FootNoteChannel=$Channel -o $appOut
if ($LASTEXITCODE -ne 0) { throw "App publish failed" }

Write-Host "Publishing Uninstall.exe (stub, self-contained, trimmed)..."
$unOut = Join-Path $work "uninstall"
dotnet publish FootNote.Uninstaller -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:PublishTrimmed=true -p:TrimMode=partial `
    -p:Version=$Version -o $unOut
if ($LASTEXITCODE -ne 0) { throw "Uninstaller publish failed" }

Copy-Item (Join-Path $unOut "Uninstall.exe") $appOut
# clean 4-file layout: app, uninstaller, on-disk reference card, license
Copy-Item (Join-Path $PSScriptRoot "install-assets\README.txt") $appOut
Copy-Item (Join-Path $PSScriptRoot "install-assets\LICENSE.txt") $appOut
Remove-Item $unOut -Recurse -Force
Get-ChildItem $appOut -Filter *.pdb | Remove-Item

$zip = Join-Path $releaseDir "FootNote-v$Version$suffix-win-x64.zip"
Compress-Archive -Path (Join-Path $appOut "*") -DestinationPath $zip -CompressionLevel Optimal

Write-Host "Publishing FootNote.Setup (wizard with embedded payload)..."
$payload = Join-Path $PSScriptRoot "FootNote.Setup\payload.zip"
Copy-Item $zip $payload -Force
try {
    $setupOut = Join-Path $work "setup"
    dotnet publish FootNote.Setup -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:Version=$Version -o $setupOut
    if ($LASTEXITCODE -ne 0) { throw "Setup publish failed" }
    Copy-Item (Join-Path $setupOut "FootNote.Setup.exe") (Join-Path $releaseDir "FootNote-Setup-v$Version$suffix.exe") -Force
}
finally {
    Remove-Item $payload -Force -ErrorAction SilentlyContinue
}

Remove-Item $work -Recurse -Force

Write-Host ""
Write-Host "Done. -> $releaseDir"
Get-ChildItem $releaseDir -File | Select-Object Name, @{n="MB";e={[math]::Round($_.Length/1MB,1)}}
