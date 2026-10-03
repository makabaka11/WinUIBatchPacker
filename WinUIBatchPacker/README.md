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

- **首次封装模式**：选择字体目录或 ZIP 压缩包。程序递归查找其中的 TTF、OTF、TTC、OTC 字体，ZIP 外层有包装目录也可识别。每集的 ASS/SSA 字幕使用该集实际用到的字形生成字体子集，改写后的字幕与字体附件一起封装为 MKV；非 UTF-8 ASS/SSA 会按“文件编码”选项读取并暂存为 UTF-8。SRT/VTT 保持原样。
- **仅补充字体模式**：只需指定存放 MKV 的文件夹，不用选择外部字幕或字体。每个 MKV 必须已有 ASS/SSA 字幕轨道和至少一个字体附件。程序临时提取这些字幕和字体，按每个 MKV 的字幕字形重新生成字体子集，替换原字体附件并写回字幕；其他音视频、字幕和非字体附件仍会保留。若缺少字幕或字体，该 MKV 跳过并记录错误。

两种模式都沿用输出文件夹和“封装成功后安全替换原视频”选项。临时字幕、字体和未完成的视频在处理后清理。

首次封装模式在选好字幕和字体来源后显示字体预检横幅，列出缺失或重复字体；开始封装前会再检查一次。预检警告不会禁止启动：缺字时尽量处理已有字体；字集化失败时改用原字幕和完整字体继续封装，日志会标明降级。仅补充字体模式在处理每个 MKV 时检查内嵌字体，无论检查通过或发现问题，都会在日志处暂停，输入 **Y** 继续当前项、**A** 后续决策全部继续、**N** 跳过当前项、**B** 后续决策全部跳过、**Q** 停止批次。检查通过时，继续会重新字集化并替换旧字体附件；字体不足时，继续会尝试处理可用字体并保留原字体附件；其他处理异常而继续时，原 MKV 保持不变（输出到新目录时复制原 MKV）。

此功能要求本机安装 Python 与 `fonttools`，可用 `python -m pip install fonttools` 安装。程序启动字体任务时会检测 Python、fontTools、`pyftsubset.exe` 和 `ttx.exe`；检测失败会在日志中提示。检测范围包括 PATH 和用户级 Python 安装目录 `AppData\Local\Programs\Python`。

启用字体时，输出文件扩展名为 `.mkv`。若勾选安全替换原视频，原 MKV 在封装成功后原位替换；非 MKV 原视频在同目录生成同名 MKV 后删除原文件。若该目标 MKV 已存在，则跳过该集。

`FontSubsetCore` 内含从同工作区 AssFontSubset.Core 引入的 PyFontTools 后端源码快照；本项目只使用这一后端。可运行 `dotnet run --project ../tests/FontIntegrationSmoke/FontIntegrationSmoke.csproj -c Release` 检查 ZIP、字集化、MKV 附件和仅补充字体流程（需本机 FFmpeg、fontTools 和 Arial 字体）。

可发布框架依赖 ZIP 或自包含单文件 EXE。自包含版首次启动时会自动把原生依赖释放到系统临时目录；字体功能仍需要本机 Python、fontTools 和 FFmpeg。

项目仅引用 WinUI 与 Runtime 组件，不引用 Windows App SDK 聚合包，因此不会携带本工具
用不到的 AI、ML、ONNX、DirectML 和 Widgets 组件。

## GitHub Actions 自动构建

提交中只要包含 `WinUIBatchPacker/**` 下的改动，`.github/workflows/winui-build.yml`
就会在 `windows-latest` 上自动执行 .NET 10 x64 发布。构建成功后，可在该次
Actions 运行页面下载框架依赖 ZIP 与自包含 EXE 两份 Artifact。

工作流也支持 Pull Request 和 Actions 页面的手动运行。
