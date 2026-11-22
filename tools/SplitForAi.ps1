param(
    [string]$Source = 'workshop-mods.jsonl',
    [string]$OutDir = 'packframe-mod-categories/input',
    [int]$BatchSize = 80,
    [int]$SummaryChars = 500
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Source)) { throw "找不到 $Source" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Get-ChildItem -LiteralPath $OutDir -Filter 'batch-*.json' | Remove-Item -Force

function Clean-Summary([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return '' }
    $s = [regex]::Replace($text, '\[[^\]]*\]', ' ')
    $s = [regex]::Replace($s, 'https?://\S+', ' ')
    $s = [regex]::Replace($s, '\s+', ' ').Trim()
    if ($s.Length -gt $SummaryChars) { $s = $s.Substring(0, $SummaryChars) }
    return $s
}

$batch = New-Object System.Collections.Generic.List[object]
$index = 1
$total = 0
foreach ($line in [IO.File]::ReadLines((Resolve-Path -LiteralPath $Source))) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $item = $line | ConvertFrom-Json
    $batch.Add([ordered]@{
        id      = [string]$item.id
        name    = [string]$item.name
        tags    = @($item.tags)
        summary = Clean-Summary ([string]$item.description)
    })
    $total++
    if ($batch.Count -ge $BatchSize) {
        $path = Join-Path $OutDir ('batch-{0:D4}.json' -f $index)
        $batch | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path -Encoding UTF8
        $batch.Clear(); $index++
    }
}
if ($batch.Count -gt 0) {
    $path = Join-Path $OutDir ('batch-{0:D4}.json' -f $index)
    $batch | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path -Encoding UTF8
}

$files = (Get-ChildItem -LiteralPath $OutDir -Filter 'batch-*.json').Count
Write-Host "共 $total 条，切成 $files 个批次（每批 $BatchSize），输出到 $OutDir"
