# 模组分类表（ModCategories.json）

分类只查这张表，不再做任何运行时推断。查不到 = 未分类。

## 文件位置与优先级

三层叠加，后面的覆盖前面的：

| 层 | 位置 | 用途 |
|---|---|---|
| 内置 | `app/PackFrame/Assets/ModCategories.json` | 随程序发布的默认表 |
| 社区 | `<程序目录>/runtime/rules/modCategories.json` | 启动时从 `update/modCategories.json` 下载，所有用户共享 |
| 用户 | `<程序目录>/runtime/rules/modCategories.user.json` | 「导出分类表」写这里，自己的改动不会被社区更新冲掉 |

查找顺序：用户层 → 社区层 → 内置层。同一个 id 在用户层有值就以用户层为准；社区层更新只影响用户没改过的条目。

仓库里维护的是 `data/categories/*.json`（按批次），由 `tools/BuildCategoryTable.ps1` 合并成 `update/modCategories.json`。

## 结构

```json
{
  "version": 1,
  "workshop": {
    "2154569778": "种族/Ancient Species"
  },
  "packageId": {
    "haduki.ancientspecies": "种族/Ancient Species"
  },
  "name": {
    "Ancient Species": "种族/Ancient Species"
  }
}
```

三段都可以省略；同一个模组建议只写一段，重复写也没关系。

## 查找顺序

1. `workshop`：模组 `About.xml` 里的 `steamWorkshopUrl` 取数字 id
2. `packageId`：模组 `About.xml` 的 `packageId`
3. `name`：`About.xml` 的 `<name>`，精确匹配（忽略大小写和首尾空白）
4. `name` 归一化后再匹配一次：去掉开头的 `[...]`/`【...】`、压缩空格、忽略大小写

先命中先用，后面的不再看。

## 分类路径写法

- 用 `/` 分层，例如 `种族/Ratkin`、`系列/Vanilla Expanded`、`美化/发型`
- 每一层就是界面里的一个文件夹；大小写不敏感
- 想新建顶级目录，直接写新前缀即可（如 `派系/Aya`）

## 程序内分类模板

分类模式里的「模组分类」窗口读取两种模板：

| 模板 | 位置 | 来源 |
|---|---|---|
| PackFrame仓库 | `<程序目录>/runtime/rules/modCategories.json` | 启动时从 `update/modCategories.json` 下载 |
| 玩家保存 | `<PackFrame根目录>/instance/ModCategoryTemplates/*.json` | 「保存」或「导入」生成 |

- 「补充分类」只填当前没有分类的已安装模组；「覆盖分类」会改写已有分类。
- 「清空目前分类」只清空当前已安装模组的分类，保留别名和前置屏蔽。
- 保存和导出只写 `packageId` 段，不写入未安装模组的分类。
- 保存的模板文件名可以做安全替换，显示名写在 `title` 字段里。
- 导入接受 `workshop` / `packageId` / `name` 任一段；三段同时存在时按 workshop → packageId → 名称匹配。

## 维护流程

1. AI 打标结果存成 `data/categories/workshop-XXXX.json`（扁平 `{ "id": "分类" }`）
2. 运行 `pwsh tools/BuildCategoryTable.ps1`：合并、列冲突、写 `update/modCategories.json`
3. 提交后，用户启动时自动下载
4. 用户自己的改动走「导出分类表」，只写用户层，不会被社区更新覆盖

导出的用户层只包含 `packageId` 段——这是本地一定能拿到的键。社区表用 `workshop` 段，覆盖未安装的模组。

## 例子：同一套分类写三种键

```json
{
  "version": 1,
  "workshop": {
    "2009463077": "系列/Harmony"
  },
  "packageId": {
    "brrainz.harmony": "系列/Harmony"
  },
  "name": {
    "Harmony": "系列/Harmony"
  }
}
```
