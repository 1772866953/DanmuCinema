$ErrorActionPreference = 'Stop'
$development = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$application = Join-Path $development 'bin\installer-app\DanmuCinema.exe'
$runner = Join-Path $development 'bin\installer-app\LibraryScanTests.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$references = @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object { '/reference:'+(Join-Path $wpf $_) }
& $compiler /nologo /target:exe /codepage:65001 ('/reference:'+$application) /reference:System.Core.dll /reference:System.Xaml.dll /reference:System.Net.Http.dll $references ('/out:'+$runner) (Join-Path $PSScriptRoot 'LibraryScanTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Library scan test compilation failed' }
& $runner (Join-Path $development 'tests\output\library-scan') (Join-Path $development 'bin\reports\library-scan.txt')
if ($LASTEXITCODE -ne 0) { throw 'Library scan tests failed' }
