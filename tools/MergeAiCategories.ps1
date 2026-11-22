param(
    [string]$InDir = 'packframe-mod-categories/output',
    [string]$Out = 'category-table.json'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $InDir)) { throw "找不到 $InDir，先把 AI 返回的 JSON 放进去" }

$map = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$files = 0
$accepted = 0
$skipped = 0

foreach ($file in Get-ChildItem -LiteralPath $InDir -Filter '*.json') {
    $files++
    $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    try { $parsed = $text | ConvertFrom-Json } catch { Write-Warning "$($file.Name) 不是合法 JSON，跳过"; continue }

    if ($parsed -is [System.Management.Automation.PSCustomObject]) {
        foreach ($prop in $parsed.PSObject.Properties) {
            $category = [string]$prop.Value
            if ([string]::IsNullOrWhiteSpace($category)) { $skipped++; continue }
            $map[$prop.Name] = $category.Trim()
            $accepted++
        }
    }
    elseif ($parsed -is [array]) {
        foreach ($item in $parsed) {
            $id = [string]$item.id
            $category = [string]$item.category
            if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($category)) { $skipped++; continue }
            $map[$id] = $category.Trim()
            $accepted++
        }
    }
    else {
        Write-Warning "$($file.Name) 结构不认识，跳过"
    }
}

$ordered = [ordered]@{}
foreach ($key in ($map.Keys | Sort-Object)) { $ordered[$key] = $map[$key] }

$document = [ordered]@{ version = 1; workshop = $ordered }
ConvertTo-Json -InputObject $document -Depth 5 | Set-Content -LiteralPath $Out -Encoding UTF8

Write-Host "读取 $files 个文件，写入 $accepted 条，跳过空值 $skipped 条"
Write-Host "输出: $Out"
