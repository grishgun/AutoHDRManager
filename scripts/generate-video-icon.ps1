# Original project artwork: blue film frame and white play symbol. No third-party assets.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$frames = foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.ScaleTransform($size/256.0,$size/256.0)
    $blue = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,0,102,204))
    $white = [Drawing.SolidBrush]::new([Drawing.Color]::White)
    $graphics.FillRectangle($blue,16,32,224,192)
    foreach ($x in @(28,208)) { foreach ($y in @(44,84,124,164,204)) { $graphics.FillRectangle($white,$x,$y,20,12) } }
    $graphics.FillPolygon($white,[Drawing.PointF[]]@([Drawing.PointF]::new(100,79),[Drawing.PointF]::new(100,177),[Drawing.PointF]::new(172,128)))
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    [pscustomobject]@{Size=$size;Bytes=$stream.ToArray()}
    $stream.Dispose(); $white.Dispose(); $blue.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/AutoHDRManager.Windows/Assets/Video.ico'
New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
$writer = [IO.BinaryWriter]::new([IO.File]::Create($target))
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([uint16]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose() }
