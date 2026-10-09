# Shared paths. The application and installer keep independent runtime roots.
$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = if ((Split-Path $projectRoot -Leaf) -eq 'development') { Split-Path -Parent $projectRoot } else { $projectRoot }
$buildRoot = Join-Path $projectRoot 'bin\installer-app'
$runtimeReferenceRoot = Join-Path $projectRoot 'bin\runtime-references\jellyfin'
$packagesRoot = Join-Path $workspaceRoot 'packages'
