# 视频字幕批量封装工具（WinUI 3 版）

这是独立的 C#/.NET WinUI 3 实现，不会替换原 Python 版本。

## 构建环境

- Visual Studio 2022/2026，安装“.NET 桌面开发”和 Windows App SDK/WinUI 工作负载；或安装官方 WinApp CLI。
- .NET 10 SDK。
- Windows App SDK `1.8.260804001`（项目已锁定版本）。

当前仓库父目录含中文。若旧版 XAML 编译器异常退出，可把 `WinUIBatchPacker` 目录复制到纯英文路径后再构建。

## 构建

```powershell
dotnet restore
dotnet build -c Release -p:Platform=x64
```

## 字体配置

勾选“配置字体”后可选择两种模式：

- **首次封装模式**：选择字体目录或压缩包。程序递归查找其中的 TTF、OTF、TTC、OTC 字体，压缩包外层有包装目录也可识别。每集的 ASS/SSA 字幕使用该集实际用到的字形生成字体子集，改写后的字幕与字体附件一起封装为 MKV；非 UTF-8 ASS/SSA 会按“文件编码”选项读取并暂存为 UTF-8。SRT/VTT 保持原样。
- **仅补充字体模式**：选择存放 MKV 的文件夹，并配置字体目录或压缩包；无需选择外部字幕。MKV 只需已有 ASS/SSA 字幕轨道，可以完全没有字体附件。程序从 MKV 提取字幕，使用所选字体生成子集并加入 MKV；如果已有字体附件，会检查并按决策替换或保留。其他音视频、字幕和非字体附件仍会保留。若缺少 ASS/SSA 字幕，该 MKV 会记录错误。

压缩包支持 ZIP、7Z、RAR、TAR；由内置的 x64 `7z.dll` 直接解压，不调用外部解压程序。

两种模式都沿用输出文件夹和“封装成功后安全替换原视频”选项。临时字幕、字体和未完成的视频在处理后清理。

首次封装模式在选好字幕和字体来源后显示字体预检横幅，列出缺失或重复字体；开始封装前会再检查一次。预检警告不会禁止启动：缺字时尽量处理已有字体。子集化失败时会暂停并在日志区询问：**Y** 用原字幕与完整字体继续当前项，**A** 后续同类失败也这样处理，**N** 跳过当前项，**B** 后续同类失败全部跳过，**Q** 停止批次。仅补充字体模式在处理每个 MKV 时检查所选字体来源；若原 MKV 已有字体附件，也会核对它们，并在日志处提供 **Y** 继续当前项、**A** 后续决策全部继续、**N** 跳过当前项、**B** 后续决策全部跳过、**Q** 停止批次。原 MKV 没有字体附件且所选字体齐全时直接补充。原 MKV 字体已齐全时，选择继续会用所选字体重新子集化并替换旧附件；所选字体不足时继续会保留旧附件。其他处理异常而继续时，原 MKV 保持不变（输出到新目录时复制原 MKV）。

此功能要求本机安装 Python 与 `fonttools`，可用 `python -m pip install fonttools` 安装。程序启动字体任务时会检测 Python、fontTools、`pyftsubset.exe` 和 `ttx.exe`；检测失败会在日志中提示。检测范围包括 PATH 和用户级 Python 安装目录 `AppData\Local\Programs\Python`。

若个别字体的 `gasp` 表格式异常导致 fontTools 报 `AssertionError: too much data`，程序会仅对该字体去掉损坏的 `gasp` 表重试，原字体文件不会修改。其他外部工具失败会在日志中显示退出码和错误输出，再由用户决定是否改用完整字体、跳过或停止。

启用字体时，输出文件扩展名为 `.mkv`。若勾选安全替换原视频，原 MKV 在封装成功后原位替换；非 MKV 原视频在同目录生成同名 MKV 后删除原文件。若该目标 MKV 已存在，则跳过该集。

`FontSubsetCore` 内含从同工作区 AssFontSubset.Core 引入的 PyFontTools 后端源码快照；本项目只使用这一后端。可运行 `dotnet run --project ../tests/FontIntegrationSmoke/FontIntegrationSmoke.csproj -c Release` 检查 ZIP、7Z、子集化、MKV 附件和仅补充字体流程（需本机 FFmpeg、fontTools 和 Arial 字体）。

可发布框架依赖 ZIP 或自包含单文件 EXE。自包含版首次启动时会自动把原生依赖释放到系统临时目录；字体功能仍需要本机 Python、fontTools 和 FFmpeg。

项目仅引用 WinUI 与 Runtime 组件，不引用 Windows App SDK 聚合包，因此不会携带本工具
用不到的 AI、ML、ONNX、DirectML 和 Widgets 组件。

## GitHub Actions 自动构建

提交中只要包含 `WinUIBatchPacker/**` 下的改动，`.github/workflows/winui-build.yml`
就会在 `windows-latest` 上自动执行 .NET 10 x64 发布。构建成功后，可在该次
Actions 运行页面下载框架依赖 ZIP 与自包含 EXE 两份 Artifact。

工作流也支持 Pull Request 和 Actions 页面的手动运行。
