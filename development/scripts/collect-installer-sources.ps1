# Source inventory is derived from Jellyfin-FFmpeg v8.1.2-1's msys2 PKGBUILD
# files. Downloads are build-time only; the installer never downloads them.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'workspace.ps1')
$sourceRoot = Join-Path $projectRoot 'downloads\installer-sources\ffmpeg-dependencies'
New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
$entries = Get-Content -LiteralPath (Join-Path $projectRoot 'installer\ffmpeg-dependencies.json') -Raw | ConvertFrom-Json
$results = $entries | ForEach-Object -Parallel {
    $entry = $_
    $target = Join-Path $using:sourceRoot $entry.filename
    try {
        if (!(Test-Path -LiteralPath $target)) {
            if ($entry.subsetPaths) {
                $full = $target + '.full'
                Invoke-WebRequest -Uri $entry.url -OutFile $full -TimeoutSec 180
                $unpack = Join-Path $using:sourceRoot ($entry.package + '-extract')
                New-Item -ItemType Directory -Path $unpack -Force | Out-Null
                & tar -xf $full -C $unpack $entry.subsetPaths
                if ($LASTEXITCODE -ne 0) { throw '头文件源码子集解压失败' }
                & tar -czf $target -C $unpack ($entry.subsetPaths[0].Split('/')[0])
                if ($LASTEXITCODE -ne 0) { throw '头文件源码子集归档失败' }
                Remove-Item -LiteralPath $full -Force
                # Gzip/tar metadata is not stable between host tools. The public
                # package builder uses the already verified release archive.
            } else {
                Invoke-WebRequest -Uri $entry.url -OutFile $target -TimeoutSec 60 -MaximumRetryCount 2 -RetryIntervalSec 2
            }
        }
        $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        if ($entry.sha256 -ne 'SKIP' -and $hash -ne $entry.sha256) { throw '上游构建记录的 SHA256 不匹配' }
        [ordered]@{package=$entry.package;source=$entry.source;url=$entry.url;filename=$entry.filename;sha256=$hash;size=(Get-Item -LiteralPath $target).Length;ok=$true}
    } catch {
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
        [ordered]@{package=$entry.package;url=$entry.url;ok=$false;error=$_.Exception.Message}
    }
} -ThrottleLimit 6
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $sourceRoot 'download-manifest.json') -Encoding UTF8
$failed = @($results | Where-Object { !$_.ok })
Write-Output "源码归档成功：$(@($results | Where-Object ok).Count)；失败：$($failed.Count)"
if ($failed.Count) { $failed | ForEach-Object { $_.package + ': ' + $_.error }; throw '外部源码未补齐，不可构建公开分发包。' }
