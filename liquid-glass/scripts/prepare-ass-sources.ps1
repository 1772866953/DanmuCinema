param([switch]$VerifyOnly)
$ErrorActionPreference='Stop'
$taskProject=Split-Path -Parent $PSScriptRoot
$taskRoot=Join-Path $taskProject 'development/dependencies/jassub-sources'
$taskPython=Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
New-Item -ItemType Directory -Path $taskRoot -Force | Out-Null
$taskSources=@(
 @{name='jassub';repo='ThaUnknown/jassub';commit='4c854810063f24adab4b5085027f30f776016103'},
 @{name='brotli';repo='google/brotli';commit='028fb5a23661f123017c060daa546b55cf4bde29'},
 @{name='freetype';repo='freetype/freetype';commit='0a0221a1347e2f1e07c395263540026e9a0aa7c7'},
 @{name='fribidi';repo='fribidi/fribidi';commit='68162babff4f39c4e2dc164a5e825af93bda9983'},
 @{name='harfbuzz';repo='harfbuzz/harfbuzz';commit='30485ee8c3d43c553afb9d78b9924cb71c8d2f19'},
 @{name='libass';repo='libass/libass';commit='fbe73bf18bd98e3ba14ce6d5e4bd1ba202a25ef4'}
)
foreach($taskSource in $taskSources){
 $taskFile=Join-Path $taskRoot ($taskSource.name+'.tar.gz')
 if(!(Test-Path -LiteralPath $taskFile)){
  if($VerifyOnly){throw "Missing ASS source: $($taskSource.name)"}
  $taskUri="https://codeload.github.com/$($taskSource.repo)/tar.gz/$($taskSource.commit)"
  Invoke-WebRequest -Uri $taskUri -OutFile ($taskFile+'.pending') -TimeoutSec 60
  Move-Item -LiteralPath ($taskFile+'.pending') -Destination $taskFile
 }
 Write-Output "ASS source available: $($taskSource.name)"
}
$taskFontLicense=Join-Path $taskRoot 'LICENSE-LiberationSans'
if(!(Test-Path -LiteralPath $taskFontLicense)){
 if($VerifyOnly){throw 'Missing Liberation Sans license.'}
 Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/liberationfonts/liberation-fonts/main/LICENSE' -OutFile $taskFontLicense -TimeoutSec 30
}
$taskApacheLicense=Join-Path $taskRoot 'LICENSE-Apache-2.0'
if(!(Test-Path -LiteralPath $taskApacheLicense)){
 if($VerifyOnly){throw 'Missing Apache 2.0 license.'}
 Invoke-WebRequest -Uri 'https://www.apache.org/licenses/LICENSE-2.0.txt' -OutFile $taskApacheLicense -TimeoutSec 30
}
& $taskPython -X utf8 (Join-Path $PSScriptRoot 'verify-ass-sources.py')
if($LASTEXITCODE){throw 'ASS redistribution verification failed.'}
