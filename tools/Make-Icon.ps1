# Generate src/Firepit/firepit.ico from the brand-flame Path data.
# Run once after the flame shape changes; checked-in .ico is the source of truth
# at build time. Idempotent — re-running with the same path data produces the
# same bytes.

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$ErrorActionPreference = 'Stop'

$pathData = 'M 8,1 C 11,5 12,8 11,11 C 13,9 14,12 14,14 C 14,17 11,18 8,18 C 5,18 2,17 2,14 C 2,12 3,9 5,11 C 4,8 5,5 8,1 Z'
$flameRgb = 0xF5C97B
$viewW    = 14.0
$viewH    = 18.0
$sizes    = @(16, 24, 32, 48, 64, 128, 256)
$outFile  = Join-Path $PSScriptRoot '..\src\Firepit\firepit.ico'

$brushColor = [System.Windows.Media.Color]::FromRgb(
    [byte](($flameRgb -shr 16) -band 0xff),
    [byte](($flameRgb -shr 8)  -band 0xff),
    [byte]( $flameRgb          -band 0xff))
$brush = [System.Windows.Media.SolidColorBrush]::new($brushColor)
$brush.Freeze()

function Render-FlamePng([int]$Size) {
    $margin   = [Math]::Max(1.0, $Size * 0.10)
    $availW   = $Size - 2 * $margin
    $availH   = $Size - 2 * $margin
    $scale    = [Math]::Min($availW / $script:viewW, $availH / $script:viewH)
    $renderW  = $script:viewW * $scale
    $renderH  = $script:viewH * $scale
    $offsetX  = ($Size - $renderW) / 2
    $offsetY  = ($Size - $renderH) / 2

    $geom     = [System.Windows.Media.Geometry]::Parse($script:pathData)
    $tg       = [System.Windows.Media.TransformGroup]::new()
    $tg.Children.Add([System.Windows.Media.ScaleTransform]::new($scale, $scale))
    $tg.Children.Add([System.Windows.Media.TranslateTransform]::new($offsetX, $offsetY))

    $visual   = [System.Windows.Media.DrawingVisual]::new()
    $dc       = $visual.RenderOpen()
    $dc.PushTransform($tg)
    $dc.DrawGeometry($script:brush, $null, $geom)
    $dc.Pop()
    $dc.Close()

    $rtb      = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        $Size, $Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)

    $encoder  = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms       = [System.IO.MemoryStream]::new()
    $encoder.Save($ms)
    # Comma forces PowerShell to return the byte[] as a single object instead
    # of enumerating it into the pipeline.
    return ,$ms.ToArray()
}

$frames = $sizes | ForEach-Object {
    [PSCustomObject]@{ Size = $_; Data = (Render-FlamePng $_) }
}

# ICO container: ICONDIR (6 bytes) + N * ICONDIRENTRY (16 bytes) + payload bytes.
# Each entry stores a PNG-encoded frame; works on Vista+ for all sizes.
$ms = [System.IO.MemoryStream]::new()
$bw = [System.IO.BinaryWriter]::new($ms)

$bw.Write([UInt16]0)                    # reserved
$bw.Write([UInt16]1)                    # type: icon
$bw.Write([UInt16]$frames.Count)

$dataOffset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $sz = if ($f.Size -ge 256) { 0 } else { $f.Size }   # 0 = 256 in the spec
    $bw.Write([byte]$sz)                # width
    $bw.Write([byte]$sz)                # height
    $bw.Write([byte]0)                  # palette colors (0 = no palette / >=8bpp)
    $bw.Write([byte]0)                  # reserved
    $bw.Write([UInt16]1)                # color planes
    $bw.Write([UInt16]32)               # bits per pixel
    $bw.Write([UInt32]$f.Data.Length)   # payload size
    $bw.Write([UInt32]$dataOffset)
    $dataOffset += $f.Data.Length
}
foreach ($f in $frames) { $bw.Write($f.Data) }
$bw.Flush()

$resolved = [System.IO.Path]::GetFullPath($outFile)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolved)) | Out-Null
[System.IO.File]::WriteAllBytes($resolved, $ms.ToArray())
Write-Host "Wrote $resolved ($($ms.Length) bytes, $($frames.Count) sizes: $($sizes -join ','))"
