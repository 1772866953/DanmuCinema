$ErrorActionPreference = 'Stop'
$development = Split-Path -Parent $PSScriptRoot
$root = Join-Path $development 'tests\output\uninstall'
$setup = Join-Path $development 'tests\output\installer-wizard\DanmuCinema-Setup-1.0.5-win-x64.exe'
$report = New-Object 'Collections.Generic.List[string]'
function Check($condition,[string]$message) { if (!$condition) { throw $message }; $report.Add('PASS: '+$message) }
function RunHidden($exe,$arguments) {
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(30000)) { & taskkill.exe /PID $process.Id /T /F | Out-Null; throw 'Probe timed out' }
    $code = $process.ExitCode; $process.Dispose(); return $code
}
foreach ($keep in @($true,$false)) {
    $install = Join-Path $root ('keep-'+$keep)
    $media = Join-Path $install 'data\library-media'
    foreach ($relative in @('data\library-media','data\dandan-cache\fixture','data\cache','data\server-logs','data\jellyfin\data','config')) { New-Item -ItemType Directory -Path (Join-Path $install $relative) -Force | Out-Null }
    '{}' | Set-Content -LiteralPath (Join-Path $install 'data\install-complete.json')
    Check ((RunHidden $setup ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLEANUPPROBE=1 /DIR="'+$install+'"')) -eq 0) 'Isolated uninstaller created'
    foreach ($relative in @('data\dandan-cache\fixture\entry.json','data\cache\thumbnail.jpg','data\server-logs\server.log','data\controller.log','data\anime-catalog-cache.json','data\jellyfin\data\jellyfin.db','data\playback-bridge.json','config\dandanplay.json')) { 'fixture-only' | Set-Content -LiteralPath (Join-Path $install $relative) }
    @{ MediaFolder=$media; EncryptedToken='fixture-only-token'; EncryptedDandanSecret='fixture-only-cipher' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $install 'data\settings.json')
    'original fixture video' | Set-Content -LiteralPath (Join-Path $media 'video.mkv')
    'original fixture danmu' | Set-Content -LiteralPath (Join-Path $media 'video.xml')
    $beforeVideo = (Get-FileHash -LiteralPath (Join-Path $media 'video.mkv')).Hash
    $beforeXml = (Get-FileHash -LiteralPath (Join-Path $media 'video.xml')).Hash
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="'+(Join-Path $root ('uninstall-'+$keep+'.log'))+'"'
    if (!$keep) { $arguments += ' /REMOVECACHELOGS=1' }
    Check ((RunHidden (Join-Path $install 'unins000.exe') $arguments) -eq 0) ('Uninstall succeeds, retain='+$keep)
    foreach ($relative in @('data\settings.json','data\jellyfin\data\jellyfin.db','data\playback-bridge.json','config\dandanplay.json')) { Check (!(Test-Path -LiteralPath (Join-Path $install $relative))) ('Account/API/bridge data removed: '+$relative) }
    foreach ($relative in @('data\dandan-cache\fixture\entry.json','data\cache\thumbnail.jpg','data\server-logs\server.log','data\controller.log','data\anime-catalog-cache.json')) { Check ((Test-Path -LiteralPath (Join-Path $install $relative)) -eq $keep) ('Cache/log retention follows choice: '+$relative) }
    Check ((Get-FileHash -LiteralPath (Join-Path $media 'video.mkv')).Hash -eq $beforeVideo) 'Original video untouched, including inside installation directory'
    Check ((Get-FileHash -LiteralPath (Join-Path $media 'video.xml')).Hash -eq $beforeXml) 'Media sidecar untouched'
}
$report | Set-Content -LiteralPath (Join-Path $development 'bin\reports\uninstall-1.0.5.txt') -Encoding UTF8
$report
