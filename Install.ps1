[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'build\SearchIconStateFix.exe'
if (-not (Test-Path $exe)) { & (Join-Path $PSScriptRoot 'Build.ps1') -OutputPath $exe }
Start-Process -FilePath $exe
