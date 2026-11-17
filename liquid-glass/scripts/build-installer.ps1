param([string]$Version = '2.2.6', [switch]$SkipBuild, [switch]$TestBuild, [switch]$PrepareOnly, [string]$StageRoot='')
$ErrorActionPreference = 'Stop'
$taskProject = Split-Path -Parent $PSScriptRoot
$taskWorkspace = Split-Path -Parent $taskProject
. (Join-Path $taskProject 'development\scripts\workspace.ps1')
$taskPackages = Join-Path (Join-Path $taskWorkspace 'packages') $Version
$taskRedistributionSources = Join-Path $taskProject 'development\dependencies\redistribution-sources.zip'
$taskNode = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
$taskPython = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
if (!(Test-Path -LiteralPath $taskNode)) { $taskNode = (Get-Command node -ErrorAction Stop).Source }
if (!(Test-Path -LiteralPath $taskPython)) { $taskPython = (Get-Command python -ErrorAction Stop).Source }
$taskIscc = if ($env:DANMU_ISCC) { $env:DANMU_ISCC } else { Join-Path $dependencyRoot 'bin\installer-tools\inno\ISCC.exe' }
if (!(Test-Path -LiteralPath $taskIscc)) { $taskIscc = (Get-Command ISCC -ErrorAction Stop).Source }
$taskStagingOutput=Join-Path ([IO.Path]::GetTempPath()) ('DanmuCinema-package-'+$Version)
$taskStage = if($StageRoot){[IO.Path]::GetFullPath($StageRoot)}else{Join-Path $taskStagingOutput 'win-unpacked'}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid package version.' }
Push-Location $taskProject
try {
 if (!$SkipBuild) {
  & (Join-Path $PSScriptRoot 'build.ps1')
  if ($LASTEXITCODE) { throw 'Application build failed.' }
  & $taskNode node_modules/electron-builder/cli.js --win --dir --x64 --publish never "--config.directories.output=$taskStagingOutput"
  if ($LASTEXITCODE) { throw 'Electron package failed.' }
 }
 & $taskPython -X utf8 (Join-Path $PSScriptRoot 'verify-ass-sources.py')
 if($LASTEXITCODE){throw 'ASS corresponding source or license verification failed.'}
 $taskRcedit = Join-Path $taskProject 'node_modules\electron-winstaller\vendor\rcedit.exe'
 & $taskRcedit (Join-Path $taskStage 'DanmuCinema.exe') --set-icon (Join-Path $taskProject 'public\icon.ico') --set-version-string ProductName DanmuCinema --set-version-string FileDescription DanmuCinema --set-file-version $Version --set-product-version $Version
 if ($LASTEXITCODE) { throw 'Application icon/metadata failed.' }
 $taskLicenseRoot = Join-Path $taskStage 'licenses'
 New-Item -ItemType Directory -Path $taskLicenseRoot -Force | Out-Null
 Add-Type -AssemblyName System.IO.Compression
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $taskPrevious = [IO.Compression.ZipFile]::OpenRead($taskRedistributionSources)
 try {
  foreach ($taskEntry in $taskPrevious.Entries) {
   if ($taskEntry.FullName.StartsWith('sources/') -or !$taskEntry.Name.StartsWith('LICENSE')) { continue }
   $taskTarget = [IO.Path]::GetFullPath((Join-Path $taskLicenseRoot $taskEntry.FullName))
   if (!$taskTarget.StartsWith($taskLicenseRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid license archive path.' }
   New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($taskTarget)) -Force | Out-Null
   [IO.Compression.ZipFileExtensions]::ExtractToFile($taskEntry,$taskTarget,$true)
  }
 } finally { $taskPrevious.Dispose() }
 Copy-Item -Path (Join-Path $taskProject 'licenses\*') -Destination $taskLicenseRoot -Force
 $taskSourceZip = Join-Path (Split-Path -Parent $taskStage) 'DanmuCinema-source.zip'
 Add-Type -AssemblyName System.IO.Compression
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $taskSourceStream = [IO.File]::Open($taskSourceZip, [IO.FileMode]::Create)
 $taskSourceArchive = [IO.Compression.ZipArchive]::new($taskSourceStream, [IO.Compression.ZipArchiveMode]::Create)
 try {
  foreach ($taskDirectory in @('src','electron','player','player-overlay','public','installer','scripts','tests','development\src','development\engine','development\plugin','development\scripts','development\assets','development\tests')) {
   foreach ($taskFile in Get-ChildItem -LiteralPath (Join-Path $taskProject $taskDirectory) -Recurse -File) {
    if ($taskFile.Extension -in @('.md','.txt') -and $taskFile.Name -notlike 'LICENSE*') { continue }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskSourceArchive,$taskFile.FullName,$taskFile.FullName.Substring($taskProject.Length+1).Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
   }
  }
  foreach ($taskName in @('package.json','package-lock.json','vite.config.ts','tsconfig.json','index.html')) {
   $taskFile = Join-Path $taskProject $taskName
   if (Test-Path -LiteralPath $taskFile) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskSourceArchive,$taskFile,$taskName,[IO.Compression.CompressionLevel]::Optimal) | Out-Null }
  }
  [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskSourceArchive,(Join-Path $taskWorkspace 'LICENSE'),'LICENSE',[IO.Compression.CompressionLevel]::Optimal) | Out-Null
 } finally { $taskSourceArchive.Dispose(); $taskSourceStream.Dispose() }
 & $taskPython (Join-Path $taskProject 'development\scripts\verify-public-package.py') $taskStage $taskSourceZip
 if ($LASTEXITCODE) { throw 'Public package privacy check failed.' }
 if (Get-ChildItem -LiteralPath $taskLicenseRoot -Recurse -File | Where-Object Name -notlike 'LICENSE*') { throw 'License folder contains a non-license document.' }
 if ($TestBuild) { $taskOutput = Join-Path $taskProject 'artifacts\installer\compiler'; New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null }
 else {
  $taskOutput = $taskPackages
  New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
  $taskCorresponding = Join-Path $taskPackages "DanmuCinema-Corresponding-Sources-$Version.zip"
  $taskOriginal = [IO.Compression.ZipFile]::OpenRead($taskRedistributionSources)
  $taskStream = [IO.File]::Open($taskCorresponding,[IO.FileMode]::Create)
  $taskArchive = [IO.Compression.ZipArchive]::new($taskStream,[IO.Compression.ZipArchiveMode]::Create)
  try {
   foreach ($taskEntry in $taskOriginal.Entries) {
    if (!$taskEntry.FullName.StartsWith('sources/') -or $taskEntry.FullName -eq 'sources/DanmuCinema-source.zip') { continue }
    $taskNew = $taskArchive.CreateEntry($taskEntry.FullName,[IO.Compression.CompressionLevel]::NoCompression)
    $taskInput = $taskEntry.Open(); $taskTarget = $taskNew.Open()
    try { $taskInput.CopyTo($taskTarget) } finally { $taskInput.Dispose(); $taskTarget.Dispose() }
   }
   $taskMpvSourceRoot=Join-Path $taskProject 'development\dependencies\mpv-sources'
   foreach($taskMpvSource in Get-ChildItem -LiteralPath $taskMpvSourceRoot -File | Where-Object { $_.Name -like '*.tar.gz' -or $_.Name -eq 'manifest.json' }) {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive,$taskMpvSource.FullName,('sources/mpv/'+$taskMpvSource.Name),[IO.Compression.CompressionLevel]::NoCompression) | Out-Null
   }
   $taskAssSourceRoot=Join-Path $taskProject 'development\dependencies\jassub-sources'
   $taskAssManifest=Get-Content -LiteralPath (Join-Path $taskAssSourceRoot 'manifest.json') -Raw | ConvertFrom-Json
   $taskAssFiles=@($taskAssManifest.sources | ForEach-Object {$_.file})+@('javascript-dependencies.zip','manifest.json')
   foreach($taskAssFile in $taskAssFiles){
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive,(Join-Path $taskAssSourceRoot $taskAssFile),('sources/jassub/'+$taskAssFile),[IO.Compression.CompressionLevel]::NoCompression) | Out-Null
   }
   [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive,$taskSourceZip,'sources/DanmuCinema-source.zip',[IO.Compression.CompressionLevel]::NoCompression) | Out-Null
   foreach ($taskFile in Get-ChildItem -LiteralPath $taskLicenseRoot -Recurse -File) {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive,$taskFile.FullName,$taskFile.FullName.Substring($taskLicenseRoot.Length+1).Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
   }
  } finally { $taskArchive.Dispose(); $taskStream.Dispose(); $taskOriginal.Dispose() }
 }
 # Use a fresh temporary directory so shell previews cannot retain a handle to
 # an earlier EXE while Inno updates its resources and checksum.
 $taskCompileOutput = if($TestBuild){$taskOutput}else{Join-Path ([IO.Path]::GetTempPath()) ("DanmuCinema-compile-$Version-"+[Guid]::NewGuid().ToString("N"))}
 if(!$PrepareOnly){New-Item -ItemType Directory -Path $taskCompileOutput -Force | Out-Null}
 $taskArguments = @('/Q', "/DPackageRoot=$taskStage", "/DOutputFolder=$taskCompileOutput", "/DPackageVersion=$Version")
 if ($TestBuild) { $taskArguments += '/DTestBuild' }
 if (!$PrepareOnly) {
  & $taskIscc @taskArguments (Join-Path $taskProject 'installer\DanmuCinema.iss')
  if ($LASTEXITCODE) { throw 'Inno Setup build failed.' }
  if(!$TestBuild){
   $taskCompiled=Join-Path $taskCompileOutput "DanmuCinema-Setup-$Version-win-x64.exe"
   $taskPublished=Join-Path $taskPackages ([IO.Path]::GetFileName($taskCompiled))
   $taskPending=$taskPublished+'.pending'
   Copy-Item -LiteralPath $taskCompiled -Destination $taskPending -Force
   for($taskAttempt=0;;$taskAttempt++){
    try{Move-Item -LiteralPath $taskPending -Destination $taskPublished -Force -ErrorAction Stop;break}
    catch{if($taskAttempt -ge 30){throw};Start-Sleep -Seconds 1}
   }
  }
 }
 if (!$TestBuild) {
  $taskHashFiles = @($taskCorresponding)
  if (!$PrepareOnly) { $taskHashFiles += Join-Path $taskPackages "DanmuCinema-Setup-$Version-win-x64.exe" }
  foreach ($taskFile in $taskHashFiles) {
   [IO.File]::WriteAllText($taskFile+'.sha256',(Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash+'  '+[IO.Path]::GetFileName($taskFile)+"`r`n",[Text.UTF8Encoding]::new($false))
  }
 }
 Write-Output "Installer output: $taskOutput"
} finally { Pop-Location }
