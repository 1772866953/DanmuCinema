param([switch]$ForInstaller)
$ErrorActionPreference = 'Stop'
$taskProject = Split-Path -Parent $PSScriptRoot
. (Join-Path $taskProject 'development\scripts\workspace.ps1')

function Get-VerifiedArchive([string]$Url, [string]$Destination, [string]$Sha256) {
    if (Test-Path -LiteralPath $Destination) {
        if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $Sha256) { throw "Archive checksum mismatch: $Destination" }
        Write-Output "Using cached archive: $([IO.Path]::GetFileName($Destination))"
        return
    }
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($Destination)) -Force | Out-Null
    $taskPartial = $Destination + '.partial'
    try {
        Invoke-WebRequest -Uri $Url -OutFile $taskPartial -TimeoutSec 900
        if ((Get-FileHash -LiteralPath $taskPartial -Algorithm SHA256).Hash -ne $Sha256) { throw "Downloaded archive checksum mismatch: $Url" }
        Move-Item -LiteralPath $taskPartial -Destination $Destination
    } finally { if (Test-Path -LiteralPath $taskPartial) { Remove-Item -LiteralPath $taskPartial } }
}

Get-VerifiedArchive 'https://repo.jellyfin.org/files/server/windows/latest-stable/amd64/jellyfin_12.1-amd64.zip' (Join-Path $downloadsRoot 'jellyfin_12.1-amd64.zip') 'E6290748A8674885865EB1F307161F8C455AE12A5CB5F9696E42A0162E182085'
Get-VerifiedArchive 'https://github.com/cxfksword/jellyfin-plugin-danmu/releases/download/v2.8.0/danmu_2.8.0.0.zip' (Join-Path $downloadsRoot 'danmu_2.8.0.0.zip') '34BE037B25A249A24773453F40A81C52F82A88C2A79D2583B62B40EB0F581C74'

$taskElectron = Join-Path $taskProject 'node_modules\electron\dist'
if (!(Test-Path -LiteralPath (Join-Path $taskElectron 'electron.exe'))) { throw 'Run npm ci first; Electron runtime is missing.' }
New-Item -ItemType Directory -Path (Join-Path $taskProject 'tools\electron') -Force | Out-Null
Copy-Item -Path (Join-Path $taskElectron '*') -Destination (Join-Path $taskProject 'tools\electron') -Recurse -Force

if ($ForInstaller) {
    $taskSources = Join-Path $taskProject 'development\dependencies\redistribution-sources.zip'
    if (!(Test-Path -LiteralPath $taskSources)) {
        $taskVersion = (Get-Content -LiteralPath (Join-Path $taskProject 'package.json') -Raw | ConvertFrom-Json).version
        $taskName = "DanmuCinema-Corresponding-Sources-$taskVersion.zip"
        $taskBase = "https://github.com/1772866953/DanmuCinema/releases/download/v$taskVersion/$taskName"
        $taskChecksum = (Invoke-WebRequest -Uri ($taskBase+'.sha256') -TimeoutSec 60).Content
        if ($taskChecksum -is [byte[]]) { $taskChecksum = [Text.Encoding]::UTF8.GetString($taskChecksum) }
        $taskMatch = [regex]::Match($taskChecksum.Trim(), '^([A-Fa-f0-9]{64})\s+' + [regex]::Escape($taskName) + '$')
        if (!$taskMatch.Success) { throw 'Invalid source archive checksum file.' }
        Get-VerifiedArchive $taskBase $taskSources $taskMatch.Groups[1].Value
    }
}
Write-Output 'Dependencies prepared.'
