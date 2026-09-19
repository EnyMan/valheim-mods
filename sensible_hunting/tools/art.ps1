# White-on-black generated art -> white-on-transparent 256x256 locator icons.
#   pwsh tools/art.ps1            # art/raw/*.png -> art/*.png
#   pwsh tools/art.ps1 -SelfTest  # synthetic anti-aliased disc, asserts the alpha ramp survives
#
# Alpha comes from brightness, and every pixel's RGB is forced to white first. That is what keeps
# anti-aliased edges honest: a 50% grey edge pixel becomes white at 50% alpha instead of a grey
# fringe, and because RGB is uniform white everywhere, downscaling can only resample alpha - there
# is no darker neighbour to bleed a halo in from.
param(
    [string]$In,
    [string]$Out,
    [int]$Size = 256,
    [double]$Margin = 0.06,   # fraction of the canvas kept clear on the tightest side
    [double]$Floor = 0.04,    # brightness at or below this is background (kills faint haze)
    [double]$Ceil = 0.92,     # brightness at or above this is fully opaque
    [switch]$SelfTest
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Convert-Silhouette([string]$src, [string]$dst) {
    $bmp = [System.Drawing.Bitmap]::FromFile((Resolve-Path $src))
    try {
        $w = $bmp.Width; $h = $bmp.Height
        $rect = [System.Drawing.Rectangle]::new(0, 0, $w, $h)
        $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $bytes = [byte[]]::new($data.Stride * $h)
        [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
        $stride = $data.Stride
        $bmp.UnlockBits($data)

        # Brightness -> alpha, RGB -> white, and the alpha bounding box in the same pass.
        $x0 = $w; $y0 = $h; $x1 = -1; $y1 = -1
        $span = $Ceil - $Floor
        for ($y = 0; $y -lt $h; $y++) {
            $row = $y * $stride
            for ($x = 0; $x -lt $w; $x++) {
                $i = $row + $x * 4
                $lum = ([Math]::Max([Math]::Max($bytes[$i], $bytes[$i + 1]), $bytes[$i + 2])) / 255.0
                $a = [Math]::Round(255 * [Math]::Min(1.0, [Math]::Max(0.0, ($lum - $Floor) / $span)))
                $bytes[$i] = 255; $bytes[$i + 1] = 255; $bytes[$i + 2] = 255; $bytes[$i + 3] = [byte]$a
                if ($a -gt 8) {
                    if ($x -lt $x0) { $x0 = $x }; if ($x -gt $x1) { $x1 = $x }
                    if ($y -lt $y0) { $y0 = $y }; if ($y -gt $y1) { $y1 = $y }
                }
            }
        }
        if ($x1 -lt 0) { throw "$src is blank after keying - is the background actually black?" }

        $keyed = [System.Drawing.Bitmap]::new($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $kd = $keyed.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            [Runtime.InteropServices.Marshal]::Copy($bytes, 0, $kd.Scan0, $bytes.Length)
            $keyed.UnlockBits($kd)

            # Trim to the shape, then fit it into the canvas so every species reads at one weight.
            $cw = $x1 - $x0 + 1; $ch = $y1 - $y0 + 1
            $box = $Size * (1.0 - 2.0 * $Margin)
            $scale = [Math]::Min($box / $cw, $box / $ch)
            $dw = [Math]::Max(1, [Math]::Round($cw * $scale)); $dh = [Math]::Max(1, [Math]::Round($ch * $scale))
            $out = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $g = [System.Drawing.Graphics]::FromImage($out)
                try {
                    $g.Clear([System.Drawing.Color]::Transparent)
                    # Straight copy of the resampled pixels; nothing to blend against on an empty canvas.
                    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $g.DrawImage($keyed,
                        [System.Drawing.Rectangle]::new([int](($Size - $dw) / 2), [int](($Size - $dh) / 2), $dw, $dh),
                        [System.Drawing.Rectangle]::new($x0, $y0, $cw, $ch),
                        [System.Drawing.GraphicsUnit]::Pixel)
                } finally { $g.Dispose() }
                $out.Save($dst, [System.Drawing.Imaging.ImageFormat]::Png)
            } finally { $out.Dispose() }
        } finally { $keyed.Dispose() }
    } finally { $bmp.Dispose() }
    Write-Output "  $([IO.Path]::GetFileName($dst)) <- $([IO.Path]::GetFileName($src)) (${Size}x${Size})"
}

if ($SelfTest) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) "sh_art_test"
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null
    $src = Join-Path $tmp 'disc.png'; $dst = Join-Path $tmp 'disc_out.png'
    $b = [System.Drawing.Bitmap]::new(512, 512)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.Clear([System.Drawing.Color]::Black)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.FillEllipse([System.Drawing.Brushes]::White, 100, 100, 300, 300)
    $g.Dispose(); $b.Save($src, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()

    Convert-Silhouette $src $dst
    $o = [System.Drawing.Bitmap]::FromFile($dst)
    $fail = 0
    function Check($label, $actual, $expected) {
        if ($actual -eq $expected) { Write-Output "ok   $label = $actual" } else { Write-Output "FAIL $label = $actual (expected $expected)"; $script:fail++ }
    }
    Check 'output width' $o.Width 256
    Check 'corner is transparent' $o.GetPixel(1, 1).A 0
    Check 'centre is opaque' $o.GetPixel(128, 128).A 255
    Check 'centre is white' ("{0},{1},{2}" -f $o.GetPixel(128, 128).R, $o.GetPixel(128, 128).G, $o.GetPixel(128, 128).B) '255,255,255'
    # The shape is trimmed and padded, so it starts one margin in (6% of 256 ~ 15 px).
    Check 'shape reaches the margin' ($o.GetPixel(128, 17).A -gt 200) $true
    # Anti-aliasing: somewhere down the left edge there must be a partial alpha, white all the same.
    # A premultiplied edge would come back as grey (R ~ A) instead of white; 255 vs 254 is rounding.
    $ramp = $false; $fringe = $false
    for ($x = 0; $x -lt 256; $x++) {
        $p = $o.GetPixel($x, 128)
        if ($p.A -gt 20 -and $p.A -lt 235) { $ramp = $true; if ($p.R -lt 250) { $fringe = $true } }
    }
    Check 'anti-aliased edge kept as partial alpha' $ramp $true
    Check 'no grey fringe on the edge' $fringe $false
    $o.Dispose()
    if ($fail) { throw "$fail check(s) failed" }
    Write-Output "Self-test OK"
    return
}

