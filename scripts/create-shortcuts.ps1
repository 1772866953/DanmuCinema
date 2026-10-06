$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$iconPath = Join-Path $projectRoot 'assets\DanmuCinema.ico'
if (!(Test-Path -LiteralPath $iconPath)) { throw 'Application icon not found.' }
$shell = New-Object -ComObject WScript.Shell
try {
    foreach ($name in @('DanmuCinema.lnk', '启动弹幕影院.lnk')) {
        $shortcut = $shell.CreateShortcut((Join-Path $projectRoot $name))
        # The original script builds the executable if it does not exist yet.
        $shortcut.TargetPath = Join-Path $projectRoot 'Start.cmd'
        $shortcut.Arguments = ''
        $shortcut.WorkingDirectory = $projectRoot
        $shortcut.IconLocation = $iconPath + ',0'
        $shortcut.Description = 'DanmuCinema - LAN video and anime danmaku'
        $shortcut.WindowStyle = 7
        $shortcut.Save()
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) | Out-Null
    }
} finally {
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
}
Write-Output 'Created root launch shortcuts with the DanmuCinema icon.'
