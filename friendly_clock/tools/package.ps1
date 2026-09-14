# Build first (Release), then: pwsh tools/package.ps1 [-Name Friendly_Clock -Dll FriendlyClock -Extra art/clock_*.png]
# Shared by all mods (simple_compas and weather_altar call this script with its own arguments).
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [string]$Name = 'Friendly_Clock',
    [string]$Dll = 'FriendlyClock',
    [string[]]$Extra = @('art/clock_face.png', 'art/clock_hand.png', 'art/clock_cap.png')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$dist = Join-Path $Root 'dist'
$plugins = Join-Path $dist 'BepInEx/plugins'
$dllPath = Join-Path $Root "bin/Release/net48/$Dll.dll"
$package = Join-Path $Root 'package'
if (-not (Test-Path $dllPath)) { throw "$dllPath not found. Build first." }
$version = (Get-Content (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json).version_number

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
# Files the DLL loads from its own folder go flat next to it; the mod manager installs the package into one plugin folder.
Copy-Item $dllPath $plugins
foreach ($e in $Extra) { Copy-Item (Join-Path $Root $e) $plugins }
Copy-Item (Join-Path $package '*') $dist

$img = [System.Drawing.Image]::FromFile((Join-Path $dist 'icon.png'))
try { if ($img.Width -ne 256 -or $img.Height -ne 256) { throw "icon.png must be 256x256" } } finally { $img.Dispose() }

$zip = Join-Path $Root "EnyMan-$Name-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip
Write-Output "Package: $zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::OpenRead($zip)
$z.Entries | ForEach-Object { Write-Output ("  " + $_.FullName + " (" + $_.Length + " bytes)") }
$z.Dispose()
