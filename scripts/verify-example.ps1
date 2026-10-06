$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
[void][Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'bin\DanmuCinema.exe'))
[DanmuCinema.Paths]::Root = $projectRoot
$settings = [DanmuCinema.SettingsStore]::Load()
$api = New-Object DanmuCinema.JellyfinApi($settings)
$report = New-Object 'Collections.Generic.List[string]'
try {
    $items = $api.Items('骸骨骑士').GetAwaiter().GetResult()
    if ($items.Length -ne 12) { throw ('Expected 12 local episodes, got ' + $items.Length) }
    $report.Add('PASS: 中文本地搜索返回骸骨骑士 S2 全部 12 集')
    foreach ($term in @('骸骨骑士', '骸骨骑士大人冒险中S2', '骸骨骑士 第二季', '骸骨骑士大人奇幻世界冒险中 第二季')) {
        $content = $api.Request('GET', ('api/danmu/search?keyword=' + [Uri]::EscapeDataString($term)), $null, $true).GetAwaiter().GetResult()
        $sources = [DanmuCinema.Json]::Array([DanmuCinema.Json]::Object('{"Items":' + $content + '}'), 'Items')
        $report.Add(('SEARCH: ' + $term + ' => ' + $sources.Length + ' candidates'))
        foreach ($source in $sources) {
            if ([DanmuCinema.Json]::Text($source, 'Name') -like '*骸骨*') {
                $report.Add(('CANDIDATE: ' + [DanmuCinema.Json]::Text($source, 'Name') + ' | ' + [DanmuCinema.Json]::Text($source, 'Year') + ' | ' + [DanmuCinema.Json]::Text($source, 'SiteId')))
            }
        }
    }
    $content = $api.Request('GET', 'api/youku/danmu/acefcce9d51e4a91aaae/episodes', $null, $true).GetAwaiter().GetResult()
    $episodes = [DanmuCinema.Json]::Array([DanmuCinema.Json]::Object('{"Items":' + $content + '}'), 'Items')
    if ($episodes.Length -ne 12) { throw 'Episode API mismatch' }
    $cid = [DanmuCinema.MediaNames]::CommentId($episodes[2])
    if ([string]::IsNullOrWhiteSpace($cid)) { throw 'Missing comment ID' }
    $xmlText = $api.Request('GET', ('api/youku/danmu/' + [Uri]::EscapeDataString($cid) + '/download'), $null, $true).GetAwaiter().GetResult()
    $xml = New-Object Xml.XmlDocument
    $xml.XmlResolver = $null
    $xml.LoadXml($xmlText)
    $count = $xml.GetElementsByTagName('d').Count
    if ($count -eq 0) { throw 'Upstream returned no comments' }
    $report.Add(('PASS: 2022 年候选读取 12 集，并下载第 3 集 ' + $count + ' 条弹幕，仅验证接口，不关联 S2'))
} finally {
    $api.Dispose()
    [IO.File]::WriteAllLines((Join-Path $projectRoot 'tests\output\example-verification.txt'), $report, (New-Object Text.UTF8Encoding($false)))
    $report
}
