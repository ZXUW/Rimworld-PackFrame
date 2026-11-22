param(
    [string]$SourceDir = 'data/categories',
    [string]$Out = 'update/modCategories.json'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $SourceDir)) { throw "找不到 $SourceDir" }

$map = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$origin = @{}          # id -> 最后一次写入的文件
$conflicts = New-Object System.Collections.Generic.List[string]
$files = 0
$accepted = 0

foreach ($file in (Get-ChildItem -LiteralPath $SourceDir -Filter '*.json' | Sort-Object Name)) {
    $files++
    $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    try { $parsed = $text | ConvertFrom-Json } catch { Write-Warning "$($file.Name) 不是合法 JSON，跳过"; continue }

    # 支持两种格式：扁平 { id: 分类 }，或带 workshop 段的对象
    $entries = @{}
    if ($parsed.PSObject.Properties.Name -contains 'workshop') {
        foreach ($prop in $parsed.workshop.PSObject.Properties) { $entries[$prop.Name] = [string]$prop.Value }
    }
    else {
        foreach ($prop in $parsed.PSObject.Properties) { $entries[$prop.Name] = [string]$prop.Value }
    }

    foreach ($id in $entries.Keys) {
        $category = $entries[$id]
        if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($category)) { continue }
        if ($map.ContainsKey($id) -and $map[$id] -ne $category.Trim()) {
            $conflicts.Add("$id`t$($map[$id])`t->`t$($category.Trim())`t($($origin[$id]) → $($file.Name))")
        }
        $map[$id] = $category.Trim()
        $origin[$id] = $file.Name
        $accepted++
    }
}

$ordered = [ordered]@{}
foreach ($key in ($map.Keys | Sort-Object)) { $ordered[$key] = $map[$key] }

$document = [ordered]@{
    version   = 2
    generated = (Get-Date).ToString('O')
    workshop  = $ordered
}
$dir = Split-Path -Parent $Out
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
ConvertTo-Json -InputObject $document -Depth 5 | Set-Content -LiteralPath $Out -Encoding UTF8

Write-Host "读取 $files 个文件，写入 $accepted 条，输出 $Out"
if ($conflicts.Count -gt 0) {
    Write-Host "冲突 $($conflicts.Count) 条（后写入的生效）:"
    $conflicts | ForEach-Object { Write-Host "  $_" }
}
else {
    Write-Host "无冲突"
}
