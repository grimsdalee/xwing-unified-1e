[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot,

    [Parameter(Mandatory = $true)]
    [string]$ShipFolder,

    [string]$SourceTextureSubfolder = "1",

    [switch]$Apply
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath(
        [System.IO.Path]::Combine($RepositoryRoot, $Path))
}

function Get-TextureFiles {
    param([string]$Folder)

    if (-not (Test-Path -LiteralPath $Folder -PathType Container)) {
        return @()
    }

    return @(
        Get-ChildItem -LiteralPath $Folder -File |
        Where-Object {
            $_.Extension -in @(".jpg", ".jpeg", ".png", ".webp")
        } |
        Sort-Object Name
    )
}

$repository = Resolve-FullPath $RepositoryRoot
$ship = Resolve-FullPath $ShipFolder

if (-not $ship.StartsWith($repository, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ShipFolder must be located beneath RepositoryRoot."
}

if (-not (Test-Path -LiteralPath $ship -PathType Container)) {
    throw "Ship folder does not exist: $ship"
}

$textureRoot = @(
    Get-ChildItem -LiteralPath $ship -Directory |
    Where-Object { $_.Name.Equals("Textures", [System.StringComparison]::OrdinalIgnoreCase) }
)

if ($textureRoot.Count -ne 1) {
    throw "Expected exactly one Textures directory beneath $ship; found $($textureRoot.Count)."
}

$textureRootPath = $textureRoot[0].FullName
$sourcePath = Join-Path $textureRootPath $SourceTextureSubfolder

if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
    throw "Source texture subfolder does not exist: $sourcePath"
}

$rootTextures = Get-TextureFiles $textureRootPath
$sourceTextures = Get-TextureFiles $sourcePath

if ($sourceTextures.Count -eq 0) {
    throw "No texture images were found directly inside: $sourcePath"
}

$sourceNames = @{}
foreach ($file in $sourceTextures) {
    if ($sourceNames.ContainsKey($file.Name.ToLowerInvariant())) {
        throw "Duplicate source texture filename: $($file.Name)"
    }

    $sourceNames[$file.Name.ToLowerInvariant()] = $file.FullName
}

$nonTextureRootFiles = @(
    Get-ChildItem -LiteralPath $textureRootPath -File |
    Where-Object {
        $_.Extension -notin @(".jpg", ".jpeg", ".png", ".webp")
    }
)

Write-Host "UnifiedToolkit Ship Texture Folder Cleanup"
Write-Host "=========================================="
Write-Host ""
Write-Host "Repository:               $repository"
Write-Host "Ship folder:              $ship"
Write-Host "Texture root:             $textureRootPath"
Write-Host "Active source subfolder:  $sourcePath"
Write-Host "Mode:                     $(if ($Apply) { 'Apply' } else { 'Preview' })"
Write-Host ""
Write-Host "Root textures to remove:  $($rootTextures.Count)"
Write-Host "Textures to move to root: $($sourceTextures.Count)"
Write-Host "Other root files retained:$($nonTextureRootFiles.Count)"
Write-Host ""

if ($rootTextures.Count -gt 0) {
    Write-Host "Root textures marked obsolete:"
    $rootTextures | ForEach-Object { Write-Host "  REMOVE $($_.Name)" }
    Write-Host ""
}

Write-Host "Active textures to promote:"
$sourceTextures | ForEach-Object { Write-Host "  MOVE   $($_.Name)" }
Write-Host ""

$destinationConflicts = @()
foreach ($sourceFile in $sourceTextures) {
    $destination = Join-Path $textureRootPath $sourceFile.Name

    if (Test-Path -LiteralPath $destination -PathType Leaf) {
        $destinationConflicts += $destination
    }
}

# Root texture files are deliberately replaced, so conflicts are expected only
# when the destination is a non-texture file with the same name.
$unexpectedConflicts = @(
    $destinationConflicts |
    Where-Object {
        [System.IO.Path]::GetExtension($_) -notin @(".jpg", ".jpeg", ".png", ".webp")
    }
)

if ($unexpectedConflicts.Count -gt 0) {
    throw "Unexpected destination conflicts: $($unexpectedConflicts -join ', ')"
}

if (-not $Apply) {
    Write-Host "Preview completed. No files were changed."
    Write-Host ""
    Write-Host "Rerun with -Apply to create a backup, remove obsolete root"
    Write-Host "textures, move the active texture set to the root, and remove"
    Write-Host "the source folder if it becomes empty."
    exit 0
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$relativeShip = [System.IO.Path]::GetRelativePath($repository, $ship)
$backupRoot = Join-Path $repository "_unifiedtoolkit_backups"
$backupPath = Join-Path $backupRoot "ship-texture-cleanup-$timestamp"
$shipBackup = Join-Path $backupPath $relativeShip

New-Item -ItemType Directory -Path $shipBackup -Force | Out-Null
Copy-Item -LiteralPath $textureRootPath -Destination $shipBackup -Recurse -Force

Write-Host "Backup: $shipBackup"

foreach ($file in $rootTextures) {
    Remove-Item -LiteralPath $file.FullName -Force
}

foreach ($file in $sourceTextures) {
    $destination = Join-Path $textureRootPath $file.Name
    Move-Item -LiteralPath $file.FullName -Destination $destination -Force
}

$remainingSourceItems = @(
    Get-ChildItem -LiteralPath $sourcePath -Force
)

if ($remainingSourceItems.Count -eq 0) {
    Remove-Item -LiteralPath $sourcePath -Force
    Write-Host "Removed empty source folder: $sourcePath"
}
else {
    Write-Host "Source folder retained because it still contains non-promoted files:"
    $remainingSourceItems | ForEach-Object { Write-Host "  $($_.Name)" }
}

Write-Host ""
Write-Host "Cleanup completed."
Write-Host "Obsolete root textures removed: $($rootTextures.Count)"
Write-Host "Active textures promoted:       $($sourceTextures.Count)"
Write-Host "Backup created:                 $shipBackup"
