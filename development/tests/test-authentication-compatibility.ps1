param([switch]$ApplyCurrent)
$ErrorActionPreference = 'Stop'
$development = Split-Path -Parent $PSScriptRoot
$app = Join-Path $development 'bin\installer-app'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$runner = Join-Path $app 'AuthenticationCompatibilityTests.exe'
& $compiler /nologo /target:exe /codepage:65001 ('/reference:'+(Join-Path $app 'DanmuCinema.exe')) /reference:System.Core.dll /reference:System.Net.Http.dll ('/out:'+$runner) (Join-Path $PSScriptRoot 'AuthenticationCompatibilityTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Authentication test compilation failed' }
Copy-Item -LiteralPath (Join-Path $app 'DanmuCinema.exe.config') -Destination ($runner+'.config') -Force
if ($ApplyCurrent) {
    & $runner '--apply-current' 'C:\Software\弹幕\DanmuCinema' (Join-Path $development 'bin\reports\authentication-current.txt')
} else {
    $fixture = Join-Path $PSScriptRoot 'output\authentication-compatibility'
    if (Test-Path -LiteralPath $fixture) { throw 'Authentication fixture already exists; clean it after verifying its server is stopped.' }
    New-Item -ItemType Directory -Path $fixture -Force | Out-Null
    Expand-Archive -LiteralPath (Join-Path $development 'downloads\jellyfin_12.1-amd64.zip') -DestinationPath (Join-Path $fixture 'runtime')
    & $runner $fixture 'unused' (Join-Path $development 'bin\reports\authentication-compatibility.txt')
}
if ($LASTEXITCODE -ne 0) { throw 'Authentication compatibility checks failed' }
