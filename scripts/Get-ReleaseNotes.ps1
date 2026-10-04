param(
    [string]$Tag,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$changelog = Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md'
$lines = @(Get-Content -LiteralPath $changelog -Encoding UTF8)
$start = -1
$latestTag = $null
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s+(v\d+(?:\.\d+)+)(?=\s|（|\(|$)') {
        $latestTag = $Matches[1]
        $start = $i + 1
        break
    }
}
if ($start -lt 0) { throw 'CHANGELOG.md does not contain a version section' }
if ($Tag -and $latestTag -cne $Tag) {
    throw "The latest CHANGELOG.md version is $latestTag, but the release tag is $Tag"
}

$end = $lines.Count
for ($i = $start; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s+') {
        $end = $i
        break
    }
}
if ($end -le $start) { throw "CHANGELOG.md section for $latestTag is empty" }
$notes = ($lines[$start..($end - 1)] -join "`n").Trim()
if ([string]::IsNullOrWhiteSpace($notes)) { throw "CHANGELOG.md section for $latestTag is empty" }

$directory = Split-Path -Parent $OutputPath
if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
Set-Content -LiteralPath $OutputPath -Value $notes -Encoding UTF8
Write-Host "Release notes for $latestTag written to $OutputPath"
