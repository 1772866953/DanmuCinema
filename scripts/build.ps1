param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows .NET Framework 4.x。' }
New-Item -ItemType Directory -Path (Join-Path $projectRoot 'bin') -Force | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName
$outputName = if ($env:DANMU_BUILD_UPDATE -eq '1') { 'DanmuCinema.updated.exe' } else { 'DanmuCinema.exe' }
$output = Join-Path (Join-Path $projectRoot 'bin') $outputName
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /utf8output "/out:$output" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll /reference:System.Web.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.Xml.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $sources
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\DanmuCinema.exe.config') -Destination ($output + '.config') -Force
Write-Output "构建完成：$output"
if ($Test) {
    $testProcess = Start-Process -FilePath $output -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath (Join-Path $projectRoot 'tests\output\self-test.txt') -Encoding UTF8
    if ($testProcess.ExitCode -ne 0) { throw '自检失败。' }
}
