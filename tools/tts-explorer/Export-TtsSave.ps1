param(
    [Parameter(Mandatory=$true)]
    [string]$InputPath,

    [Parameter(Mandatory=$false)]
    [string]$OutputDir = "output\tts-export"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (!(Test-Path $InputPath)) {
    throw "Input file not found: $InputPath"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
New-Item -ItemType Directory -Force -Path "$OutputDir\objects" | Out-Null
New-Item -ItemType Directory -Force -Path "$OutputDir\scripts" | Out-Null
New-Item -ItemType Directory -Force -Path "$OutputDir\ui" | Out-Null

Write-Host "Reading TTS JSON: $InputPath"
$json = Get-Content -Raw -Path $InputPath | ConvertFrom-Json

$summary = [ordered]@{
    SourceFile = $InputPath
    TopLevelKeys = ($json.PSObject.Properties.Name -join ", ")
    ObjectCount = 0
    ObjectsWithLua = 0
    ObjectsWithXmlUI = 0
    GlobalLuaLength = 0
    GlobalXmlUILength = 0
}

if ($json.ObjectStates) { $summary.ObjectCount = $json.ObjectStates.Count }
if ($json.LuaScript) {
    $summary.GlobalLuaLength = $json.LuaScript.Length
    $json.LuaScript | Set-Content -Encoding UTF8 "$OutputDir\scripts\Global.lua"
}
if ($json.XmlUI) {
    $summary.GlobalXmlUILength = $json.XmlUI.Length
    $json.XmlUI | Set-Content -Encoding UTF8 "$OutputDir\ui\Global.xml"
}

$rows = New-Object System.Collections.Generic.List[object]

function Export-ObjectRecursive {
    param(
        [Parameter(Mandatory=$true)] $Obj,
        [Parameter(Mandatory=$true)] [string] $Path
    )

    $guid = $Obj.GUID
    if ([string]::IsNullOrWhiteSpace($guid)) { $guid = "NO_GUID_$([guid]::NewGuid().ToString('N').Substring(0,8))" }

    $name = $Obj.Name
    $nick = $Obj.Nickname
    $safeNick = if ($nick) { ($nick -replace '[^\w\-. ]','_').Trim() } else { "" }

    if ($Obj.LuaScript -and $Obj.LuaScript.Length -gt 0) {
        $script:summary.ObjectsWithLua++
        $scriptName = "$guid"
        if ($safeNick) { $scriptName += "_$safeNick" }
        $Obj.LuaScript | Set-Content -Encoding UTF8 "$OutputDir\scripts\$scriptName.lua"
    }

    if ($Obj.XmlUI -and $Obj.XmlUI.Length -gt 0) {
        $script:summary.ObjectsWithXmlUI++
        $uiName = "$guid"
        if ($safeNick) { $uiName += "_$safeNick" }
        $Obj.XmlUI | Set-Content -Encoding UTF8 "$OutputDir\ui\$uiName.xml"
    }

    $mesh = ""
    $diffuse = ""
    $collider = ""

    $customMeshProp = $Obj.PSObject.Properties["CustomMesh"]
    if ($customMeshProp -and $customMeshProp.Value) {
        $cm = $customMeshProp.Value
        if ($cm.PSObject.Properties["MeshURL"]) { $mesh = $cm.MeshURL }
        if ($cm.PSObject.Properties["DiffuseURL"]) { $diffuse = $cm.DiffuseURL }
        if ($cm.PSObject.Properties["ColliderURL"]) { $collider = $cm.ColliderURL }
    }

    $rows.Add([pscustomobject]@{
        GUID = $Obj.GUID
        Name = $Obj.Name
        Nickname = $Obj.Nickname
        Description = $Obj.Description
        posX = $Obj.Transform.posX
        posY = $Obj.Transform.posY
        posZ = $Obj.Transform.posZ
        rotX = $Obj.Transform.rotX
        rotY = $Obj.Transform.rotY
        rotZ = $Obj.Transform.rotZ
        scaleX = $Obj.Transform.scaleX
        scaleY = $Obj.Transform.scaleY
        scaleZ = $Obj.Transform.scaleZ
        HasLua = [bool]($Obj.LuaScript -and $Obj.LuaScript.Length -gt 0)
        LuaLength = if ($Obj.LuaScript) { $Obj.LuaScript.Length } else { 0 }
        HasXmlUI = [bool]($Obj.XmlUI -and $Obj.XmlUI.Length -gt 0)
        MeshURL = $mesh
        DiffuseURL = $diffuse
        ColliderURL = $collider
        Path = $Path
    }) | Out-Null

    $containedObjectsProp = $Obj.PSObject.Properties["ContainedObjects"]
    if ($containedObjectsProp -and $containedObjectsProp.Value) {
        $i = 0
        foreach ($child in $containedObjectsProp.Value) {
            Export-ObjectRecursive -Obj $child -Path "$Path/$guid[$i]"
            $i++
        }
    }
}

$i = 0
foreach ($obj in $json.ObjectStates) {
    Export-ObjectRecursive -Obj $obj -Path "ObjectStates[$i]"
    $i++
}

$rows | Export-Csv -NoTypeInformation -Encoding UTF8 "$OutputDir\objects\objects.csv"

$urls = foreach ($row in $rows) {
    if ($row.MeshURL) { [pscustomobject]@{Type="Mesh"; URL=$row.MeshURL; GUID=$row.GUID; Nickname=$row.Nickname} }
    if ($row.DiffuseURL) { [pscustomobject]@{Type="Diffuse"; URL=$row.DiffuseURL; GUID=$row.GUID; Nickname=$row.Nickname} }
    if ($row.ColliderURL) { [pscustomobject]@{Type="Collider"; URL=$row.ColliderURL; GUID=$row.GUID; Nickname=$row.Nickname} }
}
$urls | Export-Csv -NoTypeInformation -Encoding UTF8 "$OutputDir\objects\asset_urls.csv"

$summary | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 "$OutputDir\summary.json"

Write-Host "Export complete:"
Write-Host "  $OutputDir\summary.json"
Write-Host "  $OutputDir\objects\objects.csv"
Write-Host "  $OutputDir\objects\asset_urls.csv"
Write-Host "  $OutputDir\scripts\"
Write-Host "  $OutputDir\ui\"
