param(
    [Parameter(Mandatory=$true)]
    [string]$LeftObjectsCsv,

    [Parameter(Mandatory=$true)]
    [string]$RightObjectsCsv,

    [Parameter(Mandatory=$false)]
    [string]$OutputPath = "output\tts-compare.csv"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$left = Import-Csv $LeftObjectsCsv
$right = Import-Csv $RightObjectsCsv

New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath) | Out-Null

$results = foreach ($l in $left) {
    $matches = $right | Where-Object {
        ($_.Nickname -eq $l.Nickname -and $_.Name -eq $l.Name) -or
        ($_.GUID -eq $l.GUID)
    }

    if ($matches.Count -eq 0) {
        [pscustomobject]@{
            LeftGUID = $l.GUID
            LeftName = $l.Name
            LeftNickname = $l.Nickname
            RightGUID = ""
            RightName = ""
            RightNickname = ""
            MatchType = "NoMatch"
        }
    } else {
        foreach ($r in $matches) {
            [pscustomobject]@{
                LeftGUID = $l.GUID
                LeftName = $l.Name
                LeftNickname = $l.Nickname
                RightGUID = $r.GUID
                RightName = $r.Name
                RightNickname = $r.Nickname
                MatchType = if ($r.GUID -eq $l.GUID) { "GUID" } else { "NameNickname" }
            }
        }
    }
}

$results | Export-Csv -NoTypeInformation -Encoding UTF8 $OutputPath
Write-Host "Comparison written to $OutputPath"
