# Shared Thunderstore packager. Build the mod (Release) first, then from anywhere:
#   pwsh tools/package.ps1 <mod folder> [-Extra files...]
# -Name defaults to package/manifest.json "name", -Dll to the csproj <AssemblyName>.
# -Extra paths are relative to the mod folder (wildcards ok) and land flat next to the DLL.
param(
    [Parameter(Mandatory)][string]$Mod,
    [string]$Name,
    [string]$Dll,
    [string[]]$Extra = @()
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Root = (Resolve-Path $Mod).Path
$package = Join-Path $Root 'package'
$manifest = Get-Content (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
if (-not $Name) { $Name = $manifest.name }
if (-not $Dll) { $Dll = ([xml](Get-Content (Get-ChildItem $Root -Filter *.csproj | Select-Object -First 1).FullName)).Project.PropertyGroup.AssemblyName | Where-Object { $_ } | Select-Object -First 1 }
$version = $manifest.version_number

$dist = Join-Path $Root 'dist'
$plugins = Join-Path $dist 'BepInEx/plugins'
$dllPath = Join-Path $Root "bin/Release/net48/$Dll.dll"
if (-not (Test-Path $dllPath)) { throw "$dllPath not found. Build first." }

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
# Files the DLL loads from its own folder go flat next to it; the mod manager installs the package into one plugin folder.
Copy-Item $dllPath $plugins
foreach ($e in $Extra) { Copy-Item (Join-Path $Root $e) $plugins }
Copy-Item (Join-Path $package '*') $dist
Copy-Item (Join-Path $PSScriptRoot '../LICENSE') $dist

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
