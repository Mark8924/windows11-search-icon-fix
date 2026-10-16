[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:LOCALAPPDATA 'SearchIconStateFix'
$exe = Join-Path $dir 'SearchIconStateFix.exe'
if (Test-Path $exe) { & $exe --stop }
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $key -Name SearchIconStateFix -ErrorAction SilentlyContinue
$task = 'SearchIconStateFix-' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false
Write-Host 'Stopped; startup removed. Cache and logs are retained in:' $dir
Write-Host 'Delete this folder after signing out or restarting Explorer to remove all cached files.'