if (-not $In) { $In = Join-Path $PSScriptRoot '../art/raw' }
if (-not $Out) { $Out = Join-Path $PSScriptRoot '../art/icons' }
if (-not (Test-Path $In)) { throw "No input folder $In - drop the generated PNGs there." }
New-Item -ItemType Directory -Path $Out -Force | Out-Null
# Nano Banana hands back JPEGs, so take those too and always write a PNG.
$files = Get-ChildItem $In | Where-Object { $_.Extension -match '^\.(png|jpg|jpeg)$' }
if (-not $files) { throw "No PNG or JPEG in $In" }
Write-Output "Keying $($files.Count) image(s):"
foreach ($f in $files) {
    Convert-Silhouette $f.FullName (Join-Path $Out ([IO.Path]::GetFileNameWithoutExtension($f.Name) + '.png'))
}

# Contact sheet, gold on dark: the only way to judge a silhouette is at the size it is drawn, and the
# right-hand thumbnail is 34 px, what a blip right next to you actually gets.
$icons = Get-ChildItem $Out -Filter *.png | Sort-Object Name
$cols = 4; $cellW = 190; $cellH = 140
$rows = [Math]::Ceiling($icons.Count / $cols)
$sheet = [System.Drawing.Bitmap]::new($cols * $cellW, [Math]::Max(1, $rows) * $cellH)
$g = [System.Drawing.Graphics]::FromImage($sheet)
try {
    $g.Clear([System.Drawing.Color]::FromArgb(26, 26, 28))
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $cm = [System.Drawing.Imaging.ColorMatrix]::new()
    $cm.Matrix00 = 1.0; $cm.Matrix11 = 0.79; $cm.Matrix22 = 0.25; $cm.Matrix33 = 1.0   # the HUD's gold
    $ia = [System.Drawing.Imaging.ImageAttributes]::new(); $ia.SetColorMatrix($cm)
    $font = [System.Drawing.Font]::new('Consolas', 11)
    $i = 0
    foreach ($f in $icons) {
        $img = [System.Drawing.Bitmap]::FromFile($f.FullName)
        try {
            $cx = ($i % $cols) * $cellW; $cy = [Math]::Floor($i / $cols) * $cellH
            $g.DrawImage($img, [System.Drawing.Rectangle]::new($cx + 12, $cy + 14, 88, 88), 0, 0, $img.Width, $img.Height, [System.Drawing.GraphicsUnit]::Pixel, $ia)
            $g.DrawImage($img, [System.Drawing.Rectangle]::new($cx + 118, $cy + 40, 34, 34), 0, 0, $img.Width, $img.Height, [System.Drawing.GraphicsUnit]::Pixel, $ia)
            $g.DrawString($f.BaseName, $font, [System.Drawing.Brushes]::Gainsboro, $cx + 12, $cy + 108)
        } finally { $img.Dispose() }
        $i++
    }
} finally { $g.Dispose() }
$preview = Join-Path $Out '../preview.png'
$sheet.Save($preview, [System.Drawing.Imaging.ImageFormat]::Png); $sheet.Dispose()
Write-Output "Preview: $preview"
