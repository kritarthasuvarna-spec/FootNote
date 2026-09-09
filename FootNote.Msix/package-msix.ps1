# Packages FootNote.App as an MSIX, self-signed for local sideload testing.
# Requires the Windows 10 SDK (makeappx.exe, signtool.exe) -- auto-detects
# the newest installed version.
#
# This script does NOT install the package or touch certificate stores /
# Developer Mode itself -- those are system/security settings, left for you
# to do by hand (instructions printed at the end). It only produces a
# signed .msix file.
#
# Usage:
#   .\package-msix.ps1                                   # Free tier
#   .\package-msix.ps1 -Tier Pro                          # Owner tier (always unlocked, sideload only)
#   .\package-msix.ps1 -Tier Pro -Channel Store           # Store-Paid tier (starts locked, real IAP) -- THIS is what goes to Partner Center
param(
    [string]$Version = "1.2.0",
    [ValidateSet("Free", "Pro")][string]$Tier = "Free",
    [ValidateSet("Owner", "Store")][string]$Channel = "Owner"
)
if ($Tier -eq "Free" -and $Channel -eq "Store") { throw "Channel Store only applies to -Tier Pro (Free tier has no Pro code linked in at all)." }

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$root = Split-Path $PSScriptRoot -Parent

# --- locate the Windows SDK tools ------------------------------------------
$sdkRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
$sdkVer = Get-ChildItem $sdkRoot -Directory | Where-Object { $_.Name -match '^\d+\.' } |
    Sort-Object Name -Descending | Select-Object -First 1
if (-not $sdkVer) { throw "Windows 10 SDK not found under $sdkRoot -- install it first." }
$makeappx = Join-Path $sdkVer.FullName "x64\makeappx.exe"
$signtool = Join-Path $sdkVer.FullName "x64\signtool.exe"
Write-Host "Using SDK tools from $($sdkVer.Name)"

# --- stage the app -----------------------------------------------------------
$stage = Join-Path $PSScriptRoot "_stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

Write-Host "Publishing FootNote.App ($Tier/$Channel, self-contained, for MSIX)..."
dotnet publish (Join-Path $root "FootNote.App") -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:FootNoteTier=$Tier -p:FootNoteChannel=$Channel -o $stage
if ($LASTEXITCODE -ne 0) { throw "App publish failed" }

Get-ChildItem $stage -Filter *.pdb | Remove-Item

Copy-Item Package.appxmanifest (Join-Path $stage "AppxManifest.xml") # makeappx requires this exact filename
Copy-Item Assets (Join-Path $stage "Assets") -Recurse

# --- pack --------------------------------------------------------------------
$tierFolder = if ($Tier -ne "Pro") { "Free" } elseif ($Channel -eq "Store") { "Store" } else { "Owner" }
$out = Join-Path $root "dist\$tierFolder\v$Version"
New-Item -ItemType Directory -Path $out -Force | Out-Null
$suffix = if ($Tier -ne "Pro") { "" } elseif ($Channel -eq "Store") { "-Store" } else { "-Owner" }
$msix = Join-Path $out "FootNote-v$Version$suffix.msix"
if (Test-Path $msix) { Remove-Item $msix -Force }

Write-Host "Packing MSIX..."
& $makeappx pack /d $stage /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

# --- self-sign for local sideload testing only --------------------------------
# Store submissions use Partner Center's own signing -- this cert is ONLY so
# Add-AppxPackage will install the thing on a dev machine to test it.
$certSubject = "CN=F94053C2-7FE8-4301-BE68-5CF640A88F39" # must match Package.appxmanifest Publisher
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $certSubject } | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creating a local self-signed test certificate (one-time)..."
    $cert = New-SelfSignedCertificate -Type Custom -Subject $certSubject `
        -KeyUsage DigitalSignature -FriendlyName "FootNote MSIX test cert" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
}
$pfxPath = Join-Path $stage "test-cert.pfx"
$pwd = ConvertTo-SecureString -String "footnote-test" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $pwd | Out-Null

& $signtool sign /fd SHA256 /a /f $pfxPath /p "footnote-test" $msix
if ($LASTEXITCODE -ne 0) { throw "signtool failed" }

$certCer = Join-Path $out "footnote-test-cert.cer"
Export-Certificate -Cert $cert -FilePath $certCer | Out-Null
Remove-Item $stage -Recurse -Force

Write-Host ""
Write-Host "Done -> $msix"
Write-Host ""
Write-Host "This is signed with a local test certificate, not a real one -- it will"
Write-Host "only install on a machine that's both in Developer Mode and trusts that"
Write-Host "certificate. Neither of those is something this script changes for you."
Write-Host "To actually install and test it:"
Write-Host "  1. Enable Developer Mode: Settings -> Privacy & security -> For developers"
Write-Host "  2. Trust the test cert: double-click $certCer -> Install Certificate ->"
Write-Host "     Current User -> Place in 'Trusted Root Certification Authorities'"
Write-Host "  3. Install: Add-AppxPackage -Path `"$msix`""
