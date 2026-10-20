# Rimworld PackFrame

RimWorld 整合包管理框架，基于 Avalonia 12 和 .NET 10。

## 功能

- 实例管理、存档目录隔离
- Mod 启用/禁用、排序、搜索、颜色标记
- Steam 创意工坊合集与 SteamCMD 下载
- 本地/GitHub 整合包安装
- 导出 GitHub 整合包仓库
- 整合包索引 PR 审核提交
- 预览图、README 和创意工坊合集封面

## 下载

在 Releases 中下载：

- Windows x64：`Rimworld PackFrame-portable.zip`
- macOS Intel：`Rimworld PackFrame-osx-x64.zip`
- macOS Apple Silicon：`Rimworld PackFrame-osx-arm64.zip`

Windows 解压后双击 `Rimworld PackFrame.exe`。macOS 解压后双击 `Rimworld PackFrame.command`；未签名应用首次启动如被拦截，请右键选择“打开”。

## 构建

```powershell
dotnet build .\app\PackFrame\PackFrame.csproj -c Release
```

发布 Windows：

```powershell
dotnet publish .\app\PackFrame\PackFrame.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

发布 macOS：

```powershell
dotnet publish .\app\PackFrame\PackFrame.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true
dotnet publish .\app\PackFrame\PackFrame.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
```
