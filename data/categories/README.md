# 分类源文件

AI 打标结果按批次放这里，一个文件一批：

```
workshop-0001.json
workshop-0002.json
```

格式是扁平的 `{ "workshopId": "分类路径" }`：

```json
{
  "2154569778": "内容拓展/种族/Ancient Species",
  "2009463077": "系列/Harmony"
}
```

规则：

- 键是工坊 id（纯数字）
- 值为完整分类路径，空值会被忽略
- 同一 id 出现在多个文件时，文件名排序靠后的生效，构建脚本会列出冲突
- 不要手改 `update/modCategories.json`，它由 `tools/BuildCategoryTable.ps1` 生成

构建：

```powershell
pwsh tools/BuildCategoryTable.ps1
```

输出 `update/modCategories.json`（`version: 2`，含 `generated` 时间戳），程序启动时自动下载。
