$ErrorActionPreference = "Stop"

$taskName = "UnifiedToolkit Build Agent"
Start-ScheduledTask -TaskName $taskName
Write-Host "Started: $taskName"
