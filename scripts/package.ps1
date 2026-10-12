<#
.SYNOPSIS
  Builds the portable Windows x64 release of Akeno ColorVision.

.DESCRIPTION
  Publishes the app and the emergency reset tool as self-contained single-file executables
  (no .NET installation needed on the target PC), adds the quick-start guide and license,
  and creates dist/Akeno-ColorVision-v<version>-win-x64.zip plus a SHA-256 checksum file.

.EXAMPLE
  pwsh scripts/package.ps1 -Version 0.1.0
#>
param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"
$stage = Join-Path $dist "Akeno-ColorVision"
$zipName = "Akeno-ColorVision-v$Version-$Runtime.zip"
$zip = Join-Path $dist $zipName

if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Path $stage | Out-Null

Write-Host "Publishing app ($Version, $Runtime)..."
dotnet publish (Join-Path $root "src/Akeno.ColorVision.App/Akeno.ColorVision.App.csproj") `
    -c $Configuration -r $Runtime --self-contained true -p:Version=$Version -o $stage
if ($LASTEXITCODE -ne 0) { throw "App publish failed." }

Write-Host "Publishing emergency reset tool..."
dotnet publish (Join-Path $root "src/Akeno.ColorVision.Recovery/Akeno.ColorVision.Recovery.csproj") `
    -c $Configuration -r $Runtime --self-contained true -p:Version=$Version -o $stage
if ($LASTEXITCODE -ne 0) { throw "Recovery publish failed." }

# Keep the folder clean for non-technical users: only the two executables and the documents.
Get-ChildItem $stage -File | Where-Object { $_.Extension -in ".pdb", ".xml", ".json" } | Remove-Item -Force

Copy-Item (Join-Path $root "docs/QUICK-START.txt") (Join-Path $stage "READ ME FIRST.txt")
Copy-Item (Join-Path $root "LICENSE") (Join-Path $stage "LICENSE.txt")
Copy-Item (Join-Path $root "CHANGELOG.md") (Join-Path $stage "CHANGELOG.md")

$required = @("AkenoColorVision.exe", "AkenoColorVision-EmergencyReset.exe")
foreach ($file in $required) {
    if (-not (Test-Path (Join-Path $stage $file))) { throw "Missing $file in the package." }
}

Write-Host "Creating $zipName..."
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $zipName" -NoNewline -Encoding ascii

Get-ChildItem $stage | Format-Table Name, Length
Write-Host "SHA-256: $hash"
Write-Host "Done: $zip"
