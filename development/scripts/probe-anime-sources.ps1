$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'downloads\source-research'
$headers = @{ 'User-Agent' = 'DanmuCinema/1.0 (local personal media controller)' }
$search = '骸骨骑士'
$probes = @(
    @{Name='bangumi-search';Url='https://api.bgm.tv/v0/search/subjects?limit=20';Method='POST';Body=(@{keyword=$search;filter=@{type=@(2)}}|ConvertTo-Json -Depth 4)},
    @{Name='bahamut-search';Url=('https://api.gamer.com.tw/mobile_app/anime/v1/search.php?kw='+[Uri]::EscapeDataString('骸骨騎士'));Method='GET'},
    @{Name='animeko-subject';Url='https://api.animeko.org/v2/subjects/528828';Method='GET'},
    @{Name='bahamut-episodes';Url='https://api.gamer.com.tw/anime/v1/video.php?videoSn=49914';Method='GET'},
    @{Name='bahamut-comments';Url='https://api.gamer.com.tw/anime/v1/danmu.php?geo=TW%2CHK&videoSn=50084';Method='GET'},
    @{Name='animeko-comments';Url='https://api.animeko.org/v1/danmaku/1704902';Method='GET'}
)
foreach($probe in $probes) {
    try {
        $params = @{Uri=$probe.Url;Method=$probe.Method;Headers=$headers;TimeoutSec=15;UseBasicParsing=$true}
        if($probe.Body) { $params.Body=[Text.Encoding]::UTF8.GetBytes($probe.Body);$params.ContentType='application/json' }
        $response=Invoke-WebRequest @params
        $raw = $response.RawContentStream.ToArray()
        [IO.File]::WriteAllText((Join-Path $outDir ($probe.Name+'.json')), [Text.Encoding]::UTF8.GetString($raw), (New-Object Text.UTF8Encoding($false)))
        Write-Output ($probe.Name+': HTTP '+$response.StatusCode+'; bytes='+$response.RawContentLength)
    } catch { Write-Output ($probe.Name+': '+$_.Exception.Message) }
}
