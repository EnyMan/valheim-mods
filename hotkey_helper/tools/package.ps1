# Build first (Release), then: pwsh tools/package.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$plugins = Join-Path $dist 'BepInEx\plugins'
$dll = Join-Path $root 'bin\Release\net48\HotkeyHelper.dll'
$manifest = Join-Path $root 'package\manifest.json'
$readme = Join-Path $root 'package\README.md'
$icon = Join-Path $root 'package\icon.png'

if (-not (Test-Path $dll)) { throw "HotkeyHelper.dll not found. Build first." }
$version = (Get-Content $manifest -Raw | ConvertFrom-Json).version_number

# Placeholder icon (256x256 PNG): a keycap. Replace package/icon.png with real art any time.
if (-not (Test-Path $icon)) {
    $bmp = New-Object System.Drawing.Bitmap 256, 256
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::FromArgb(38, 30, 22))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 70, 45))), 40, 48, 176, 168)
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(140, 110, 70))), 52, 56, 152, 140)
    $font = New-Object System.Drawing.Font 'Segoe UI', 84, ([System.Drawing.FontStyle]::Bold)
    $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $g.DrawString('?', $font, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 205, 90))), (New-Object System.Drawing.RectangleF 52, 56, 152, 140), $fmt)
    $g.Dispose(); $bmp.Save($icon, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Output "Generated placeholder $icon"
}

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
Copy-Item $dll $plugins
Copy-Item $manifest, $readme, $icon $dist

$img = [System.Drawing.Image]::FromFile($icon)
try { if ($img.Width -ne 256 -or $img.Height -ne 256) { throw "icon.png must be 256x256" } } finally { $img.Dispose() }

$zip = Join-Path $root "Hotkey_Helper-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip
Write-Output "Package: $zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::OpenRead($zip)
$z.Entries | ForEach-Object { Write-Output ("  " + $_.FullName + " (" + $_.Length + " bytes)") }
$z.Dispose()
