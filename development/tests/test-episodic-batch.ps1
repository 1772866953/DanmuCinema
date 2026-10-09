$ErrorActionPreference = 'Stop'
$development = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$application = Join-Path $development 'bin\installer-app\DanmuCinema.exe'
$runner = Join-Path $development 'bin\installer-app\EpisodicBatchTests.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$references = @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object { '/reference:'+(Join-Path $wpf $_) }
& $compiler /nologo /target:exe /codepage:65001 ('/reference:'+$application) /reference:System.Core.dll /reference:System.Xaml.dll $references ('/out:'+$runner) (Join-Path $PSScriptRoot 'EpisodicBatchTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Episode batch test compilation failed' }
& $runner (Join-Path $development 'tests\output\episodic-batch') (Join-Path $development 'bin\reports\episodic-batch.txt')
if ($LASTEXITCODE -ne 0) { throw 'Episode batch tests failed' }
