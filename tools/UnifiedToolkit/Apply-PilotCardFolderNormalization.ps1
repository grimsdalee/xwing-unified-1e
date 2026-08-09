[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$RepositoryRoot = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'

function ConvertTo-AssetId {
    param([Parameter(Mandatory = $true)][string]$Value)

    return ([regex]::Replace($Value.ToLowerInvariant(), '[^a-z0-9]', ''))
}

$repositoryRootPath = [System.IO.Path]::GetFullPath($RepositoryRoot)
$pilotCardsRoot = Join-Path $repositoryRootPath 'assets\source\unified1e\pilot-cards'

if (-not (Test-Path -LiteralPath $pilotCardsRoot -PathType Container)) {
    throw "Pilot-card root was not found: $pilotCardsRoot"
}

$gitDirectory = Join-Path $repositoryRootPath '.git'
$useGit = Test-Path -LiteralPath $gitDirectory

$files = Get-ChildItem -LiteralPath $pilotCardsRoot -File -Recurse
$moves = [System.Collections.Generic.List[object]]::new()

foreach ($file in $files) {
    $relativeToCards = [System.IO.Path]::GetRelativePath($pilotCardsRoot, $file.FullName)
    $parts = $relativeToCards -split '[\\/]'

    if ($parts.Count -ne 3) {
        throw "Unexpected pilot-card path depth: $relativeToCards"
    }

    $factionId = ConvertTo-AssetId $parts[0]
    $shipId = ConvertTo-AssetId $parts[1]

    if ([string]::IsNullOrWhiteSpace($factionId) -or [string]::IsNullOrWhiteSpace($shipId)) {
        throw "Could not derive canonical folder IDs from: $relativeToCards"
    }

    $destination = Join-Path $pilotCardsRoot (Join-Path $factionId (Join-Path $shipId $file.Name))

    if ([System.StringComparer]::Ordinal.Equals($file.FullName, $destination)) {
        continue
    }

    $moves.Add([pscustomobject]@{
        Source = $file.FullName
        Destination = $destination
        OldFaction = $parts[0]
        OldShip = $parts[1]
        NewFaction = $factionId
        NewShip = $shipId
    })
}

$collisions = $moves |
    Group-Object -Property Destination |
    Where-Object { $_.Count -gt 1 }

if ($collisions) {
    $paths = ($collisions | ForEach-Object { $_.Name }) -join [Environment]::NewLine
    throw "Two or more source files resolve to the same canonical destination:$([Environment]::NewLine)$paths"
}

foreach ($move in $moves) {
    if (Test-Path -LiteralPath $move.Destination) {
        $sourceHash = (Get-FileHash -LiteralPath $move.Source -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $move.Destination -Algorithm SHA256).Hash

        if ($sourceHash -ne $destinationHash) {
            throw "Destination already exists with different content: $($move.Destination)"
        }

        throw "Destination already exists. Resolve the duplicate before rerunning: $($move.Destination)"
    }
}

$folderMappings = $moves |
    Select-Object OldFaction, OldShip, NewFaction, NewShip -Unique |
    Sort-Object NewFaction, NewShip

Write-Host 'Unified First Edition pilot-card folder normalization'
Write-Host '======================================================='
Write-Host
Write-Host "Repository:       $repositoryRootPath"
Write-Host "Pilot-card files: $($files.Count)"
Write-Host "Files to move:    $($moves.Count)"
Write-Host "Folder mappings:  $($folderMappings.Count)"
Write-Host

foreach ($mapping in $folderMappings) {
    Write-Host ("{0}/{1} -> {2}/{3}" -f `
        $mapping.OldFaction,
        $mapping.OldShip,
        $mapping.NewFaction,
        $mapping.NewShip)
}

foreach ($move in $moves) {
    $destinationDirectory = Split-Path -Parent $move.Destination
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

    $movedWithGit = $false
    if ($useGit) {
        $relativeSource = [System.IO.Path]::GetRelativePath($repositoryRootPath, $move.Source).Replace('\', '/')
        $relativeDestination = [System.IO.Path]::GetRelativePath($repositoryRootPath, $move.Destination).Replace('\', '/')

        & git -C $repositoryRootPath ls-files --error-unmatch -- $relativeSource 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            & git -C $repositoryRootPath mv -- $relativeSource $relativeDestination
            if ($LASTEXITCODE -ne 0) {
                throw "git mv failed: $relativeSource -> $relativeDestination"
            }
            $movedWithGit = $true
        }
    }

    if (-not $movedWithGit) {
        Move-Item -LiteralPath $move.Source -Destination $move.Destination
    }
}

$directories = Get-ChildItem -LiteralPath $pilotCardsRoot -Directory -Recurse |
    Sort-Object { $_.FullName.Length } -Descending

foreach ($directory in $directories) {
    if (-not (Get-ChildItem -LiteralPath $directory.FullName -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $directory.FullName
    }
}

$remainingInvalidDirectories = Get-ChildItem -LiteralPath $pilotCardsRoot -Directory -Recurse |
    Where-Object { $_.Name -cnotmatch '^[a-z0-9]+$' }

if ($remainingInvalidDirectories) {
    $invalid = ($remainingInvalidDirectories | ForEach-Object { $_.FullName }) -join [Environment]::NewLine
    throw "Normalization completed but non-canonical directories remain:$([Environment]::NewLine)$invalid"
}

Write-Host
Write-Host 'Pilot-card folder normalization completed successfully.'
Write-Host 'PNG filenames and file contents were not modified.'
Write-Host
Write-Host 'Next refresh the generated repository catalogue and knowledge base:'
Write-Host '  dotnet run -- build-knowledge-base "<repository-root>"'
