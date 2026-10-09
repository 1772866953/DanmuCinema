$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$workspace=Split-Path -Parent $projectRoot
$fixture=Join-Path $projectRoot 'tests\output\server-log-policy'
$report=New-Object 'Collections.Generic.List[string]'
function Check($condition,$text){if(!$condition){throw "FAIL: $text"};$report.Add('PASS: '+$text)}
function Free-Port { $listener=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop();return $port }
[void][Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'bin\installer-app\DanmuCinema.exe'))
[DanmuCinema.Paths]::Root=$fixture
$field=[DanmuCinema.Paths].GetField('RuntimeOverride',[Reflection.BindingFlags]'Static,NonPublic')
$runtimeFixture = Join-Path $projectRoot 'tests\output\server-log-runtime'
if (!(Test-Path -LiteralPath (Join-Path $runtimeFixture 'jellyfin\jellyfin.exe'))) { Expand-Archive -LiteralPath (Join-Path $projectRoot 'downloads\jellyfin_12.1-amd64.zip') -DestinationPath $runtimeFixture -Force }
$field.SetValue($null,(Join-Path $runtimeFixture 'jellyfin'))
$settings=New-Object DanmuCinema.AppSettings
$settings.Port=Free-Port
$settings.LogRetentionDays=7
$settings.EnableDandan=$settings.EnableAnimeko=$settings.EnableBahamut=$settings.EnableExistingDanmu=$false
[DanmuCinema.Log]::Configure(7)
$service=New-Object DanmuCinema.ServiceManager($settings)
try {
    [void]$service.Start().GetAwaiter().GetResult()
    [void]$service.Api.Initialize('log_policy_fixture','Fixture_Log_Policy_Only9').GetAwaiter().GetResult()
    [void]$service.Stop().GetAwaiter().GetResult()
    [void]$service.Start().GetAwaiter().GetResult()
    $info=$service.Api.PublicInfo().GetAwaiter().GetResult()
    Check ($service.OwnsProcess -and [DanmuCinema.Json]::Text($info,'Id')) 'Video service restarts successfully with new file log policy'
    $remote=[DanmuCinema.Json]::Object($service.Api.Request('GET','System/Configuration',$null,$true).GetAwaiter().GetResult())
    Check ([int]$remote['LogFileRetentionDays'] -eq 7) 'Running service loads configured retention of seven days'
    $logging=Get-Content -LiteralPath (Join-Path $fixture 'data\jellyfin\config\logging.default.json') -Raw -Encoding UTF8
    Check ($logging -match '"retainedFileCountLimit"\s*:\s*null') 'Running file sink accepts unlimited count and day-based cleanup'
    $logs=@(Get-ChildItem -LiteralPath (Join-Path $fixture 'data\server-logs') -File)
    Check ($logs.Count -gt 0) 'Video service writes logs normally with the new configuration'
    $result=[DanmuCinema.Log]::ClearAll()
    Check ($result.Pending -gt 0) 'Active service log is deferred without stopping playback service'
    [void]$service.Stop().GetAwaiter().GetResult()
    Check (!(Test-Path -LiteralPath (Join-Path $fixture 'data\log-cleanup-pending.json'))) 'Stopping service removes the deferred log queue'
    Check (@(Get-ChildItem -LiteralPath (Join-Path $fixture 'data\server-logs') -File).Count -eq 0) 'All server logs are removed after releasing the active file'
} finally {
    [void]$service.Stop().GetAwaiter().GetResult();$service.Dispose()
    $report | Set-Content -LiteralPath (Join-Path $projectRoot 'bin\reports\server-log-policy-test.txt') -Encoding UTF8
}
$report
