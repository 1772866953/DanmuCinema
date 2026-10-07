param([switch]$Offline)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$downloads = Join-Path $projectRoot 'downloads'
$runtime = Join-Path $projectRoot 'runtime'
$server = Join-Path $runtime 'jellyfin'
$pluginDirectory = Join-Path $projectRoot 'data\jellyfin\plugins\Danmu_2.8.0.0'
New-Item -ItemType Directory -Path $downloads,$runtime,$pluginDirectory -Force | Out-Null
$packages = @(
    @{ Name='jellyfin_12.1-amd64.zip'; Url='https://repo.jellyfin.org/files/server/windows/latest-stable/amd64/jellyfin_12.1-amd64.zip'; Algorithm='SHA256'; Hash='E6290748A8674885865EB1F307161F8C455AE12A5CB5F9696E42A0162E182085' },
    @{ Name='danmu_2.8.0.0.zip'; Url='https://github.com/cxfksword/jellyfin-plugin-danmu/releases/download/v2.8.0/danmu_2.8.0.0.zip'; Algorithm='MD5'; Hash='8498d9a1ed6cdfe27b94d78e77d2752b' }
)
foreach ($package in $packages) {
    $archivePath = Join-Path $downloads $package.Name
    if (!(Test-Path -LiteralPath $archivePath)) {
        if ($Offline) { throw "缺少离线安装包：$archivePath" }
        Write-Output ("正在下载 " + $package.Name + '，首次下载约 210 MB，请稍候…')
        $partialPath = $archivePath + '.partial'
        $downloadClient = New-Object Net.WebClient
        try { $downloadClient.DownloadFile($package.Url, $partialPath) }
        finally { $downloadClient.Dispose() }
        Move-Item -LiteralPath $partialPath -Destination $archivePath -Force
    }
    Write-Output ('校验 ' + $package.Name)
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm $package.Algorithm).Hash -ne $package.Hash) {
        throw ('安装包校验失败，请删除 downloads 下对应安装包后重试：' + $package.Name)
    }
}
if (!(Test-Path -LiteralPath (Join-Path $server 'jellyfin.exe'))) {
    Write-Output '正在解压 Jellyfin 12.1…'
    $staging = Join-Path $runtime ('staging-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    Expand-Archive -LiteralPath (Join-Path $downloads 'jellyfin_12.1-amd64.zip') -DestinationPath $staging
    $extracted = Join-Path $staging 'jellyfin'
    if (!(Test-Path -LiteralPath (Join-Path $extracted 'jellyfin.exe'))) { throw '官方安装包中未找到 jellyfin.exe。' }
    if (Test-Path -LiteralPath $server) { throw 'runtime/jellyfin 中已有不完整的文件，请将这个目录改名后重试。' }
    Move-Item -LiteralPath $extracted -Destination $server
    Remove-Item -LiteralPath $staging # now empty; no recursive removal
}
Write-Output '正在安装 Danmu 2.8.0.0…'
Expand-Archive -LiteralPath (Join-Path $downloads 'danmu_2.8.0.0.zip') -DestinationPath $pluginDirectory -Force
if (!(Test-Path -LiteralPath (Join-Path $pluginDirectory 'Jellyfin.Plugin.Danmu.dll'))) { throw '弹幕插件解压失败。' }
& (Join-Path $PSScriptRoot 'build-playback-plugin.ps1') -Install
Write-Output '运行组件已就绪。启动服务后，在首次设置中创建账号并添加媒体库。'
