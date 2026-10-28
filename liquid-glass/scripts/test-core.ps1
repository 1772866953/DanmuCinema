param([ValidateSet('BridgeContract','MatchingRegression','ManualSearchRegression','ManualSearchProbe','RealHashProbe','ClassificationRegression','LibraryRemovalRegression')][string]$Case='BridgeContract', [string]$InstalledRoot='', [string]$Video='')
$ErrorActionPreference='Stop'
$taskProject=Split-Path -Parent $PSScriptRoot
$taskOutput=Join-Path $taskProject ('artifacts\'+$Case.ToLowerInvariant())
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskWpf=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$taskCore=Join-Path $taskProject 'development\bin\installer-app\DanmuCinema.exe'
Copy-Item -LiteralPath $taskCore -Destination $taskOutput -Force
Copy-Item -LiteralPath ($taskCore+'.config') -Destination $taskOutput -Force
$taskExe=Join-Path $taskOutput ($Case+'.exe')
Copy-Item -LiteralPath ($taskCore+'.config') -Destination ($taskExe+'.config') -Force
& $taskCompiler /nologo /target:exe /platform:x64 /codepage:65001 "/out:$taskExe" "/reference:$taskCore" /reference:System.dll /reference:System.Core.dll /reference:System.Net.Http.dll /reference:System.Web.dll /reference:System.Xaml.dll "/reference:$taskWpf\PresentationCore.dll" "/reference:$taskWpf\PresentationFramework.dll" "/reference:$taskWpf\WindowsBase.dll" (Join-Path $taskProject ('development\tests\'+$Case+'.cs'))
if($LASTEXITCODE){throw 'Contract test compilation failed.'}
& $taskExe $taskOutput $InstalledRoot $Video
if($LASTEXITCODE){throw 'Contract test failed.'}
