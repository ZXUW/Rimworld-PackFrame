# Rimworld PackFrame

基于 Avalonia 12 和 .NET 10 的 RimWorld 模组与整合包管理器。支持多实例、模组列表编辑、配置同步、工坊/GitHub 整合包安装与分享。

## 快速开始

1. 将发布包解压到可写目录，保留完整目录结构。
2. Windows 本地目录版双击根目录 `Rimworld PackFrame.exe`；它会启动 `app/PackFrame/bin/Release/net10.0/packframe.exe`。单独发布版运行其 `packframe` 可执行文件。
3. 首次启动选择语言，自动检测或手动选择 RimWorld 游戏目录。
4. 在实例菜单中新建、复制或导入实例，也可以打开整合包窗口安装整合包。
5. 编辑启用列表后点击“保存”，再启动游戏。Steam 工坊内容需要等待 Steam 完成下载。

支持 English、简体中文、繁體中文、日本語。

## 主窗口功能

- 左侧显示模组预览图、名称、作者、路径、Package ID 和简介。
- 三列列表：未启用模组、两个可选择实例的已启用列表；目标实例决定未启用列的内容。
- 搜索名称、作者、路径、Package ID 或支持版本；支持来源筛选、问题筛选和显示模式切换。
- 支持单选、多选、Shift 范围选择、Ctrl 逐项选择、跨列拖拽和排序、双击启用/禁用。
- 支持颜色标记、颜色标签、涂色操作、撤销/重做；历史保留最近 10 步。
- 显示排序、版本、重复与缺失模组信息。规则检查不等同于完整兼容性验证。
- 排序检查包含 `loadAfter`、`forceLoadAfter`、`loadBefore`、`forceLoadBefore`；启用的 `incompatibleWith` 关系会在 S 问题详情中明确显示，不作为可用排序解决的问题。
- 实例菜单支持新建、复制、重命名、删除、导入、导出和打开配置目录。
- 导入支持 ModsConfig XML、兼容的 JSON 模组列表、`.rml`、`.rws` 存档模组信息、实例 ZIP 和实例文件夹。

列表编辑暂存在内存，点击“保存”才写入 `ModsConfig.xml`。普通保存保留缺失模组项。刷新或关闭时如有未保存的列表修改，会提供“保存 / 丢弃 / 取消”。颜色标记独立写入颜色文件。

启动前会将磁盘配置中的 `activeMods` / `knownExpansions` 包名规范化为小写，以兼容旧配置；仅在需要修改时写入，并在同目录保留 `ModsConfig.xml.before-normalize-*.bak`。发现/浏览实例不会修改配置。

## 实例与数据目录

```text
PackFrame/
  Rimworld PackFrame.exe             Windows 本地目录版入口
  app/PackFrame/                     源码
    bin/Release/net10.0/             本地目录版运行程序
      packframe.json                语言、游戏路径、实例选择等应用设置
      packframe.log                 启动器错误日志
  instance/
    ModColors.json                  模组颜色与标签
    profile1/
      Config/                       模组排序与游戏/模组设置
        ModsConfig.xml
      Saves/                        存档
      Mods/                         此实例附带或 SteamCMD 下载的模组
      packframe.instance.json       显示名、来源、配置同步绑定、SteamCMD 标记
      player.log                    游戏日志
    profile2/
  Input profile/                    导入文件的默认位置
  Output profile/                   导出的实例或整合包文件
  tools/steamcmd/                    自动安装的 SteamCMD（使用时生成）
  runtime/                          整合包安装暂存目录
  tests/PackFrame.Checks/            回归检查
```

`packframe.json` 和 `packframe.log` 始终位于实际托管程序目录；单文件发布版与其可执行文件同目录。错误日志超过约 2 MB 后轮换为 `packframe.log.1`。

实例用 `profileN` 文件夹编号；重命名只改变显示名，复制和重命名会保留整合包来源绑定。旧版 `profile/` 可用于首次实例迁移；当前版本不需要旧的 `tools/pack.ps1` 流程。

损坏的实例显示 `⚠`，悬停实例选择框可查看原因；其他实例仍可使用。损坏实例禁止列表编辑和启动，菜单保留打开目录、导入恢复、删除（普通实例）入口。修复文件后点击刷新重新加载。

## 游戏启动与隔离

普通实例按以下方式启动：

```text
"<游戏可执行文件>" -logfile "<实例目录>/player.log" -savedatafolder=<实例目录>
```

实际参数通过 `ProcessStartInfo.ArgumentList` 传入，支持空格和中文路径。避免在实例路径中使用 `=`，游戏自身的参数解析可能无法正确处理。

`-savedatafolder` 隔离 Config、Saves、ModLists、Scenarios、Ideos 等运行数据。**“官方路径”实例直接使用所选原版配置目录，编辑和保存会作用于该目录**；普通实例则使用自己的目录。

扫描来源包括游戏 `Data`、游戏 `Mods`、Steam 工坊和实例 `Mods`。实例模组通过临时目录链接提供给游戏，正常退出后清理，下次启动前也会恢复遗留链接。

