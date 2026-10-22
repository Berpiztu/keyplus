<#
.SYNOPSIS
    Berpiztu AI Keyboard's Windows icon (windows\BerpiztuKeyboard.ico) made
    from the Android app's own (Resources\drawable\ic_keyplus.xml), each of
    Windows' sizes drawn from its vector paths in their own colours.
.DESCRIPTION
    Run again only when ic_keyplus.xml changes; the .ico is kept in the
    repository. Each size is a PNG inside the .ico, as Windows reads them
    since Vista.
#>
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore, WindowsBase

$root = Split-Path $PSScriptRoot -Parent
$vector = [xml](Get-Content (Join-Path $root "Resources\drawable\ic_keyplus.xml") -Raw)
$android = "http://schemas.android.com/apk/res/android"
$box = [double]$vector.vector.GetAttribute("viewportWidth", $android)
$sizes = 16, 24, 32, 48, 64, 128, 256

$images = foreach ($size in $sizes) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $context.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size / $box), ($size / $box)))
    foreach ($path in $vector.vector.path) {
        $shape = [System.Windows.Media.Geometry]::Parse("F1 " + $path.GetAttribute("pathData", $android))
        $colour = [System.Windows.Media.ColorConverter]::ConvertFromString($path.GetAttribute("fillColor", $android))
        $context.DrawGeometry((New-Object System.Windows.Media.SolidColorBrush $colour), $null, $shape)
    }
    $context.Pop()
    $context.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $png = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $png.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    $png.Save($stream)
    , $stream.ToArray()
}

# The .ico: its header, one entry a size, then the PNGs in order.
$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $side = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$side); $writer.Write([byte]$side); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
$target = Join-Path $root "windows\BerpiztuKeyboard.ico"
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
Write-Host "Written: $target ($($out.Length) bytes, sizes $($sizes -join ', '))"
