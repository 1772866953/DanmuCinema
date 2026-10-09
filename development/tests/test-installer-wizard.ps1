$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$workspace = Split-Path -Parent $projectRoot
$testRoot = Join-Path $projectRoot 'tests\output\installer-wizard'
if (Test-Path -LiteralPath $testRoot) {
    if ([IO.Path]::GetFullPath($testRoot) -ne [IO.Path]::GetFullPath((Join-Path $projectRoot 'tests\output\installer-wizard'))) { throw 'Unsafe test path.' }
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
$payload = Join-Path $testRoot 'probe-payload'
foreach ($dir in @('bin','assets','runtime','plugins\Danmu_2.8.0.0','plugins\DanmuCinemaPlayback_1.0.0.0','config','licenses')) { New-Item -ItemType Directory -Path (Join-Path $payload $dir) -Force | Out-Null }
Copy-Item -LiteralPath (Join-Path $projectRoot 'bin\installer-app\DanmuCinema.exe') -Destination (Join-Path $payload 'bin') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\DanmuCinema.ico') -Destination (Join-Path $payload 'assets') -Force
foreach ($dir in @('runtime','plugins\Danmu_2.8.0.0','plugins\DanmuCinemaPlayback_1.0.0.0','licenses')) { 'Wizard callback test only; not a distributable runtime.' | Set-Content -LiteralPath (Join-Path $payload ($dir+'\LICENSE.txt')) -Encoding UTF8 }
$compiler = Join-Path $projectRoot 'bin\installer-tools\inno\ISCC.exe'
& $compiler '/DTestBuild=1' "/DPackageRoot=$payload" "/DOutputFolder=$testRoot" '/DPackageVersion=1.0.5' (Join-Path $projectRoot 'installer\DanmuCinema.iss') *> (Join-Path $testRoot 'compile.log')
if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath (Join-Path $testRoot 'compile.log') -Tail 18; throw 'Wizard probe compilation failed.' }
$setup = Join-Path $testRoot 'DanmuCinema-Setup-1.0.5-win-x64.exe'
$fresh = Join-Path $testRoot '新安装目录 with spaces'; $existing = Join-Path $testRoot '已有安装目录'
New-Item -ItemType Directory -Path (Join-Path $existing 'data') -Force | Out-Null
'{}' | Set-Content -LiteralPath (Join-Path $existing 'data\install-complete.json') -Encoding UTF8
$report = Join-Path $testRoot 'report.txt'
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
$arguments = '/WIZARDPROBE="'+$report+'" /PROBENEW="'+$fresh+'" /PROBEEXISTING="'+$existing+'" /LOG="'+(Join-Path $testRoot 'wizard.log')+'" /NORESTART'
$process = Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(30000)) {
    & taskkill.exe /PID $process.Id /T /F | Out-Null
    throw 'Wizard callback probe did not close within 30 seconds.'
}
$process.Dispose()
if (!(Test-Path -LiteralPath $report)) { throw 'Wizard probe did not write a report.' }
$result = Get-Content -LiteralPath $report -Encoding UTF8 -Raw
if ($result -notmatch '^PASS:') { throw $result }
$log = Get-Content -LiteralPath (Join-Path $testRoot 'wizard.log') -Encoding UTF8 -Raw
if ($log -match 'Runtime error|before it was initialized|Internal error') { throw 'Runtime error occurred during wizard callbacks.' }
if (Test-Path -LiteralPath (Join-Path $fresh 'bin')) { throw 'Probe unexpectedly installed files.' }
$result.Trim()
'PASS: wizard closes without runtime dialogs or installing files' | Add-Content -LiteralPath $report -Encoding UTF8
$cleanupRoot = Join-Path $testRoot 'cleanup-probe'
New-Item -ItemType Directory -Path (Join-Path $cleanupRoot 'config'),(Join-Path $cleanupRoot 'data'),(Join-Path $cleanupRoot 'licenses\sources') -Force | Out-Null
$privateFixture = '{"AppId":"fixture-only","AppSecret":"fixture-private"}'
[IO.File]::WriteAllText((Join-Path $cleanupRoot 'config\dandanplay.json'),$privateFixture)
'{}' | Set-Content -LiteralPath (Join-Path $cleanupRoot 'data\install-complete.json')
'old template' | Set-Content -LiteralPath (Join-Path $cleanupRoot 'config\dandanplay.example.json')
'old documentation' | Set-Content -LiteralPath (Join-Path $cleanupRoot 'licenses\THIRD-PARTY.md')
'old source' | Set-Content -LiteralPath (Join-Path $cleanupRoot 'licenses\sources\DanmuCinema-source.zip')
$process = Start-Process -FilePath $setup -ArgumentList ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLEANUPPROBE=1 /DIR="'+$cleanupRoot+'"') -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(30000)) { & taskkill.exe /PID $process.Id /T /F | Out-Null; throw 'Cleanup probe timed out.' }
if ($process.ExitCode -ne 0) { throw 'Cleanup probe installation failed.' }
$process.Dispose()
if (Test-Path -LiteralPath (Join-Path $cleanupRoot 'config\dandanplay.example.json')) { throw 'Legacy example survived upgrade.' }
if ([IO.File]::ReadAllText((Join-Path $cleanupRoot 'config\dandanplay.json')) -ne $privateFixture) { throw 'Upgrade modified real API config.' }
if (Get-ChildItem -LiteralPath (Join-Path $cleanupRoot 'licenses') -Recurse -File | Where-Object Name -notlike 'LICENSE*') { throw 'Legacy license documentation survived upgrade.' }
'PASS: upgrade removes example/docs/source archive while preserving actual API config' | Add-Content -LiteralPath $report -Encoding UTF8
