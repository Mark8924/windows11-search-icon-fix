[CmdletBinding()]
param([string]$OutputPath = (Join-Path $PSScriptRoot 'build\SearchIconStateFix.exe'))
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -lt 5) {
    throw 'Use Windows PowerShell 5.1 (powershell.exe), not PowerShell 7 (pwsh.exe).'
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force (Split-Path $OutputPath -Parent) | Out-Null
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,WindowsBase,UIAutomationClient,UIAutomationTypes
$refs = @('System.dll','System.Core.dll',
    [Windows.Forms.Application].Assembly.Location,
    [Drawing.Bitmap].Assembly.Location,
    [Windows.Rect].Assembly.Location,
    [Windows.Automation.AutomationElement].Assembly.Location,
    [Windows.Automation.TogglePatternIdentifiers].Assembly.Location)
if (Test-Path -LiteralPath $OutputPath) { Remove-Item -LiteralPath $OutputPath }
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'SearchIconStateFix.cs'))) -Language CSharp -ReferencedAssemblies $refs -OutputAssembly $OutputPath -OutputType WindowsApplication
Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256
