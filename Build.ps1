[CmdletBinding()]
param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$sourceRoot = $PSScriptRoot
if (-not $OutputPath) { $OutputPath = Join-Path $sourceRoot 'build\SearchIconStateFix.exe' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force (Split-Path $OutputPath -Parent) | Out-Null
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,WindowsBase,UIAutomationClient,UIAutomationTypes
$refs = @('System.dll','System.Core.dll',
    [Windows.Forms.Application].Assembly.Location,
    [Drawing.Bitmap].Assembly.Location,
    [Windows.Rect].Assembly.Location,
    [Windows.Automation.AutomationElement].Assembly.Location,
    [Windows.Automation.TogglePatternIdentifiers].Assembly.Location)
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$arguments = @('/noconfig','/nologo','/target:winexe','/platform:anycpu','/optimize+',('/out:' + $OutputPath))
foreach ($ref in $refs) { $arguments += '/reference:' + $ref }
foreach ($arch in @('arm64','x64')) {
    foreach ($name in @('AnimationBridgeHost.exe','SearchAnimationBridge.dll')) {
        $resource = 'Native.' + $arch + '.' + $name + '.gz'
        $payload = Join-Path $sourceRoot $resource
        if (-not (Test-Path $payload)) { throw "Missing $resource. Run BuildNative.py first." }
        $arguments += '/resource:' + $payload + ',' + $resource
    }
}
$arguments += '/win32icon:' + (Join-Path $sourceRoot 'AppIcon.ico')
$arguments += '/resource:' + (Join-Path $sourceRoot 'AppIcon.ico') + ',AppIcon.ico'
$arguments += Join-Path $sourceRoot 'SearchIconStateFix.cs'
& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "C# compiler exited with $LASTEXITCODE" }
Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256
