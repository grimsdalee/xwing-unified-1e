[CmdletBinding()]
param(
    [string]$RepositoryRoot = "C:\Users\Evan\Documents\GitHub\xwing-unified-1e",
    [switch]$StartNow
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $scriptRoot "UnifiedToolkitBuildAgent.csproj"
$configSource = Join-Path $scriptRoot "agentsettings.json"

$installRoot = Join-Path $RepositoryRoot "tools\UnifiedToolkitBuildAgent"
$publishRoot = Join-Path $installRoot "publish"
$configTarget = Join-Path $installRoot "agentsettings.json"
$taskName = "UnifiedToolkit Build Agent"

Write-Host "UnifiedToolkit Build Agent Installer"
Write-Host "===================================="
Write-Host "Repository: $RepositoryRoot"
Write-Host "Install:    $installRoot"
Write-Host

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK was not found on PATH. Install .NET 8 SDK first."
}

$dotnetVersion = & dotnet --version
Write-Host ".NET SDK:   $dotnetVersion"

New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

Write-Host
Write-Host "Publishing agent..."
& dotnet publish $projectFile `
    --configuration Release `
    --output $publishRoot `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$config = Get-Content $configSource -Raw | ConvertFrom-Json
$config.repositoryRoot = $RepositoryRoot
$config | ConvertTo-Json -Depth 20 |
    Set-Content -Path $configTarget -Encoding UTF8

$exe = Join-Path $publishRoot "UnifiedToolkitBuildAgent.exe"
if (-not (Test-Path $exe)) {
    throw "Published executable was not found: $exe"
}

$arguments = "--config `"$configTarget`""
$action = New-ScheduledTaskAction `
    -Execute $exe `
    -Argument $arguments `
    -WorkingDirectory $installRoot

$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1)

$principal = New-ScheduledTaskPrincipal `
    -UserId "$env:USERDOMAIN\$env:USERNAME" `
    -LogonType Interactive `
    -RunLevel Limited

$task = New-ScheduledTask `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal `
    -Description "Watches UnifiedToolkit source files and automatically runs dotnet builds."

Register-ScheduledTask `
    -TaskName $taskName `
    -InputObject $task `
    -Force | Out-Null

Write-Host
Write-Host "Scheduled task installed: $taskName"
Write-Host "Configuration:            $configTarget"
Write-Host "Executable:               $exe"

if ($StartNow) {
    Start-ScheduledTask -TaskName $taskName
    Write-Host "Agent started."
}
else {
    Write-Host
    Write-Host "Run this to start it now:"
    Write-Host "  Start-ScheduledTask -TaskName `"$taskName`""
}

Write-Host
Write-Host "Latest status:"
Write-Host "  $RepositoryRoot\_unifiedtoolkit_reports\build-agent\build-agent-status.json"
Write-Host
Write-Host "Latest build result:"
Write-Host "  $RepositoryRoot\_unifiedtoolkit_reports\build-agent\build-agent-latest.json"
