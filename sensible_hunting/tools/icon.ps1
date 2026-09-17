# Draws package/icon.png (256x256): the in-game ((•)) blip over a trail of deer tracks leading to it.
# Same palette as Simple Compass. Run: pwsh tools/icon.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '../package/icon.png'

$bg    = [System.Drawing.ColorTranslator]::FromHtml('#181E28')
$gold  = [System.Drawing.ColorTranslator]::FromHtml('#FFCC66')
$cream = [System.Drawing.ColorTranslator]::FromHtml('#EFEADD')

$bmp = [System.Drawing.Bitmap]::new(256, 256)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear($bg)

# The blip: centre dot + two arcs each side, exactly the ((•)) shape players see.
$cx = 128; $cy = 78
$dot = [System.Drawing.SolidBrush]::new($gold)
$g.FillEllipse($dot, $cx - 11, $cy - 11, 22, 22)
$pen = [System.Drawing.Pen]::new($gold, 9)
$pen.StartCap = 'Round'; $pen.EndCap = 'Round'
foreach ($r in 32, 56) {
    $g.DrawArc($pen, $cx - $r, $cy - $r, 2 * $r, 2 * $r, 180 - 38, 76)  # left  (
    $g.DrawArc($pen, $cx - $r, $cy - $r, 2 * $r, 2 * $r, -38, 76)       # right )
}

# Deer tracks walking up toward the blip, older (further) ones fainter.
# A cloven hoof print = two slim teardrops side by side, pointing the way the animal went.
function Hoof($x, $y, $angle, $alpha) {
    $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb($alpha, $cream))
    $state = $g.Save()
    $g.TranslateTransform($x, $y)
    $g.RotateTransform($angle)
    foreach ($side in -1, 1) {
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        # rounded heel at the bottom, pointed toe at the top
        $path.AddBezier(($side * 3), -21, ($side * 17), -9, ($side * 16), 17, ($side * 3), 18)
        $path.CloseFigure()
        $g.FillPath($brush, $path)
        $path.Dispose()
    }
    $g.Restore($state)
    $brush.Dispose()
}
# Alternating left/right steps walking up toward the blip.
Hoof 100 222 14  90
Hoof 150 196  8 150
Hoof 112 170  2 205
Hoof 154 146 -4 250

$g.Dispose()
$bmp.Save((Resolve-Path (Split-Path $out)).Path + '\icon.png', [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "Wrote $out"
