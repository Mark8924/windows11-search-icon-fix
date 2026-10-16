[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$taskName = 'SearchIconStateFix-' + $identity.User.Value
$dir = Join-Path $env:LOCALAPPDATA 'SearchIconStateFix'
$exe = Join-Path $dir 'SearchIconStateFix.exe'
if (Test-Path -LiteralPath $exe) {
    & $exe --stop
    Get-Process -Name SearchIconStateFix -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq [Diagnostics.Process]::GetCurrentProcess().SessionId } |
        Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}
$task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($task) {
    if ($task.State -eq 'Running') { Stop-ScheduledTask -TaskName $taskName }
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}
Write-Output 'Stopped and removed the current-user logon task.'
Write-Output "Files and logs were retained in $dir; you may delete that folder when no longer needed."
