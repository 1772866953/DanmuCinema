param([switch]$SkipCompile)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $projectRoot 'tests\output\installer'
$installRoot = Join-Path $testRoot '中文目录 with spaces'
$mediaRoot = Join-Path $testRoot '测试视频'
$report = New-Object 'Collections.Generic.List[string]'
function Check($condition, [string]$message) {
    if (!$condition) { throw "FAIL: $message" }
    $report.Add("PASS: $message")
}
function Run-Hidden([string]$file, [string]$arguments) {
    $process = Start-Process -FilePath $file -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    return $process.ExitCode
}
New-Item -ItemType Directory -Path $testRoot,$mediaRoot -Force | Out-Null
$testInstaller = Join-Path $testRoot 'DanmuCinema-Setup-1.0.5-win-x64.exe'
if (!$SkipCompile) {
    & (Join-Path $projectRoot 'bin\installer-tools\inno\ISCC.exe') '/DTestBuild=1' "/DOutputFolder=$testRoot" (Join-Path $projectRoot 'installer\DanmuCinema.iss') *> (Join-Path $testRoot 'compile.log')
    if ($LASTEXITCODE -ne 0) { throw '测试安装包编译失败。' }
}
if (Test-Path -LiteralPath (Join-Path $installRoot 'data\settings.json')) { throw '请先清理上一次隔离安装测试目录。' }
$provision = Join-Path $testRoot 'private-provision.json'
$apiConfig = Join-Path $testRoot 'private-official.json'
$password = 'Fixture_$"中文_Install9'
$options = @{AdminName='installer_admin';Password=$password;MediaFolder=$mediaRoot;LibraryType='tvshows';Port=18096;DanmuPort=19321;CloseToTray=$true;AutoStart=$false;DandanConfigFile=$apiConfig}
@{AppId='fixture-only-app';AppSecret='fixture-only-secret';CallbackUrl='https://example.invalid/callback'} | ConvertTo-Json | Set-Content -LiteralPath $apiConfig -Encoding UTF8
$options | ConvertTo-Json | Set-Content -LiteralPath $provision -Encoding UTF8
$server = $null
try {
    $setupArgs = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS /DIR="' + $installRoot + '" /CONFIG="' + $provision + '" /LOG="' + (Join-Path $testRoot 'setup.log') + '"'
    $code = Run-Hidden $testInstaller $setupArgs
    if ($code -ne 0) {
        $controllerLog = Join-Path $installRoot 'data\controller.log'
        if (Test-Path -LiteralPath $controllerLog) { Get-Content -LiteralPath $controllerLog -Tail 15 }
        Get-Content -LiteralPath (Join-Path $testRoot 'setup.log') -Tail 15
    }
    Check ($code -eq 0) 'Offline EXE installs successfully into a Chinese path containing spaces'
    $app = Join-Path $installRoot 'bin\DanmuCinema.exe'
    $settingsFile = Join-Path $installRoot 'data\settings.json'
    $settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
    Check ($settings.AdminName -eq 'installer_admin' -and $settings.MediaFolder -eq $mediaRoot) 'Wizard provision creates selected account and media path'
    Check ($settings.StartServicesOnLaunch -and $settings.CloseToTray) 'First launch starts services and retains selected close behavior'
    Check (Test-Path -LiteralPath (Join-Path $installRoot 'data\install-complete.json')) 'Initialization completes before installation reports success'
    $secretText = Get-Content -LiteralPath (Join-Path $installRoot 'config\dandanplay.json') -Raw
    $secret = $secretText | ConvertFrom-Json
    Add-Type -AssemblyName System.Security
    $decoded = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($secret.EncryptedAppSecret), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser))
    Check ($secret.AppId -eq 'fixture-only-app' -and $decoded -eq 'fixture-only-secret' -and !$secret.AppSecret) 'Optional API import encrypts credentials for the installing user'
    $allText = ($settings | ConvertTo-Json -Depth 8) + $secretText + (Get-Content -LiteralPath (Join-Path $testRoot 'setup.log') -Raw)
    Check (!$allText.Contains($password) -and !$secretText.Contains('fixture-only-secret')) 'Password and imported API secret are absent from settings and installer logs'
    Check ((Run-Hidden $app '--installer-check') -eq 0) 'Installation helper confirms the isolated app is stopped'
    $beforeUpgrade = (Get-FileHash -LiteralPath $settingsFile).Hash
    Check ((Run-Hidden $testInstaller $setupArgs) -eq 0) 'Reinstall/upgrade completes successfully'
    Check ((Get-FileHash -LiteralPath $settingsFile).Hash -eq $beforeUpgrade) 'Upgrade preserves existing settings and account credentials'
    $serverExe = Join-Path $installRoot 'runtime\jellyfin\jellyfin.exe'
    Check (Test-Path -LiteralPath $serverExe) 'Video service and its runtime are embedded in the EXE'
    $serverArgs = '--datadir "' + (Join-Path $installRoot 'data\jellyfin') + '" --cachedir "' + (Join-Path $installRoot 'data\cache') + '" --logdir "' + (Join-Path $installRoot 'data\server-logs') + '" --webdir "' + (Join-Path $installRoot 'runtime\jellyfin\jellyfin-web') + '"'
    $server = Start-Process -FilePath $serverExe -ArgumentList $serverArgs -WorkingDirectory (Split-Path $serverExe) -WindowStyle Hidden -PassThru
    $base = 'http://127.0.0.1:18096/'
    $healthy = $false
    for ($i=0;$i -lt 60;$i++) {
        try { if ((Invoke-RestMethod -Uri ($base+'health') -TimeoutSec 2) -eq 'Healthy') { $healthy=$true; break } } catch { }
        Start-Sleep -Milliseconds 500
    }
    Check $healthy 'Installed video service restarts using the embedded runtime'
    $headers = @{Authorization='MediaBrowser Client="DanmuCinema Installer Test", Device="Windows", DeviceId="installer-fixture", Version="1.0"'}
    $login = Invoke-RestMethod -Uri ($base+'Users/AuthenticateByName') -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes((@{Username='installer_admin';Pw=$password}|ConvertTo-Json)))
    Check ($login.User.Policy.IsAdministrator -and $login.AccessToken) 'Chosen password with punctuation and Chinese characters logs in as administrator'
    $auth = @{Authorization=$headers.Authorization + ', Token="' + $login.AccessToken + '"'}
    $libraries = Invoke-RestMethod -Uri ($base+'Library/VirtualFolders') -Headers $auth
    Check (@($libraries | Where-Object { $_.Locations -contains $mediaRoot -and $_.CollectionType -eq 'tvshows' }).Count -eq 1) 'Chosen media folder is created once with the requested library type'
    $plugins = Invoke-RestMethod -Uri ($base+'Plugins') -Headers $auth
    foreach ($name in @('Danmu','DanmuCinema Playback')) {
        Check (@($plugins | Where-Object { $_.Name -eq $name -and $_.Status -eq 'Active' }).Count -eq 1) "Embedded plugin loads without any download: $name"
    }
    Invoke-RestMethod -Uri ($base+'System/Shutdown') -Method Post -Headers $auth -ContentType 'application/json' -Body '{}' | Out-Null
    Check ($server.WaitForExit(15000)) 'Only the test-owned video server is shut down cleanly'
    $server.Dispose(); $server=$null
    $request = Join-Path $testRoot 'invalid-request.json'
    $invalidRoot = Join-Path $testRoot 'invalid'
    New-Item -ItemType Directory -Path (Join-Path $invalidRoot 'bin') -Force | Out-Null
    Copy-Item -LiteralPath $app,($app+'.config') -Destination (Join-Path $invalidRoot 'bin')
    $options.Password='short'
    $options | ConvertTo-Json | Set-Content -LiteralPath $request -Encoding UTF8
    Check ((Run-Hidden (Join-Path $invalidRoot 'bin\DanmuCinema.exe') ('--installer-configure "'+$request+'"')) -ne 0) 'Invalid administrator password is rejected'
    Check (!(Test-Path -LiteralPath $request) -and !(Test-Path -LiteralPath (Join-Path $invalidRoot 'data\settings.json'))) 'Rejected input is consumed without creating a partially configured server'
    # Deliberately own the requested port; the helper must neither stop its owner nor save settings.
    $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Any, 18096)
    $listener.Start()
    try {
        $options.Password=$password
        $options | ConvertTo-Json | Set-Content -LiteralPath $request -Encoding UTF8
        Check ((Run-Hidden (Join-Path $invalidRoot 'bin\DanmuCinema.exe') ('--installer-configure "'+$request+'"')) -ne 0) 'Occupied port prevents initialization'
        Check (!(Test-Path -LiteralPath (Join-Path $invalidRoot 'data\settings.json'))) 'Port conflict leaves unrelated listener and app data intact'
    } finally { $listener.Stop() }
    Check ((Run-Hidden (Join-Path $installRoot 'unins000.exe') '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART') -eq 0) 'Native uninstaller removes the isolated installation'
    Check (!(Test-Path -LiteralPath $app) -and (Test-Path -LiteralPath $settingsFile) -and (Test-Path -LiteralPath $mediaRoot)) 'Uninstall removes executables while preserving settings and media'
    $manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'bin\reports\payload-manifest.json') -Raw | ConvertFrom-Json
    Check (@($manifest | Where-Object path -Match '^data/|dandanplay\.json$|\.log$|\.bak$|tests/output').Count -eq 0) 'Payload whitelist excludes personal configuration, media, caches and logs'
    $report.Add('No live API requests, existing application shutdown, GUI automation or power actions were used.')
} finally {
    if ($server) { if (!$server.HasExited) { Stop-Process -Id $server.Id -Force }; $server.Dispose() }
    foreach ($privateFile in @($provision,$apiConfig)) { if (Test-Path -LiteralPath $privateFile) { Remove-Item -LiteralPath $privateFile -Force } }
    $report | Set-Content -LiteralPath (Join-Path $projectRoot 'tests\output\installer-test.txt') -Encoding UTF8
}
$report
