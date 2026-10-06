param([Parameter(Mandatory=$true)][string]$Path, [switch]$Apply)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$file = Get-Item -LiteralPath $Path
if ($file.PSIsContainer -or $file.Extension -ne '.xml') { throw 'Choose one existing XML file.' }
$assemblyPath = Join-Path $projectRoot 'bin\DanmuCinema.exe'
if (!(Test-Path -LiteralPath $assemblyPath)) { throw 'Build DanmuCinema first.' }
[Reflection.Assembly]::LoadFrom($assemblyPath) | Out-Null
$original = [IO.File]::ReadAllText($file.FullName)
$sorted = [DanmuCinema.DanmuCatalog]::SortXmlForPlayback($original)
if ($original -ceq $sorted) { Write-Output 'Already sorted; file unchanged.'; exit 0 }
if (!$Apply) { Write-Output 'Out-of-order timestamps found. Use -Apply to overwrite the XML with sorted comments.'; exit 0 }
$partialPath = $file.FullName + '.' + [Guid]::NewGuid().ToString('N') + '.partial'
try {
    [IO.File]::WriteAllText($partialPath, $sorted, (New-Object Text.UTF8Encoding($false)))
    # Stop if another process edited the XML while this script was preparing it.
    if ([IO.File]::ReadAllText($file.FullName) -cne $original) { throw 'XML changed during preparation; no replacement was performed.' }
    [IO.File]::Replace($partialPath, $file.FullName, [NullString]::Value)
    Write-Output ('Sorted XML: ' + $file.FullName)
} finally {
    if (Test-Path -LiteralPath $partialPath) { Remove-Item -LiteralPath $partialPath -Force }
}
