param([switch]$SkipPrepare,[switch]$UiOnly)
$ErrorActionPreference = 'Stop'
$development = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $development 'tests\output\media-types'
if (!$SkipPrepare) {
if (Test-Path -LiteralPath $testRoot) { throw 'Media type fixture already exists; stop its server and clean the isolated directory first.' }
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
Expand-Archive -LiteralPath (Join-Path $development 'downloads\jellyfin_12.1-amd64.zip') -DestinationPath (Join-Path $testRoot 'runtime')
$plugin = Join-Path $testRoot 'data\jellyfin\plugins\DanmuCinemaPlayback_1.0.0.0'
New-Item -ItemType Directory -Path $plugin -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $development 'bin\playback-plugin\DanmuCinema.Playback.dll'),(Join-Path $development 'bin\playback-plugin\meta.json') -Destination $plugin
$ffmpeg = Join-Path $testRoot 'runtime\jellyfin\ffmpeg.exe'
& $ffmpeg -hide_banner -loglevel error -f lavfi -i 'color=c=black:s=64x64:r=1' -t 1 -c:v libx264 -an (Join-Path $testRoot 'sample.mp4')
if ($LASTEXITCODE -ne 0) { throw 'Synthetic test video generation failed' }
& $ffmpeg -hide_banner -loglevel error -i (Join-Path $testRoot 'sample.mp4') -c copy (Join-Path $testRoot 'sample.mkv')
if ($LASTEXITCODE -ne 0) { throw 'Synthetic MKV generation failed' }
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$runner = Join-Path $development 'bin\installer-app\MediaTypeTests.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$references = @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object { '/reference:'+(Join-Path $wpf $_) }
& $compiler /nologo /target:exe /codepage:65001 ('/reference:'+(Join-Path $development 'bin\installer-app\DanmuCinema.exe')) /reference:System.Core.dll /reference:System.Xaml.dll /reference:System.Web.dll /reference:System.Net.Http.dll $references ('/out:'+$runner) (Join-Path $PSScriptRoot 'MediaTypeTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Media type test compilation failed' }
$arguments = @($testRoot,(Join-Path $development 'bin\reports\media-types.txt'))
if ($UiOnly) { $arguments += '--ui-only' }
& $runner @arguments
if ($LASTEXITCODE -ne 0) { throw 'Media type tests failed' }