配置和存档隔离不等于游戏二进制及工坊版本隔离。Steam 仍可能自动更新工坊模组；需要固定游戏与模组版本时，应另行保留完整副本。

## 整合包安装、同步与发布

- 整合包窗口读取 GitHub 或自定义 URL 的索引和整合包定义。
- 支持工坊订阅、SteamCMD 下载、GitHub 模组与本地模组压缩包。
- 安装后建立新实例，保存来源绑定，以便后续同步配置。
- 安装先在隐藏临时实例完成并校验，成功后才发布为 `profileN`；失败时清理临时实例并保留 `runtime/install-*` 下载内容。安装过程中复制模组需要额外磁盘空间。
- 配置同步先下载全部文件并解析 XML，再应用；应用失败尝试回滚，回滚失败保留备份并报告路径。
- 导出/发布流程支持工坊合集与 GitHub 仓库；发布所需 Steam 登录或 GitHub token 按窗口提示提供。
- GitHub 仓库内容通过一次 Git commit 更新，基于原有 tree 保留无关文件，分支并发变更时拒绝非快进覆盖。大型模组包仍单独上传 Release，Release 资产与分支提交不属于同一事务；空仓库可能需要额外的初始化提交。
- 发布读取仓库的默认分支（包括 `master`）。大包复用 `packframe-mods` Release，并以内容 SHA256 命名资产；相同内容复用已上传资产，提交成功后清理此 Release 中旧的受管资产。旧版生成的 `packframe-mods-*` 历史 Release 不自动删除，以免破坏历史下载链接。
- 分享前检查配置文件中的账号、密钥等私人信息，并确认附带模组文件允许再分发。

## 备份与恢复

- 完整备份：关闭游戏后复制整个 `instance/`；需要保留软件设置时同时备份 `packframe.json`。
- 单实例备份：用实例菜单导出 ZIP；导出会跳过缓存路径和临时链接清单。
- 恢复：通过实例菜单导入 ZIP，选择新建或覆盖。覆盖 ZIP 会替换整个实例数据，列表文件只替换排序。
- 故障排查：查看 `packframe.log` 与实例 `player.log`。若提示回滚失败，先保留提示的备份目录。

## 平台状态

| 平台 | 实现与验证范围 |
|---|---|
| Windows x64 | 本地构建与自动回归检查通过；Windows 目录链接使用 junction |
| Linux | 路径检测、启动、符号链接和 SteamCMD 平台包已有实现；游戏与 GUI 流程仍需实机验证 |
| macOS | 使用 `open -W -n` 等待新游戏实例；提供对应 SteamCMD 包；游戏与 GUI 流程仍需实机验证 |

SteamCMD 仍依赖 Valve 的平台运行环境，Apple Silicon 上的 Intel 组件可能需要 Rosetta。macOS 正式分发需另外制作 `.app` 并处理签名/公证。

## 开发与验证

需要 .NET 10 SDK。

```powershell
dotnet run --project app/PackFrame
dotnet build app/PackFrame -c Release
dotnet run --project tests/PackFrame.Checks -c Release
```

构建到默认目录前先关闭该目录下运行的程序，以免二进制被占用。可使用 `--artifacts-path <临时目录>` 隔离构建输出。

检查使用临时目录，不操作玩家实例；覆盖元数据、配置更新回滚、真实目录链接、颜色缓存、列表插入、双实例撤销/重做、启动参数和安装落盘。GUI 拖拽、多选和平台游戏启动的手动检查见 [测试说明](tests/PackFrame.Checks/README.md)。

`.github/workflows/build.yml` 在 Windows、Linux、macOS 上构建并运行这些检查；上传仓库后由 GitHub Actions 执行，构建检查不代替 GUI/游戏实机验证。

关键代码：

| 文件 | 职责 |
|---|---|
| `MainWindow.axaml` / `MainWindow.axaml.cs` | 主窗口布局、交互与界面状态 |
| `MainWindow.Paint.cs` / `MainWindow.DragDrop.cs` / `MainWindow.FilterSort.cs` | 已拆出的涂色、拖拽滚动、搜索与排序逻辑；其余界面协调仍在主文件 |
| `GameLaunchService.cs` | 启动参数、进程监控与退出后的处理 |
| `ModpackInstanceInstaller.cs` | 将安装结果写入新实例 |
| `ModListOperations.cs` | 列表插入及撤销/重做快照恢复 |
| `InstanceStore.cs` | 实例发现、导入导出与配置保存 |
| `ModCatalog.cs` | 模组扫描与元数据解析 |
| `ModLinkService.cs` | 临时模组链接及恢复 |
| `ModpackService.cs` | 下载、整合包解析与配置同步 |
| `FileTransactions.cs` / `AppLog.cs` | 文件替换、回滚与错误日志 |

发布示例：

```powershell
dotnet publish app/PackFrame -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
dotnet publish app/PackFrame -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
dotnet publish app/PackFrame -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
dotnet publish app/PackFrame -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

发行 ZIP 需要从对应发布产物重新打包；本地 `dotnet build` 不会自动更新已有 ZIP。
## 许可证

- 代码：[MIT](LICENSE)
- 第三方依赖：[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
