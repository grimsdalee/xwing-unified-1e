[CmdletBinding()]
param(
    [switch]$RemovePublishedFiles
)

$ErrorActionPreference = "Stop"

$taskName = "UnifiedToolkit Build Agent"

Write-Host "Removing scheduled task: $taskName"

$task = Get-ScheduledTask `
    -TaskName $taskName `
    -ErrorAction SilentlyContinue

if ($null -ne $task) {
    Stop-ScheduledTask `
        -TaskName $taskName `
        -ErrorAction SilentlyContinue

    Unregister-ScheduledTask `
        -TaskName $taskName `
        -Confirm:$false
}

if ($RemovePublishedFiles) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $publishRoot = Join-Path $scriptRoot "publish"

    if (Test-Path $publishRoot) {
        Remove-Item `
            -Path $publishRoot `
            -Recurse `
            -Force
    }
}

Write-Host "Build Agent removed."
