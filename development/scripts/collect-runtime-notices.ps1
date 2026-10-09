$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'workspace.ps1')
$output = Join-Path $projectRoot 'downloads\installer-sources\runtime-notices'
New-Item -ItemType Directory -Path $output -Force | Out-Null
& (Join-Path $PSScriptRoot 'prepare-runtime-references.ps1')
$packages = (Get-Content -LiteralPath (Join-Path $runtimeReferenceRoot 'jellyfin.deps.json') -Raw | ConvertFrom-Json).libraries.PSObject.Properties |
    Where-Object { $_.Value.type -eq 'package' -and $_.Name -notmatch '^(Microsoft|System|runtime|NETStandard)' }
$records = $packages | ForEach-Object -Parallel {
    $package = $_; $parts = $package.Name.Split('/'); $id=$parts[0].ToLowerInvariant(); $version=$parts[1].ToLowerInvariant()
    $directory = Join-Path $using:output ($id+'-'+$version)
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $archive = Join-Path $directory 'package.nupkg'
    try {
        if (!(Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" -OutFile $archive -TimeoutSec 45 }
        $hash=[Convert]::ToBase64String([Security.Cryptography.SHA512]::Create().ComputeHash([IO.File]::ReadAllBytes($archive)))
        # These packages supply attribution metadata only, never replacement
        # binaries. Record both hashes; the shipped runtime remains the verified
        # official Jellyfin archive rather than a reconstructed NuGet runtime.
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $xml = $null
            foreach ($entry in $zip.Entries) {
                if ($entry.FullName -match '(?i)(license|copying|notice|copyright)|\.nuspec$') {
                    $safeName = $entry.FullName.Replace('/','_').Replace('\','_')
                    if ($entry.Name) { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $directory $safeName), $true) }
                    if ($entry.FullName.EndsWith('.nuspec')) {
                        $reader = New-Object IO.StreamReader($entry.Open()); try { $xml=[xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
                    }
                }
            }
            [ordered]@{package=$package.Name;license=[string]$xml.package.metadata.license.InnerText;licenseUrl=[string]$xml.package.metadata.licenseUrl;copyright=[string]$xml.package.metadata.copyright;authors=[string]$xml.package.metadata.authors;repository=[string]$xml.package.metadata.repository.url;commit=[string]$xml.package.metadata.repository.commit;runtimeContentHash=$package.Value.sha512;metadataArchiveSha512=$hash;ok=$true}
        } finally { $zip.Dispose() }
    } catch { [ordered]@{package=$package.Name;ok=$false;error=$_.Exception.Message} }
} -ThrottleLimit 6
$records | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'packages.json') -Encoding UTF8
$failed=@($records | Where-Object { !$_.ok })
Write-Output "NuGet 许可材料：$(@($records | Where-Object ok).Count)；失败：$($failed.Count)"
if($failed.Count) { $failed | ForEach-Object { $_.package + ': ' + $_.error }; throw '尚有运行组件许可材料未收集。' }
