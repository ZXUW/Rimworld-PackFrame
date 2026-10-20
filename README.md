# Rimworld PackFrame

RimWorld 整合包管理框架，基于 Avalonia 12 和 .NET 10。

## 功能特点

实例隔离

整合包下载安装和上传

方便的刷子模组染色

直接显示报错类型的警告

## 下载

在 Releases 中下载：

Windows x64：`Rimworld PackFrame-portable.zip`

macOS Intel：`Rimworld PackFrame-osx-x64.zip`

macOS Apple Silicon：`Rimworld PackFrame-osx-arm64.zip`

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
