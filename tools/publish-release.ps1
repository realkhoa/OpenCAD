# ==============================================================================
# OpenCAD Release Publishing Script
# Generates Portable (Framework-dependent) and Standalone (Self-contained)
# binary distributions and compressed zip packages.
# ==============================================================================

$ErrorActionPreference = 'Stop'

Write-Host "== [1/4] Publishing Framework-Dependent (Portable) Release ==" -ForegroundColor Cyan
$distPortable = "dist/publish-win-x64"
dotnet publish src/OpenCad.App/OpenCad.App.csproj -c Release -o $distPortable

Write-Host "== [2/4] Publishing Self-Contained Standalone Single-File Release ==" -ForegroundColor Cyan
$distStandalone = "dist/publish-win-x64-standalone"
dotnet publish src/OpenCad.App/OpenCad.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $distStandalone

Write-Host "== [3/4] Packaging Distributions ==" -ForegroundColor Cyan
$releaseDir = "dist/releases"
if (Test-Path $releaseDir) {
    Remove-Item $releaseDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

# 1. Portable package folder
$portablePkg = Join-Path $releaseDir "OpenCAD-win-x64-portable"
New-Item -ItemType Directory -Force -Path $portablePkg | Out-Null
Copy-Item "$distPortable/*" $portablePkg -Recurse -Force
$portableSamples = Join-Path $portablePkg "samples"
New-Item -ItemType Directory -Force -Path $portableSamples | Out-Null
Copy-Item "samples/demo.dwg" (Join-Path $portableSamples "demo.dwg") -Force
Copy-Item "samples/can-ho.dwg" (Join-Path $portableSamples "can-ho.dwg") -Force

Set-Content -Path (Join-Path $portablePkg "README.txt") -Value @"
OpenCAD v1.0.0 (Portable)
==============================================================
Requirements: Windows 10/11 x64 with .NET 10.0 Desktop Runtime.

Quick Start:
  .\OpenCad.App.exe
Or open sample drawing:
  .\OpenCad.App.exe samples\demo.dwg
"@ -Encoding utf8

$portableZip = Join-Path $releaseDir "OpenCAD-v1.0.0-win-x64-portable.zip"
Compress-Archive -Path "$portablePkg/*" -DestinationPath $portableZip -Force
Write-Host "Created: $portableZip" -ForegroundColor Green

# 2. Standalone package folder
$standalonePkg = Join-Path $releaseDir "OpenCAD-win-x64-standalone"
New-Item -ItemType Directory -Force -Path $standalonePkg | Out-Null
Copy-Item (Join-Path $distStandalone "OpenCad.App.exe") $standalonePkg -Force
$standaloneSamples = Join-Path $standalonePkg "samples"
New-Item -ItemType Directory -Force -Path $standaloneSamples | Out-Null
Copy-Item "samples/demo.dwg" (Join-Path $standaloneSamples "demo.dwg") -Force
Copy-Item "samples/can-ho.dwg" (Join-Path $standaloneSamples "can-ho.dwg") -Force

Set-Content -Path (Join-Path $standalonePkg "README.txt") -Value @"
OpenCAD v1.0.0 (Standalone Single-File)
==============================================================
Requirements: Windows 10/11 x64 (No .NET installation needed).

Quick Start:
  .\OpenCad.App.exe
Or open sample drawing:
  .\OpenCad.App.exe samples\demo.dwg
"@ -Encoding utf8

$standaloneZip = Join-Path $releaseDir "OpenCAD-v1.0.0-win-x64-standalone.zip"
Compress-Archive -Path "$standalonePkg/*" -DestinationPath $standaloneZip -Force
Write-Host "Created: $standaloneZip" -ForegroundColor Green

Write-Host "== [4/4] Release Manifest ==" -ForegroundColor Cyan
Get-ChildItem $releaseDir -Filter *.zip | Select-Object Name, @{Name="Size (MB)"; Expression={[math]::Round($_.Length / 1MB, 2)}}, LastWriteTime | Format-Table -AutoSize
