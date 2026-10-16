# Renders the Ã‡ekirdek logo (design/logo/svg/1-cekirdek.svg) into the PNG and ICO assets the MSIX package and the
# tray icon need. The SVG is simple enough to draw directly with GDI+, so no SVG renderer is required.
# Run from anywhere: .\windows\tools\Generate-Assets.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\Fenpo.App\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-LogoBitmap([int]$width, [int]$height, [double]$logoSize) {
    $bitmap = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # The SVG uses a 64x64 view box; scale it to logoSize and center it.
    $scale = $logoSize / 64.0
    $g.TranslateTransform(($width - $logoSize) / 2.0, ($height - $logoSize) / 2.0)
    $g.ScaleTransform($scale, $scale)

    # Rounded square: x=3 y=3 58x58 rx=19, fill Ã‡iÃ§ek #E8577E.
    $square = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 19.0; $d = 2 * $r; $x = 3.0; $y = 3.0; $s = 58.0
    $square.AddArc($x, $y, $d, $d, 180, 90)
    $square.AddArc($x + $s - $d, $y, $d, $d, 270, 90)
    $square.AddArc($x + $s - $d, $y + $s - $d, $d, $d, 0, 90)
    $square.AddArc($x, $y + $s - $d, $d, $d, 90, 90)
    $square.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#E8577E'))), $square)

    # Inner disc: r=21 at 32,32, fill #FFF6F0.
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#FFF6F0'))), 11.0, 11.0, 42.0, 42.0)

    # Five seeds, MÃ¼rekkep #1B1F3B: M32 15.5 c3.4 4.2 3.7 9 0 11.6 c-3.7-2.6-3.4-7.4 0-11.6z, rotated by 72Â°.
    $ink = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#1B1F3B'))
    for ($i = 0; $i -lt 5; $i++) {
        $seed = New-Object System.Drawing.Drawing2D.GraphicsPath
        $seed.AddBezier(32.0, 15.5, 35.4, 19.7, 35.7, 24.5, 32.0, 27.1)
        $seed.AddBezier(32.0, 27.1, 28.3, 24.5, 28.6, 19.7, 32.0, 15.5)
        $seed.CloseFigure()
        $matrix = New-Object System.Drawing.Drawing2D.Matrix
        $matrix.RotateAt(72 * $i, (New-Object System.Drawing.PointF 32, 32))
        $seed.Transform($matrix)
        $g.FillPath($ink, $seed)
    }

    $g.Dispose()
    return $bitmap
}

function Save-Png([string]$name, [int]$width, [int]$height, [double]$logoSize) {
    $bitmap = New-LogoBitmap $width $height $logoSize
    $bitmap.Save((Join-Path $assets $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

# Square44x44: app list and taskbar. Plated (scale) and unplated (targetsize) variants.
foreach ($scale in 100, 150, 200, 400) { $px = [int](44 * $scale / 100); Save-Png "Square44x44Logo.scale-$scale.png" $px $px $px }
foreach ($size in 16, 24, 32, 48, 256) {
    Save-Png "Square44x44Logo.targetsize-$size.png" $size $size $size
    Save-Png "Square44x44Logo.altform-unplated_targetsize-$size.png" $size $size $size
    Save-Png "Square44x44Logo.altform-lightunplated_targetsize-$size.png" $size $size $size
}
foreach ($scale in 100, 200) { $px = [int](150 * $scale / 100); Save-Png "Square150x150Logo.scale-$scale.png" $px $px ($px * 0.66) }
foreach ($scale in 100, 200) { $w = [int](310 * $scale / 100); $h = [int](150 * $scale / 100); Save-Png "Wide310x150Logo.scale-$scale.png" $w $h ($h * 0.66) }
foreach ($scale in 100, 200) { $px = [int](50 * $scale / 100); Save-Png "StoreLogo.scale-$scale.png" $px $px $px }
foreach ($scale in 100, 200) { $w = [int](620 * $scale / 100); $h = [int](300 * $scale / 100); Save-Png "SplashScreen.scale-$scale.png" $w $h ($h * 0.5) }
Save-Png 'LockScreenLogo.scale-200.png' 48 48 48

# Unqualified logo for in-app use (title bar, tray flyout, pairing wizard); MRT would pick a plated variant otherwise.
Save-Png 'AppLogo.png' 96 96 96

# Fenpo.ico: tray and window icon, PNG-compressed entries.
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($size in $sizes) {
    $bitmap = New-LogoBitmap $size $size $size
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $stream.ToArray()
}

$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $ico
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]($(if ($size -ge 256) { 0 } else { $size })))
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'Fenpo.ico'), $ico.ToArray())

Write-Host "Assets written to $((Resolve-Path $assets).Path)"
