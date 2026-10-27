# Shared paths. The application and installer keep independent runtime roots.
$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent (Split-Path -Parent $projectRoot)
$dependencyRoot = if ($env:DANMU_DEPENDENCY_ROOT) {
    [IO.Path]::GetFullPath($env:DANMU_DEPENDENCY_ROOT)
} elseif (Test-Path -LiteralPath (Join-Path $workspaceRoot 'legacy-ui\development')) {
    Join-Path $workspaceRoot 'legacy-ui\development'
} else { $projectRoot }
$downloadsRoot = Join-Path $dependencyRoot 'downloads'
$buildRoot = Join-Path $projectRoot 'bin\installer-app'
$runtimeReferenceRoot = Join-Path $dependencyRoot 'bin\runtime-references\jellyfin'
$packagesRoot = Join-Path $workspaceRoot 'packages\liquid-glass'
