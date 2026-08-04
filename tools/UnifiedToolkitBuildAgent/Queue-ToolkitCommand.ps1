[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Name,

    [Parameter(Mandatory = $true)]
    [string[]]$Arguments,

    [string]$RepositoryRoot =
        "C:\Users\Evan\Documents\GitHub\xwing-unified-1e",

    [int]$TimeoutMinutes = 30
)

$ErrorActionPreference = "Stop"

$queuePath = Join-Path `
    $RepositoryRoot `
    "_unifiedtoolkit_agent\commands.json"

$queueDirectory = Split-Path -Parent $queuePath
New-Item `
    -ItemType Directory `
    -Force `
    -Path $queueDirectory | Out-Null

if (Test-Path $queuePath) {
    $queue = Get-Content $queuePath -Raw | ConvertFrom-Json
}
else {
    $queue = [pscustomobject]@{
        schemaVersion = "1.0.0"
        commands = @()
    }
}

$command = [pscustomobject]@{
    id = [Guid]::NewGuid().ToString("N")
    name = $Name
    arguments = $Arguments
    timeoutMinutes = $TimeoutMinutes
    createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
    completed = $false
    success = $false
}

$commands = @($queue.commands)
$commands += $command
$queue.commands = $commands

$temporaryPath = "$queuePath.tmp"

$queue |
    ConvertTo-Json -Depth 20 |
    Set-Content -Path $temporaryPath -Encoding UTF8

Move-Item `
    -Path $temporaryPath `
    -Destination $queuePath `
    -Force

Write-Host "Queued command:"
Write-Host "  ID:   $($command.id)"
Write-Host "  Name: $Name"
Write-Host "  File: $queuePath"
