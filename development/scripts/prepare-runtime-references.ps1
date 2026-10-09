$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'workspace.ps1')
$archive = Join-Path $projectRoot 'downloads\jellyfin_12.1-amd64.zip'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne 'E6290748A8674885865EB1F307161F8C455AE12A5CB5F9696E42A0162E182085') { throw '运行包校验失败。' }
New-Item -ItemType Directory -Path $runtimeReferenceRoot -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName -ne ('jellyfin/' + $entry.Name)) { continue }
        if ($entry.Name -ne 'jellyfin.deps.json' -and !($entry.Name -like '*.dll' -and $entry.Name -match '^(System\.|Microsoft\.(AspNetCore|Extensions)\.|MediaBrowser\.|Jellyfin\.Data|netstandard)' -and $entry.Name -notmatch '\.Native\.dll$')) { continue }
        $destination = Join-Path $runtimeReferenceRoot $entry.Name
        if (!(Test-Path -LiteralPath $destination)) { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$destination,$false) }
    }
} finally { $zip.Dispose() }
if (!(Test-Path -LiteralPath (Join-Path $runtimeReferenceRoot 'MediaBrowser.Controller.dll'))) { throw '未取得编译参考程序集。' }
