$ErrorActionPreference = 'Stop'
$taskProject = Split-Path -Parent $PSScriptRoot
$taskElectron = Join-Path $taskProject 'tools\electron\electron.exe'
if (!(Test-Path -LiteralPath $taskElectron)) { $taskElectron = Join-Path $taskProject 'node_modules\electron\dist\electron.exe' }
if (!(Test-Path -LiteralPath (Join-Path $taskProject 'dist\index.html'))) { & (Join-Path $PSScriptRoot 'build.ps1'); if($LASTEXITCODE) { exit $LASTEXITCODE } }
Start-Process -FilePath $taskElectron -ArgumentList ('"' + $taskProject + '"') -WorkingDirectory $taskProject -WindowStyle Hidden
