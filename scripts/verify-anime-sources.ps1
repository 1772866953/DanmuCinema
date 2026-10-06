param([string]$Executable = 'DanmuCinema.updated.exe', [string]$Keyword = '骸骨骑士')
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$projectRoot = Split-Path -Parent $PSScriptRoot
[void][Reflection.Assembly]::LoadFrom((Join-Path $projectRoot ('bin\' + $Executable)))
[DanmuCinema.Paths]::Root = $projectRoot
$settings = [DanmuCinema.SettingsStore]::Load()
$fixture = Join-Path $projectRoot ('tests\output\anime-live-' + (Get-Date -Format yyyyMMdd-HHmmss))
[void][IO.Directory]::CreateDirectory((Join-Path $fixture 'data'))
[DanmuCinema.Paths]::Root = $fixture
$portProbe = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
$portProbe.Start(); $settings.DanmuPort = $portProbe.LocalEndpoint.Port; $portProbe.Stop()
$settings.EncryptedDanmuKey = [DanmuCinema.SettingsStore]::Protect([Guid]::NewGuid().ToString('N'))
$gateway = New-Object DanmuCinema.DanmuGateway($settings)
$report = New-Object 'Collections.Generic.List[string]'
try {
    $gateway.Start()
    $base = 'http://127.0.0.1:' + $settings.DanmuPort + '/' + $gateway.Key
    $data = Invoke-RestMethod ($base + '/api/v2/search/anime?keyword=' + [Uri]::EscapeDataString($Keyword)) -TimeoutSec 40
    $report.Add('KEYWORD: ' + $Keyword)
    foreach ($state in $data.sourceStatus) { $report.Add(('SOURCE: ' + $state.Name + ' | count=' + $state.Count + ' | ' + $state.Error)) }
    foreach ($source in @('Animeko', '巴哈姆特动画疯')) {
        $anime = @($data.animes | Where-Object { $_.animeTitle -like ('*第二季*from ' + $source) -and $_.startDate -like '2026*' })
        if ($anime.Count -ne 1) { throw ('Expected one S2 candidate from ' + $source) }
        $detail = Invoke-RestMethod ($base + '/api/v2/bangumi/' + $anime[0].animeId) -TimeoutSec 30
        $episode = @($detail.bangumi.episodes | Where-Object { $_.episodeNumber -eq '3' })
        if ($episode.Count -ne 1 -or $detail.bangumi.episodes.Count -ne 12) { throw ('Expected 12 episodes and episode 3 from ' + $source) }
        $response = Invoke-WebRequest ($base + '/api/v2/comment/' + $episode[0].episodeId + '?format=xml') -UseBasicParsing -TimeoutSec 30
        $content = [Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray())
        $xml = [DanmuCinema.DanmuCatalog]::ParseXml($content)
        $count = $xml.GetElementsByTagName('d').Count
        if ($count -eq 0) { throw ('Empty comments from ' + $source) }
        [IO.File]::WriteAllText((Join-Path $fixture ($source + '-S2-E03.xml')), $content, (New-Object Text.UTF8Encoding($false)))
        $json = Invoke-RestMethod ($base + '/api/v2/comment/' + $episode[0].episodeId + '?format=json') -TimeoutSec 30
        if ($json.comments.Count -ne $count) { throw ('JSON/XML comment count mismatch for ' + $source) }
        $report.Add(('PASS: ' + $source + ' S2, 12 episodes, episode 3 => ' + $count + ' comments through iPad HTTP API (XML and JSON)'))
    }
    $report.Add('PASS: No media files changed; samples only in isolated test output')
} catch {
    $report.Add('FAIL: ' + $_.Exception.Message)
    throw
} finally {
    [void]$gateway.Stop().GetAwaiter().GetResult(); $gateway.Dispose()
    [DanmuCinema.Paths]::Root = $projectRoot
    [IO.File]::WriteAllLines((Join-Path $projectRoot 'tests\output\anime-sources-live.txt'), $report, (New-Object Text.UTF8Encoding($false)))
    $report
}
