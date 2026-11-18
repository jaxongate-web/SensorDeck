# Generates src/SensorDeck.App/Assets/SensorDeck.ico: a blue gradient rounded square with a white pulse line.
# Small sizes are stored as 32-bit BMP frames (best compatibility); 256 px is stored as PNG.
param([string]$Out = (Join-Path $PSScriptRoot "..\src\SensorDeck.App\Assets\SensorDeck.ico"))

Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 24.0
    $inset = [Math]::Max(0.5, 1 * $s)
    $rect = New-Object System.Drawing.RectangleF $inset, $inset, ($size - 2 * $inset), ($size - 2 * $inset)
    $r = 6 * $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($rect.Right - 2 * $r, $rect.Y, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($rect.Right - 2 * $r, $rect.Bottom - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, `
        ([System.Drawing.Color]::FromArgb(255, 0x1D, 0x4E, 0xD8)), ([System.Drawing.Color]::FromArgb(255, 0x60, 0xA5, 0xFA)), 45.0
    $g.FillPath($brush, $path)

    # Pulse line (same shape as the in-app logo)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.6, 2.3 * $s))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pts = @(@(4.5, 12), @(8, 12), @(10, 7), @(14, 17), @(16, 12), @(19.5, 12)) | ForEach-Object {
        New-Object System.Drawing.PointF ($_[0] * $s), ($_[1] * $s) }
    $g.DrawLines($pen, [System.Drawing.PointF[]]$pts)

    $pen.Dispose(); $brush.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

function Get-BmpFrame([System.Drawing.Bitmap]$bmp) {
    $n = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    # BITMAPINFOHEADER (height doubled for XOR + AND masks)
    $w.Write([int]40); $w.Write([int]$n); $w.Write([int]($n * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $n - 1; $y -ge 0; $y--) {          # bottom-up BGRA pixels
        for ($x = 0; $x -lt $n; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $maskStride = [int]([Math]::Ceiling($n / 32.0) * 4)   # AND mask, all zero (alpha channel is used)
    $w.Write((New-Object byte[] ($maskStride * $n)))
    $w.Flush()
    return ,$ms.ToArray()   # comma stops PowerShell unrolling the byte array
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = foreach ($sz in $sizes) {
    $bmp = New-IconBitmap $sz
    if ($sz -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $data = $ms.ToArray()
    } else {
        $data = Get-BmpFrame $bmp
    }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $sz; Data = [byte[]]$data }
}

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$frames.Count)   # ICONDIR
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {                                                     # ICONDIRENTRY
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]$f.Data.Length); $bw.Write([int]$offset)
    $offset += $f.Data.Length
}
foreach ($f in $frames) { $bw.Write([byte[]]$f.Data) }
$bw.Close()
"Wrote $Out ($((Get-Item $Out).Length) bytes, sizes: $($sizes -join ', '))"

# Preview PNG for documentation
$preview = New-IconBitmap 256
$preview.Save((Join-Path (Split-Path $Out) "SensorDeck-256.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()
