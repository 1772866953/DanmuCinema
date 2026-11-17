param()
$ErrorActionPreference='Stop'
$taskProject=Split-Path -Parent $PSScriptRoot
$taskArchive=Join-Path $taskProject 'development\dependencies\mpv-x86_64-20261105-git-3138fc8eea.7z'
$taskMpv=Join-Path $taskProject 'runtime\mpv'
if(!(Test-Path -LiteralPath $taskArchive)){throw '缺少固定版本的 mpv 压缩包，请按 player/runtime.json 下载后重新构建。'}
if((Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash -ne '89aa56f9769f81f783576183212e7b4802c91909ecf860b4468113cbfd106d93'){throw 'mpv archive hash mismatch.'}
New-Item -ItemType Directory -Path $taskMpv -Force | Out-Null
$taskExecutable=Join-Path $taskMpv 'mpv.exe'
$taskIcon=Join-Path $taskProject 'public\icon.ico'
$taskManifest=Join-Path $taskMpv 'build.json'
$taskPrevious=$null
if(Test-Path -LiteralPath $taskManifest){try{$taskPrevious=Get-Content -LiteralPath $taskManifest -Raw | ConvertFrom-Json}catch{}}
$taskOriginalHash='63DE44712D633A59B50A6EF19F91A6F51CEFA6079DB59C69BBD31BAC03C01060'
$taskIconHash=(Get-FileHash -LiteralPath $taskIcon -Algorithm SHA256).Hash
$taskCurrentHash=if(Test-Path -LiteralPath $taskExecutable){(Get-FileHash -LiteralPath $taskExecutable -Algorithm SHA256).Hash}else{''}
$taskBranded=$taskPrevious -and $taskPrevious.upstream -eq $taskOriginalHash -and $taskPrevious.icon -eq $taskIconHash -and $taskPrevious.executable -eq $taskCurrentHash
if(!$taskBranded -and $taskCurrentHash -ne $taskOriginalHash){
 & tar -xf $taskArchive -C $taskMpv mpv.exe
 if($LASTEXITCODE){throw 'mpv extraction failed.'}
}
if(!$taskBranded){
 if((Get-FileHash -LiteralPath $taskExecutable -Algorithm SHA256).Hash -ne $taskOriginalHash){throw 'mpv binary hash mismatch.'}
 $taskRcedit=Join-Path $taskProject 'node_modules\electron-winstaller\vendor\rcedit.exe'
 & $taskRcedit $taskExecutable --set-icon $taskIcon --set-version-string ProductName DanmuCinema --set-version-string FileDescription 'DanmuCinema Local Player'
 if($LASTEXITCODE){throw 'Native player icon update failed.'}
 $taskRecord=@{upstream=$taskOriginalHash;icon=$taskIconHash;executable=(Get-FileHash -LiteralPath $taskExecutable -Algorithm SHA256).Hash;changes='Windows icon and product description only'} | ConvertTo-Json
 [IO.File]::WriteAllText($taskManifest,$taskRecord,[Text.UTF8Encoding]::new($false))
}
New-Item -ItemType Directory -Path (Join-Path $taskMpv 'fonts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskProject 'public\fonts\noto-sans-sc.ttf') -Destination (Join-Path $taskMpv 'fonts\noto-sans-sc.ttf') -Force
Copy-Item -LiteralPath (Join-Path $taskProject 'player\modernz\fonts\modernz-icons.ttf') -Destination (Join-Path $taskMpv 'fonts\modernz-icons.ttf') -Force
foreach($taskWeight in 100,200,300,400,500,600,700,800,900){
 $taskFace=Join-Path $taskProject ("player\ui-fonts\cinema-ui-$taskWeight.ttf")
 if(!(Test-Path -LiteralPath $taskFace)){throw '缺少播放器静态字重字体，请先运行 scripts/build-player-fonts.py。'}
 Copy-Item -LiteralPath $taskFace -Destination (Join-Path $taskMpv ('fonts\'+[IO.Path]::GetFileName($taskFace))) -Force
}
Write-Output 'Pinned mpv runtime and local fonts verified.'
