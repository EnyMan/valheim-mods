$ErrorActionPreference = 'Stop'
Get-Content 'package\manifest.json' -Raw | ConvertFrom-Json | Out-Null
Write-Output 'manifest.json: valid JSON'
$bytes = [System.IO.File]::ReadAllBytes('dist\icon.png')
$isPng = ($bytes[0] -eq 0x89 -and $bytes[1] -eq 0x50 -and $bytes[2] -eq 0x4E -and $bytes[3] -eq 0x47)
Write-Output ('icon.png PNG signature: ' + $isPng)
$w = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
$h = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
Write-Output ('icon.png dimensions: ' + $w + 'x' + $h)
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile((Resolve-Path 'dist\icon.png'))
Write-Output ('icon.png decoded: ' + $img.Width + 'x' + $img.Height)
$img.Dispose()