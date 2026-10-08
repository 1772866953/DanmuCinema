param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows .NET Framework 4.x。' }
New-Item -ItemType Directory -Path (Join-Path $projectRoot 'bin') -Force | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName
$sources += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'desktop') -Filter '*.cs' | ForEach-Object FullName
$outputName = if ($env:DANMU_BUILD_UPDATE -eq '1') { 'DanmuCinema.updated.exe' } else { 'DanmuCinema.exe' }
$output = Join-Path (Join-Path $projectRoot 'bin') $outputName
$icon = Join-Path $projectRoot 'assets\DanmuCinema.ico'
& (Join-Path $PSScriptRoot 'build-icon.ps1')
$logoResource = '/resource:' + (Join-Path $projectRoot 'assets\AppLogo.xaml') + ',DanmuCinema.Desktop.AppLogo.xaml'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$wpfReferences = @('PresentationCore.dll', 'PresentationFramework.dll', 'WindowsBase.dll', 'System.Xaml.dll') | ForEach-Object {
    $path = Join-Path $wpf $_
    if (!(Test-Path -LiteralPath $path)) { $path = Join-Path (Split-Path $wpf -Parent) $_ }
    if (!(Test-Path -LiteralPath $path)) { throw "WPF component not found: $_" }
    '/reference:' + $path
}
$xamlResources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'desktop') -Filter '*.xaml' | ForEach-Object { '/resource:' + $_.FullName + ',DanmuCinema.Desktop.' + $_.Name }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /utf8output "/out:$output" "/win32icon:$icon" "/resource:$icon,DanmuCinema.AppIcon" $logoResource /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll /reference:System.Web.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.Xml.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $wpfReferences $xamlResources $sources
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\DanmuCinema.exe.config') -Destination ($output + '.config') -Force
if (Test-Path -LiteralPath (Join-Path $projectRoot 'runtime\jellyfin\MediaBrowser.Controller.dll')) { & (Join-Path $PSScriptRoot 'build-playback-plugin.ps1') }
& (Join-Path $PSScriptRoot 'create-shortcuts.ps1')
Write-Output "构建完成：$output"
if ($Test) {
    $testProcess = Start-Process -FilePath $output -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath (Join-Path $projectRoot 'tests\output\self-test.txt') -Encoding UTF8
    if ($testProcess.ExitCode -ne 0) { throw '自检失败。' }
    $wpfTest = Start-Process -FilePath $output -ArgumentList '--wpf-test' -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath (Join-Path $projectRoot 'tests\output\wpf-test.txt') -Encoding UTF8
    if ($wpfTest.ExitCode -ne 0) { throw 'WPF 自检失败。' }
}
