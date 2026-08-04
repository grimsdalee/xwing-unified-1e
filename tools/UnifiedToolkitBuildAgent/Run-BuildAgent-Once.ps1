[CmdletBinding()]
param(
    [string]$Configuration = ""
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $scriptRoot "publish\UnifiedToolkitBuildAgent.exe"

if ([string]::IsNullOrWhiteSpace($Configuration)) {
    $Configuration = Join-Path $scriptRoot "agentsettings.json"
}

if (-not (Test-Path $exe)) {
    throw "Published agent executable not found. Run Install-BuildAgent.ps1 first."
}

& $exe --config $Configuration --run-once
exit $LASTEXITCODE
