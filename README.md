# video-subtitle-batch-packer

一个基于 Windows App SDK (WinUI 3) 开发的 **C# 视频 + 字幕批量封装工具**，使用 FFmpeg 实现视频与字幕的自动匹配与批量封装。

> 本项目为原生 C# (.NET) 重写版，原 Python (tkinter + PyInstaller) 实现已移除。

## ✨ 功能特性

- **集号自动提取**引擎，支持：

  - 常规数字集号（`01`、`24`）；

  - 小数集号（`24.5`）；

  - 再版标记（`06v2` → 规整为 `06`）；

  - 特殊集号（`OVA`、`OAD`、`SP`、`NCOP`、`NCED` 及带编号形式）；

- **智能字幕分组**：仅当去掉语言后缀（`sc`/`tc`/`en` 等）后文件主体完全一致的字幕才归为同一字幕组（如 `06.zh.ass` 与 `06.tc.ass` 归组，而 `06` 与 `06.5` 各自独立）；

- 视频与字幕**按序号一一配对**，可勾选/全选，支持「上移 / 下移」调整顺序；

- 列表实时显示**集号**（强调色），表头与数据列严格对齐，仅竖向滚动；

- 刷新按钮（带转圈动画），一键重新载入文件列表；

- 后台静默调用 FFmpeg（不弹命令行窗口），日志区自动滚动并显示实际执行的命令；

- 视频与字幕**同文件夹**或**分文件夹**两种模式；

- 可设置是否加为默认字幕轨道、封装成功后安全替换原视频；

- 多样式 UI：Mica 背景、圆角卡片、明暗主题自适应。

## 🛠 环境要求

1. 操作系统：Windows 10/11（富媒体交互需 Windows 11 体验最佳）；
2. 构建工具：.NET 10 SDK（`net10.0-windows10.0.19041.0`）与 Windows App SDK 1.8；
3. FFmpeg（[下载地址](https://ffmpeg.org/download.html)）：

   - 已加入系统 PATH 时程序内路径栏可留空；

   - 或手动选择 `ffmpeg.exe`；
4. 格式支持：

   - 视频：`mkv`、`mp4`、`mov`、`avi`、`m4v`、`webm`；

   - 字幕：`ass`、`ssa`、`srt`、`vtt`。

## 📁 目录结构

```
video-subtitle-batch-packer/
├── WinUIBatchPacker/        # 主项目（WinUI 3 应用）
│   ├── MainWindow.xaml(.cs) # 主界面与业务逻辑
│   ├── MediaListView.xaml(.cs) # 媒体列表控件
│   ├── MediaService.cs      # 集号提取 / 字幕分组 / FFmpeg 调用核心
│   ├── Models.cs            # 数据模型
│   ├── WinUIBatchPacker.csproj
│   ├── global.json          # 锁定 .NET 10 SDK
│   └── publish/             # 发布产物（单文件，被 gitignore 忽略）
├── .github/workflows/winui-build.yml  # win-x64 CI 构建
└── README.md
```

## 🚀 构建与发布

在 `WinUIBatchPacker/` 目录下执行：

**方式一：框架依赖最小版（默认，依赖本机已安装的 .NET 10 + Windows App SDK 运行时）**

```bash
dotnet publish -c Release -p:Platform=x64 --self-contained false -o publish
```

产物在 `publish/`，主程序 `WinUIBatchPacker.exe` 约几百 KB，总量约 37 MB，不打包 .NET 运行时，需本机已安装 .NET 10（Desktop）与 Windows App SDK 1.8 运行时。

> 为什么不提供"只省略 .NET、内嵌 WinApp SDK 的单个 exe"？
> WinUI 单文件模式官方仅支持自包含（`.NET SelfContained`）——若省略 .NET 仅内嵌 WinApp SDK 打包成单文件，运行时依赖的 WinApp SDK 原生库无法正确自解压，导致启动崩溃；且该模式无法启用压缩，实际体积反而达 137+ MB，只会更大。

**方式二：自包含单文件版（可移植，无需安装任何运行时）**

```bash
dotnet publish -c Release -p:Platform=x64 -o publish \
  -p:SelfContained=true -p:WindowsAppSDKSelfContained=true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
```

产物为单个 `publish/WinUIBatchPacker.exe`（约 85 MB，内置 .NET 与 Windows App SDK 运行时）。

## 📖 使用方法

1. 启动程序，配置 **FFmpeg 路径**（已加入 PATH 可留空）；
2. 选择**视频文件夹**与**字幕文件夹**（或勾选「视频和字幕位于同一个文件夹」）；
3. 可选：设置输出文件夹、字幕语言、编码、默认轨道、替换原视频；
4. 程序自动提取集号并分组，确认视频/字幕**序号一一对应**（列表左侧集号可快速核对）；
5. 点击**开始批量封装**，日志区实时显示每个文件的执行命令与结果；
6. 完成后在输出文件夹（或原位）查看封装产物。

## ⚠️ 注意事项

- 视频与字幕按**列表序号**一一配对，封装前请核对数量是否相等；

- 若封装失败，查看日志中的 FFmpeg 命令与退出码（常见：FFmpeg 未配置、编码不匹配、格式不支持）；

- `06v2` 与 `06` 因名称不同会被视为不同条目，请酌情勾选。

## ❓ 常见问题

- **找不到 FFmpeg？** 确认其已加入 PATH 并重启，或手动选择 `ffmpeg.exe`。

- **字幕乱码？** 在「字幕选项」中切换编码格式后重新封装。

- **列表不显示？** 点击列表标题栏的刷新按钮重新载入。

- **打开时提示缺少 .NET 运行时？** 使用框架依赖版（方式一）时本机需安装 .NET 10 Desktop Runtime；缺失时系统会**弹窗提示安装**（指向 .NET 官方安装页），安装后重启即可。也可改用自包含单文件版（方式二），无需安装任何运行时。

