param([ValidateRange(1024,65535)][int]$MediaPort=8096,[ValidateRange(1024,65535)][int]$DanmuPort=9321)
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw '请通过 GUI 的局域网设置按钮运行，此操作需要 Windows 管理员权限。' }
$rules = @(@{ Name='DanmuCinema-Media'; Port=$MediaPort },@{ Name='DanmuCinema-Danmu'; Port=$DanmuPort })
foreach ($rule in $rules) {
    $existing = Get-NetFirewallRule -Name $rule.Name -ErrorAction SilentlyContinue
    if ($existing) { $existing | Remove-NetFirewallRule }
    New-NetFirewallRule -Name $rule.Name -DisplayName $rule.Name -Group 'DanmuCinema' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $rule.Port -Profile Private -RemoteAddress LocalSubnet | Out-Null
}
Write-Output '已允许专用网络同一子网访问视频和弹幕端口。'
