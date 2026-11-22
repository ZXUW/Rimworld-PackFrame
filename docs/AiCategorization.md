# AI 分类工作流

前提：先跑出 `workshop-mods.jsonl`（每条含 id / name / tags / description）。

## 1. 切分喂料

```powershell
pwsh tools/SplitForAi.ps1 -BatchSize 80 -SummaryChars 500
```

输出 `packframe-mod-categories/input/batch-0001.json` …，每个文件是一个数组：

```json
[
  {
    "id": "2154569778",
    "name": "Ancient Species",
    "tags": ["1.6", "Race", "Hair"],
    "summary": "This mod adds two races ..."
  }
]
```

- `summary` 默认只取简介前 500 字（可调），去掉 BBCode，避免更新日志和授权声明干扰判断
- 想减少 token 就调小 `-SummaryChars`，想更准就调大

## 2. 给 AI 的提示词

把下面的内容作为系统提示，分批把 `packframe-mod-categories/input/batch-XXXX.json` 发过去：

```text
你是 RimWorld 模组库的分类员。只依据给定分类体系，为每个模组指定唯一分类。

分类体系：
- 内容拓展/种族/<种族名>      种族本体、该种族的翻译/服装/补丁/表情；多族用 "A and B"
- 内容拓展/<类型>             动物、植物、基因、家具、发型、服装、地面、武器、建筑、派系、事件、场景、特性、玩法、音乐、生物、地图、食物、医疗、载具、生产、研究
- 非内容拓展/<类型>           性能优化、UI 美化、贴图美化、内容简化、文本修改、操作便利、汉化工具
- 系列/<系列名>               只有内容混杂的整合系列；属于该系列的模组一律放这里

判定提示：
- 只换皮/重绘、不改数值 → 非内容拓展/贴图美化
- 只改名字/描述/文案 → 非内容拓展/文本修改
- 加特质、开局剧本、BGM、机械体 → 内容拓展/特性、场景、音乐、生物

判定顺序：
1. 属于某个混杂整合系列 → 系列/<系列名>
2. 服务于某个种族 → 内容拓展/种族/<种族名>；服务多个种族用 "A and B" 连接，超过两个写成 "A and B and……"
3. 增加游戏内容 → 内容拓展/<最贴近的类型>
4. 不增加内容，只改体验 → 非内容拓展/<类型>
5. 无法判断 → 空字符串

输出要求：
- 只输出 JSON 对象，键是模组 id，值是分类路径或空字符串
- 不要输出解释、不要加代码块、不要改动 id
- 一次只处理给出的这一批，不要补别的模组

示例：
{"123":"内容拓展/种族/NewRatkin","456":"内容拓展/家具","789":""}
```

## 3. 回收合并

把每批 AI 返回的 JSON 原样存成 `packframe-mod-categories/output/batch-0001.json`，然后：

```powershell
pwsh tools/MergeAiCategories.ps1
```

生成 `category-table.json`：

```json
{
  "version": 1,
  "workshop": { "2154569778": "内容拓展/种族/Ancient Species" }
}
```

空字符串会被丢掉（= 未分类）。检查无误后：

- 直接放到 `<程序目录>/runtime/rules/modCategories.json` 就能生效
- 要发布给所有人，则合并进仓库的 `update/modCategories.json`

## 4. 注意

- AI 返回的分类可能自造不存在的类型；合并脚本不做校验，建议抽查前 50 条
- 种族名要统一写法（`NewRatkin` 而不是 `Ratkin` / `New Ratkin` 混用），否则会拆成多个文件夹
- 一批里模组太少会浪费 token，太多会漏答；80~120 条比较稳
