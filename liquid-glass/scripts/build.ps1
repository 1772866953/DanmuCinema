param([switch]$SkipRuntime)
$ErrorActionPreference = 'Stop'
$taskProject = Split-Path -Parent $PSScriptRoot
$taskWorkspace = Split-Path -Parent $taskProject
. (Join-Path $taskProject 'development\scripts\workspace.ps1')
$taskNode = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
$taskPwsh = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe'
if (!(Test-Path -LiteralPath $taskNode)) { $taskNode = (Get-Command node -ErrorAction Stop).Source }
if (!(Test-Path -LiteralPath $taskPwsh)) { $taskPwsh = (Get-Command pwsh -ErrorAction Stop).Source }
Push-Location $taskProject
try {
    & $taskPwsh -NoProfile -ExecutionPolicy Bypass -File development/scripts/build.ps1 -ForInstaller
    if ($LASTEXITCODE) { throw 'Media core build failed.' }
    & $taskNode node_modules/typescript/bin/tsc --noEmit
    if ($LASTEXITCODE) { throw 'Type checking failed.' }
    & $taskNode node_modules/vite/bin/vite.js build --configLoader runner
    if ($LASTEXITCODE) { throw 'Frontend build failed.' }
    if (!$SkipRuntime) {
        $taskDownloads = $downloadsRoot
        if (!(Test-Path -LiteralPath runtime/jellyfin/jellyfin.exe)) { Expand-Archive -LiteralPath (Join-Path $taskDownloads 'jellyfin_12.1-amd64.zip') -DestinationPath runtime -Force }
        if (!(Test-Path -LiteralPath plugins/Danmu_2.8.0.0/Jellyfin.Plugin.Danmu.dll)) { Expand-Archive -LiteralPath (Join-Path $taskDownloads 'danmu_2.8.0.0.zip') -DestinationPath plugins/Danmu_2.8.0.0 -Force }
        $taskRuntimeRoot = Join-Path $taskProject 'runtime-data'
        $taskPluginRoot = Join-Path $taskRuntimeRoot 'data\jellyfin\plugins'
        New-Item -ItemType Directory -Path $taskPluginRoot -Force | Out-Null
        Copy-Item -LiteralPath plugins/Danmu_2.8.0.0 -Destination $taskPluginRoot -Recurse -Force
        $taskPlayback = Join-Path $taskPluginRoot 'DanmuCinemaPlayback_1.1.0.0'
        New-Item -ItemType Directory -Path $taskPlayback -Force | Out-Null
        Copy-Item -Path development/bin/playback-plugin/* -Destination $taskPlayback -Force
        $taskScriptRoot = Join-Path $taskRuntimeRoot 'scripts'
        New-Item -ItemType Directory -Path $taskScriptRoot -Force | Out-Null
        Copy-Item -LiteralPath development/scripts/configure-firewall.ps1 -Destination $taskScriptRoot -Force
    }
} finally { Pop-Location }
Write-Output 'DanmuCinema Liquid Glass build complete.'
