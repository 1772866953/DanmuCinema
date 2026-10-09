$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $projectRoot 'tests\output\public-config'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'bin\installer-app\DanmuCinema.exe')) | Out-Null
[DanmuCinema.Paths]::Root = $testRoot
$report = New-Object 'Collections.Generic.List[string]'
function Check($condition,[string]$message) { if (!$condition) { throw $message }; $report.Add('PASS: '+$message) }
$settings = New-Object DanmuCinema.AppSettings
[DanmuCinema.DandanConfig]::Ensure($settings)
$json = [IO.File]::ReadAllText([DanmuCinema.DandanConfig]::FilePath) | ConvertFrom-Json
Check (!$json.AppId -and !$json.AppSecret -and !($json.PSObject.Properties.Name -contains 'EncryptedAppSecret')) 'Fresh config is blank and has no encrypted credential field'
$config = New-Object DanmuCinema.DandanConfig
$config.EncryptedAppSecret = [DanmuCinema.SettingsStore]::Protect('fixture-encrypted')
Check ($config.Secret -eq 'fixture-encrypted') 'Existing user-encrypted configuration still works'
$config.AppSecret = ' fixture-manual '
Check ($config.Secret -eq 'fixture-manual') 'Manually entered secret takes precedence over existing encrypted value'
$config.EncryptedAppSecret = 'not-a-valid-ciphertext'
Check ($config.Secret -eq 'fixture-manual') 'Manual secret also works with another account ciphertext'
$config.AppId = 'fixture-app'
[DanmuCinema.SettingsStore]::AtomicWrite([DanmuCinema.DandanConfig]::FilePath,[DanmuCinema.Json]::Write($config),$false)
$before = [IO.File]::ReadAllText([DanmuCinema.DandanConfig]::FilePath)
[DanmuCinema.DandanConfig]::Ensure($settings)
Check ([IO.File]::ReadAllText([DanmuCinema.DandanConfig]::FilePath) -eq $before) 'Existing configuration is not overwritten'
$report | Set-Content -LiteralPath (Join-Path $projectRoot 'bin\reports\public-config-1.0.2.txt') -Encoding UTF8
$report
