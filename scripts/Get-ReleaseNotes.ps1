param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$changelog = Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md'
$lines = @(Get-Content -LiteralPath $changelog -Encoding UTF8)
$heading = '^##\s+' + [regex]::Escape($Tag) + '(?:\s|（|\(|$)'
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match $heading) {
        $start = $i + 1
        break
    }
}
if ($start -lt 0) { throw "CHANGELOG.md does not contain a section for $Tag" }

$end = $lines.Count
for ($i = $start; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s+') {
        $end = $i
        break
    }
}
$notes = ($lines[$start..($end - 1)] -join "`n").Trim()
if ([string]::IsNullOrWhiteSpace($notes)) { throw "CHANGELOG.md section for $Tag is empty" }

$directory = Split-Path -Parent $OutputPath
if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
Set-Content -LiteralPath $OutputPath -Value $notes -Encoding UTF8
Write-Host "Release notes for $Tag written to $OutputPath"
