param([switch]$Install)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'workspace.ps1')
if ($PSVersionTable.PSEdition -ne 'Core') {
    $pwshPath = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
    if (!$pwshPath) { $pwshPath = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe' }
    if (!(Test-Path -LiteralPath $pwshPath)) { throw '构建播放准备插件需要 PowerShell 7.6（.NET 10）或更新版本。' }
    & $pwshPath -NoProfile -File $PSCommandPath -Install:$Install
    if ($LASTEXITCODE -ne 0) { throw '播放准备插件编译失败。' }; return
}
if ($Install) { throw '请使用 EXE 安装包安装插件。开发目录只负责构建。' }
& (Join-Path $PSScriptRoot 'prepare-runtime-references.ps1')
$runtimePath = $runtimeReferenceRoot
$outputPath = Join-Path $projectRoot 'bin\playback-plugin'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$assemblyPath = Join-Path $outputPath 'DanmuCinema.Playback.dll'
if (Test-Path -LiteralPath $assemblyPath) { Remove-Item -LiteralPath $assemblyPath }
$references = Get-ChildItem -LiteralPath $runtimePath -Filter '*.dll' | Where-Object { $_.Name -match '^(System\.|Microsoft\.(AspNetCore|Extensions)\.|MediaBrowser\.|Jellyfin\.Data|netstandard)' -and $_.Name -notmatch '\.Native\.dll$' } | ForEach-Object FullName
[Reflection.Assembly]::LoadFrom((Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')) | Out-Null
$syntax = @((Get-ChildItem -LiteralPath (Join-Path $projectRoot 'plugin') -Filter '*.cs' -File | ForEach-Object FullName)) + @(Join-Path $projectRoot 'src\MediaAuto.cs') | ForEach-Object { [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($_)) }
$metadataReferences = [Microsoft.CodeAnalysis.MetadataReference[]]@($references | ForEach-Object { [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_) })
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('DanmuCinema.Playback', [Microsoft.CodeAnalysis.SyntaxTree[]]@($syntax), $metadataReferences, $options)
$stream = [IO.File]::Create($assemblyPath)
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
if (!$result.Success) { $result.Diagnostics | ForEach-Object { Write-Output $_.ToString() }; throw 'Plugin compilation failed' }
$metadata = @{ guid='0389c672-ad21-421d-a386-84e3771f892f'; name='DanmuCinema Playback'; version='1.0.0.0'; targetAbi='12.1.0.0'; status='Active'; autoUpdate=$false; description='Prepare danmu before playback'; assemblies=@('DanmuCinema.Playback.dll') }
$metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'meta.json') -Encoding utf8
Write-Output "播放准备插件构建完成：$assemblyPath"
