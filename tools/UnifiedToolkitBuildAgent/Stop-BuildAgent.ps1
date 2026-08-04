$ErrorActionPreference = "Stop"

$taskName = "UnifiedToolkit Build Agent"
Stop-ScheduledTask -TaskName $taskName
Write-Host "Stopped: $taskName"
