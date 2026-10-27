$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'workspace.ps1')
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
$logo = [Windows.Markup.XamlReader]::Parse([IO.File]::ReadAllText((Join-Path $projectRoot 'assets\AppLogo.xaml')))
$frames = @()
foreach ($size in @(16,20,24,32,40,48,64,96,128,256)) {
    $image = New-Object Windows.Controls.Image
    $image.Source = $logo
    $image.Width = $size
    $image.Height = $size
    $image.Measure((New-Object Windows.Size($size,$size)))
    $image.Arrange((New-Object Windows.Rect(0,0,$size,$size)))
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($image)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    try { $encoder.Save($stream); $frames += [PSCustomObject]@{Size=$size;Bytes=$stream.ToArray()} } finally { $stream.Dispose() }
}
$output = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $encodedSize = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$encodedSize); $writer.Write([byte]$encodedSize); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    [IO.File]::WriteAllBytes((Join-Path $projectRoot 'assets\DanmuCinema.ico'),$output.ToArray())
} finally { $writer.Dispose(); $output.Dispose() }
