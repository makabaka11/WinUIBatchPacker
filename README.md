# WinUIBatchPacker

一个基于 Windows App SDK (WinUI 3) 开发的 **C# 视频 + 字幕+字体（子集化）批量封装工具**，使用 FFmpeg 实现视频与字幕的自动匹配与批量封装。

版本更新见 [CHANGELOG.md](CHANGELOG.md)。

> 本项目为原生 C# (.NET) 重写版，原 Python (tkinter + PyInstaller) 实现已移除。

## ✨ 功能特性

- **集号自动提取**引擎，支持：

  - 常规数字集号（`01`、`24`）；

  - 小数集号（`24.5`）；

  - 再版标记（`06v2` → 规整为 `06`）；

  - 特殊集号（`OVA`、`OAD`、`SP`、`NCOP`、`NCED` 及带编号形式）；

- **智能字幕分组**：仅当去掉语言后缀（`sc`/`tc`/`en` 等）后文件主体完全一致的字幕才归为同一字幕组（如 `06.zh.ass` 与 `06.tc.ass` 归组，而 `06` 与 `06.5` 各自独立）；

- 视频与字幕**按序号一一配对**，可勾选/全选，支持拖拽或「上移 / 下移」调整顺序；

- 列表实时显示**集号**，表头与数据列严格对齐，仅竖向滚动；

- 刷新按钮，一键重新载入文件列表；

- 后台静默调用 FFmpeg，日志区自动滚动；可在设置中开启执行命令输出（默认关闭）；
- 日志默认在每个成功文件名后显示与原视频相比的大小变化，并在批次结束时汇总；可在设置中关闭；
- 标题栏设置按钮左侧的**常用工具**菜单：选择 MKV 查看音频、字幕轨道和字体附件；选择视频查看容器、时长、大小、码率及画面和音频参数；

- 视频与字幕**同文件夹**或**分文件夹**两种模式；

- 可设置是否加为默认字幕轨道、封装成功后安全替换原视频；
- 可选字体子集化：从目录或 ZIP、7Z、RAR 等压缩包选择字体；首次封装可连同字幕加入 MKV，也可为已有 ASS/SSA 字幕的 MKV 补充或替换字体附件；

- 多样式 UI：Mica 背景、圆角卡片、明暗主题自适应。

## 🛠 环境要求

1. 操作系统：Windows 10/11（富媒体交互需 Windows 11 体验最佳）；
2. 构建工具：.NET 10 SDK（`net10.0-windows10.0.19041.0`）与 Windows App SDK 1.8；
3. FFmpeg（[下载地址](https://ffmpeg.org/download.html)）：

   - 已加入系统 PATH 时程序内路径栏可留空；

   - 或在设置中手动选择 `ffmpeg.exe`；媒体分析工具还需要同目录的 `ffprobe.exe`；
   - 使用字体子集化时还需要本机 Python 与 `fonttools`（`python -m pip install fonttools`）；
4. 格式支持：

   - 视频：`mkv`、`mp4`、`mov`、`avi`、`m4v`、`webm`；

   - 字幕：`ass`、`ssa`、`srt`、`vtt`。

## 📁 目录结构

```
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

产物在 `publish/`，主程序 `WinUIBatchPacker.exe` 约几百 KB，总量约 40 MB，不打包 .NET 运行时，需本机已安装 .NET 10（Desktop）与 Windows App SDK 1.8 运行时。

**方式二：自包含单文件版（可移植，无需安装任何运行时）**

```bash
dotnet publish -c Release -p:Platform=x64 -o publish \
  -p:SelfContained=true -p:WindowsAppSDKSelfContained=true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
```

产物为单个 `publish/WinUIBatchPacker.exe`，内置 .NET 与 Windows App SDK 运行时。字体子集化仍需本机 Python 与 fontTools。

推送 `vX.Y` 版本标签时，CI 会构建两种 Windows 产物，并自动读取 `CHANGELOG.md` 最上方的版本段落作为 GitHub Release 说明。标签必须与该版本标题一致；版本不匹配或说明为空时发布会失败，避免使用旧版日志。普通分支推送只构建，不发布。

## 📖 使用方法

1. 启动程序，按需点击标题栏的**设置**配置 FFmpeg 路径（已加入 PATH 可留空）；设置会自动保存；
2. 选择**视频文件夹**与**字幕文件夹**（或勾选「视频和字幕位于同一个文件夹」）；
3. 可选：设置输出文件夹、字幕语言、编码、默认轨道、替换原视频；
4. 程序自动提取集号并分组，确认视频/字幕**序号一一对应**（列表左侧集号可快速核对）；
5. 点击**开始批量封装**，日志区实时显示每个文件的处理结果；开启设置中的命令输出后也会显示 FFmpeg 命令；
6. 完成后在输出文件夹（或原位）查看封装产物。

## ⚠️ 注意事项

- 视频与字幕按**列表序号**一一配对，封装前请核对数量是否相等；

- 若封装失败，可在设置中开启命令输出，查看日志中的 FFmpeg 命令与退出码（常见：FFmpeg 未配置、编码不匹配、格式不支持）；

- `06v2` 与 `06` 因名称不同会被视为不同条目，请酌情勾选。

## ❓ 常见问题

- **找不到 FFmpeg？** 确认其已加入 PATH 并重启，或手动选择 `ffmpeg.exe`。

- **字幕乱码？** 在「字幕选项」中切换编码格式后重新封装。

- **列表不显示？** 点击列表标题栏的刷新按钮重新载入。

- **打开时提示缺少 .NET 运行时？** 使用框架依赖版（framework-needed）时本机需安装 .NET 10 Desktop Runtime；缺失时系统会**弹窗提示安装**，安装后重启即可。也可改用自包含单文件版（click-to-run），无需安装任何运行时。

## 🤝鸣谢

字幕子集化工具来源于[AmusementClub/AssFontSubset](https://github.com/AmusementClub/AssFontSubset)，感谢**LoliHouse压制组**。

内置解压套件来源于[7-Zip](https://7-zip.org/)。

