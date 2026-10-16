[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run with Windows PowerShell 5.1 (powershell.exe).' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$taskName = 'SearchIconStateFix-' + $identity.User.Value
$dir = Join-Path $env:LOCALAPPDATA 'SearchIconStateFix'
$exe = Join-Path $dir 'SearchIconStateFix.exe'
$temporaryExe = Join-Path $env:TEMP ('SearchIconStateFix-' + [guid]::NewGuid().ToString('N') + '.exe')
# Compile first; a compilation error must not stop an existing installation.
try {
    & (Join-Path $PSScriptRoot 'Build.ps1') -OutputPath $temporaryExe | Out-Null
    if (Test-Path -LiteralPath $exe) {
        & $exe --stop
        Get-Process -Name SearchIconStateFix -ErrorAction SilentlyContinue |
            Where-Object { $_.SessionId -eq [Diagnostics.Process]::GetCurrentProcess().SessionId } |
            Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    }
    $existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($existingTask -and $existingTask.State -eq 'Running') { Stop-ScheduledTask -TaskName $taskName }
    New-Item -ItemType Directory -Force $dir | Out-Null
    Copy-Item -LiteralPath $temporaryExe -Destination $exe -Force
    foreach ($name in @('SearchIconStateFix.cs','Build.ps1','Install.ps1','Uninstall.ps1','README.md','LICENSE')) {
        $from = Join-Path $PSScriptRoot $name
        $to = Join-Path $dir $name
        if ([IO.Path]::GetFullPath($from) -ne [IO.Path]::GetFullPath($to)) { Copy-Item -LiteralPath $from -Destination $to -Force }
    }
    $action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $dir
    $principal = New-ScheduledTaskPrincipal -UserId $identity.Name -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity.Name
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Trigger $trigger -Description 'Experimental correction for a stale colored Windows 11 Search icon; current user only.' -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName
    Write-Output "Installed: $exe"
    Write-Output "Logon task: $taskName"
    Write-Output 'Experimental preview: effectiveness against the intermittent bug is not yet fully verified.'
} finally {
    if (Test-Path -LiteralPath $temporaryExe) { Remove-Item -LiteralPath $temporaryExe }
}
