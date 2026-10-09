param([string]$Version = '1.0.8', [string]$IsccPath, [string]$PythonPath = 'python')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'workspace.ps1')
$stage = Join-Path $projectRoot 'bin\installer-payload'
$dist = $packagesRoot
if (!$IsccPath) { $IsccPath = Join-Path $projectRoot 'bin\installer-tools\inno\ISCC.exe' }
if (!(Test-Path -LiteralPath $IsccPath)) { throw '请先安装 Inno Setup 6.7 或更新版本，并用 -IsccPath 指定 ISCC.exe。' }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '安装版本必须采用 x.y.z 格式。' }
$serverZip = Join-Path $projectRoot 'downloads\jellyfin_12.1-amd64.zip'
$danmuZip = Join-Path $projectRoot 'downloads\danmu_2.8.0.0.zip'
if ((Get-FileHash -LiteralPath $serverZip -Algorithm SHA256).Hash -ne 'E6290748A8674885865EB1F307161F8C455AE12A5CB5F9696E42A0162E182085') { throw 'Jellyfin 运行包校验失败。' }
if ((Get-FileHash -LiteralPath $danmuZip -Algorithm MD5).Hash -ne '8498D9A1ED6CDFE27B94D78E77D2752B') { throw '弹幕插件校验失败。' }
& (Join-Path $PSScriptRoot 'build.ps1') -ForInstaller
if (Test-Path -LiteralPath $stage) {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    if ($resolvedStage -ne [IO.Path]::GetFullPath((Join-Path $projectRoot 'bin\installer-payload'))) { throw '暂存目录不安全。' }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
foreach ($directory in @('bin','assets','runtime\jellyfin','plugins\Danmu_2.8.0.0','plugins\DanmuCinemaPlayback_1.0.0.0','licenses')) {
    New-Item -ItemType Directory -Path (Join-Path $stage $directory) -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'bin\installer-app\DanmuCinema.exe'), (Join-Path $projectRoot 'bin\installer-app\DanmuCinema.exe.config') -Destination (Join-Path $stage 'bin')
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\DanmuCinema.ico') -Destination (Join-Path $stage 'assets')
Expand-Archive -LiteralPath $serverZip -DestinationPath (Join-Path $stage 'runtime')
Expand-Archive -LiteralPath $danmuZip -DestinationPath (Join-Path $stage 'plugins\Danmu_2.8.0.0')
Copy-Item -LiteralPath (Join-Path $projectRoot 'bin\playback-plugin\DanmuCinema.Playback.dll'), (Join-Path $projectRoot 'bin\playback-plugin\meta.json') -Destination (Join-Path $stage 'plugins\DanmuCinemaPlayback_1.0.0.0')
Copy-Item -LiteralPath (Join-Path $workspaceRoot 'LICENSE') -Destination (Join-Path $stage 'licenses\LICENSE-DanmuCinema.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\LICENSE-Jellyfin.txt'), (Join-Path $projectRoot 'docs\LICENSE-Danmu.txt') -Destination (Join-Path $stage 'licenses')
$upstreamSources = Join-Path $projectRoot 'downloads\installer-sources'
& $PythonPath (Join-Path $PSScriptRoot 'collect-license-texts.py') $upstreamSources (Join-Path $stage 'licenses')
if ($LASTEXITCODE -ne 0) { throw '离线许可证收集失败。构建机器需要 Python 3.9 或更新版本。' }
foreach ($archive in @('jellyfin-12.1.tar.gz','jellyfin-web-12.1.tar.gz','jellyfin-ffmpeg-8.1.2-1.tar.gz','danmu-2.8.0.tar.gz')) {
    if (!(Test-Path -LiteralPath (Join-Path $upstreamSources $archive))) { throw "缺少第三方源码归档：$archive" }
}
# Include the exact controller/bridge sources used in this build. This whitelist
# intentionally excludes private config, user data, tests/output and README.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceZip = Join-Path $projectRoot 'bin\DanmuCinema-source.zip'
$zipStream = [IO.File]::Open($sourceZip, [IO.FileMode]::Create)
$zip = New-Object IO.Compression.ZipArchive($zipStream, [IO.Compression.ZipArchiveMode]::Create)
try {
    $sourceFiles = @(Get-Item -LiteralPath (Join-Path $workspaceRoot 'LICENSE'))
    $sourceFiles += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs') -File | Where-Object Name -like 'LICENSE*'
    foreach ($directory in @('src','desktop','plugin','assets','scripts','installer')) {
        $sourceFiles += Get-ChildItem -LiteralPath (Join-Path $projectRoot $directory) -File -Recurse
    }
    foreach ($file in $sourceFiles) {
        if ($file.Extension -in @('.md','.txt') -and $file.Name -notlike 'LICENSE*') { continue }
        $relative = $file.FullName.Substring($workspaceRoot.Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $zip.Dispose(); $zipStream.Dispose() }
$files = Get-ChildItem -LiteralPath $stage -File -Recurse
if ($files | Where-Object { $_.FullName -match '\\data\\|\\config\\dandanplay[^\\]*\.json$|\.bak$|\.log$|\.partial$' }) { throw '暂存目录包含个人配置、示例配置或缓存。' }
if (Get-ChildItem -LiteralPath (Join-Path $stage 'licenses') -Recurse -File | Where-Object Name -notlike 'LICENSE*') { throw '许可证目录只能包含 LICENSE 文件。' }
$manifest = $files | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($stage.Length + 1).Replace('\','/'); size = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
}
New-Item -ItemType Directory -Path $dist,(Join-Path $projectRoot 'bin\reports') -Force | Out-Null
$completeSources = Join-Path $dist "DanmuCinema-Corresponding-Sources-$Version.zip"
$sourceStream = [IO.File]::Open($completeSources, [IO.FileMode]::Create)
$completeZip = New-Object IO.Compression.ZipArchive($sourceStream, [IO.Compression.ZipArchiveMode]::Create)
try {
    $licenseRoot = Join-Path $stage 'licenses'
    foreach ($file in (Get-ChildItem -LiteralPath $licenseRoot -Recurse -File)) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($completeZip, $file.FullName, $file.FullName.Substring($licenseRoot.Length + 1).Replace('\','/'), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($completeZip, $sourceZip, 'sources/DanmuCinema-source.zip', [IO.Compression.CompressionLevel]::NoCompression) | Out-Null
    foreach ($file in (Get-ChildItem -LiteralPath $upstreamSources -Filter '*.tar.gz' -File)) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($completeZip, $file.FullName, 'sources/' + $file.Name, [IO.Compression.CompressionLevel]::NoCompression) | Out-Null
    }
    $dependencyRoot = Join-Path $projectRoot 'downloads\installer-sources\ffmpeg-dependencies'
    foreach ($entry in (Get-Content -LiteralPath (Join-Path $projectRoot 'installer\ffmpeg-dependencies.json') -Raw | ConvertFrom-Json)) {
        $file = Join-Path $dependencyRoot $entry.filename
        if (!(Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.sha256) { throw "缺失或校验失败的公开分发源码：$($entry.filename)" }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($completeZip, $file, 'sources/ffmpeg-dependencies/' + $entry.filename, [IO.Compression.CompressionLevel]::NoCompression) | Out-Null
    }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($completeZip, (Join-Path $projectRoot 'installer\ffmpeg-dependencies.json'), 'sources/ffmpeg-dependencies/manifest.json', [IO.Compression.CompressionLevel]::Optimal) | Out-Null
} finally { $completeZip.Dispose(); $sourceStream.Dispose() }
& $PythonPath (Join-Path $PSScriptRoot 'verify-public-package.py') $stage $sourceZip
if ($LASTEXITCODE -ne 0) { throw '公开分发隐私检查失败，已停止编译安装器。' }
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $projectRoot 'bin\reports\payload-manifest.json') -Encoding UTF8
& $IsccPath "/DPackageRoot=$stage" "/DOutputFolder=$dist" "/DPackageVersion=$Version" (Join-Path $projectRoot 'installer\DanmuCinema.iss')
if ($LASTEXITCODE -ne 0) { throw '安装包编译失败。' }
$installer = Join-Path $dist "DanmuCinema-Setup-$Version-win-x64.exe"
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
[IO.File]::WriteAllText($installer + '.sha256', $hash + '  ' + [IO.Path]::GetFileName($installer) + "`r`n", (New-Object Text.UTF8Encoding($false)))
$sourceHash = (Get-FileHash -LiteralPath $completeSources).Hash
[IO.File]::WriteAllText($completeSources + '.sha256', $sourceHash + '  ' + [IO.Path]::GetFileName($completeSources) + "`r`n", (New-Object Text.UTF8Encoding($false)))
Write-Output "离线安装包：$installer"
Write-Output "大小：$((Get-Item -LiteralPath $installer).Length) 字节"
