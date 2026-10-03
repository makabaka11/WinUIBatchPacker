using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AssFontSubset.Core;
using SevenZipExtractor;

namespace WinUIBatchPacker;

internal static class FontPackagingService
{
    public static readonly string[] ArchiveExtensions = [".zip", ".7z", ".rar", ".tar"];
    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".ttf", ".otf", ".ttc", ".otc" };
    private static bool IsStyledSubtitle(string path) =>
        Path.GetExtension(path) is var ext &&
        (ext.Equals(".ass", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ssa", StringComparison.OrdinalIgnoreCase));

    public static async Task<(FontToolsLocation? Location, string Warning)> DetectFontToolsAsync()
    {
        var pythonFound = false;
        foreach (var python in PythonCandidates())
        {
            if (!await Succeeds(python, ["-c", "import sys; print(sys.executable)"])) continue;
            pythonFound = true;
            if (!await Succeeds(python, ["-c", "import fontTools; print(fontTools.__version__)"])) continue;
            var scripts = Path.Combine(Path.GetDirectoryName(python)!, "Scripts");
            if (File.Exists(Path.Combine(scripts, "pyftsubset.exe")) &&
                File.Exists(Path.Combine(scripts, "ttx.exe")))
                return (new FontToolsLocation(python, scripts), "");
        }

        return (null, pythonFound
            ? "未检测到可用的 Python fontTools 命令。请在 Python 环境执行 python -m pip install fonttools，并确保 pyftsubset.exe、ttx.exe 位于 Scripts 目录。"
            : "未检测到 Python。请先安装 Python，再执行 python -m pip install fonttools。");
    }

    private static IEnumerable<string> PythonCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim('"'), "python.exe");
            if (File.Exists(candidate) && seen.Add(candidate)) yield return candidate;
        }

        var localPython = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python");
        if (Directory.Exists(localPython))
        {
            foreach (var dir in Directory.EnumerateDirectories(localPython, "Python*").OrderDescending())
            {
                var candidate = Path.Combine(dir, "python.exe");
                if (File.Exists(candidate) && seen.Add(candidate)) yield return candidate;
            }
        }
    }

    private static async Task<bool> Succeeds(string file, IEnumerable<string> args)
    {
        try
        {
            var start = new ProcessStartInfo(file)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process is null) return false;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); return false; }
            await Task.WhenAll(output, error);
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    public static string PrepareFonts(string source, string workDirectory)
    {
        var input = source;
        if (File.Exists(source))
        {
            if (!ArchiveExtensions.Contains(Path.GetExtension(source), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("不支持的字体压缩包格式：" + Path.GetExtension(source));
            for (var depth = 0; depth < 3; depth++)
            {
                var extracted = Path.Combine(workDirectory, $"extracted-{depth}");
                ExtractArchive(input, extracted, workDirectory);
                input = extracted;
                var entries = Directory.EnumerateFiles(input, "*", new EnumerationOptions
                    { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToArray();
                if (entries.Any(file => FontExtensions.Contains(Path.GetExtension(file)))) break;
                if (entries.Length != 1 || !ArchiveExtensions.Contains(Path.GetExtension(entries[0]), StringComparer.OrdinalIgnoreCase)) break;
                if (depth == 2) break;
                input = entries[0];
            }
        }
        if (!Directory.Exists(input)) throw new DirectoryNotFoundException("字体目录或压缩包不存在：" + source);

        // 扫描所有层级，同时跳过压缩包内可能包含的重解析点。
        var files = Directory.EnumerateFiles(input, "*", new EnumerationOptions
            { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Where(file => FontExtensions.Contains(Path.GetExtension(file))).Order().ToArray();
        if (files.Length == 0) throw new InvalidDataException("所选目录或压缩包中没有 TTF/OTF/TTC/OTC 字体。");

        var flat = Path.Combine(workDirectory, "fonts");
        Directory.CreateDirectory(flat);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < files.Length; i++)
        {
            using var stream = File.OpenRead(files[i]);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            if (seen.Add(hash)) File.Copy(files[i], Path.Combine(flat, $"{i:D4}_{Path.GetFileName(files[i])}"));
        }
        return flat;
    }

    private static void ExtractArchive(string archive, string destination, string workDirectory)
    {
        Directory.CreateDirectory(destination);
        var toolDirectory = Path.Combine(workDirectory, "7zip-tools");
        Directory.CreateDirectory(toolDirectory);
        var dllPath = Path.Combine(toolDirectory, "7z.dll");
        if (!File.Exists(dllPath))
        {
            using var resource = typeof(FontPackagingService).Assembly
                .GetManifestResourceStream("WinUIBatchPacker.7z.dll")
                ?? throw new FileNotFoundException("未找到内置 7z.dll。", dllPath);
            using var output = File.Create(dllPath);
            resource.CopyTo(output);
        }
        try
        {
            using var file = new ArchiveFile(Path.GetFullPath(archive), dllPath);
            var entries = file.Entries.Where(entry => !entry.IsFolder).ToArray();
            var selected = entries.Where(entry => FontExtensions.Contains(Path.GetExtension(entry.FileName))).ToArray();
            if (selected.Length == 0 && entries.Length == 1 &&
                ArchiveExtensions.Contains(Path.GetExtension(entries[0].FileName), StringComparer.OrdinalIgnoreCase))
                selected = entries;
            if (selected.Length == 0)
                throw new InvalidDataException("压缩包中没有可用字体；条目：" +
                    string.Join("、", entries.Take(5).Select(entry => entry.FileName)));

            var paths = new Dictionary<Entry, string>();
            for (var i = 0; i < selected.Length; i++)
            {
                var safeName = Path.GetFileName(selected[i].FileName.Replace('\\', '/'));
                if (string.IsNullOrWhiteSpace(safeName)) throw new InvalidDataException("压缩包条目缺少文件名。");
                paths[selected[i]] = Path.Combine(destination, $"{i:D4}_{safeName}");
            }
            file.Extract(entry => paths.GetValueOrDefault(entry)!);
        }
        catch (SevenZipException ex) { throw new InvalidDataException("7z.dll 解压失败：" + ex.Message, ex); }
    }

    public static async Task<(string[] Subtitles, string[] Fonts)> SubsetEpisodeAsync(
        IReadOnlyList<string> subtitles, string fontsDirectory, string workDirectory, FontToolsLocation tools,
        string encodingName = "UTF-8", bool allowMissingFonts = false,
        IReadOnlyDictionary<string, string>? fontAliases = null)
    {
        var styledFiles = PrepareStyledSubtitles(subtitles, workDirectory, encodingName);
        if (styledFiles.Length == 0) return (subtitles.ToArray(), []);

        var assFiles = styledFiles.Select(p => new FileInfo(p)).ToArray();

        var output = Path.Combine(workDirectory, "subset");
        var config = new SubsetConfig { Backend = SubsetBackend.PyFontTools, SourceHanEllipsis = true,
            AllowMissingFonts = allowMissingFonts, FontAliases = fontAliases };
        await new SubsetCore().SubsetAsync(assFiles, new DirectoryInfo(fontsDirectory),
            new DirectoryInfo(output), new DirectoryInfo(tools.BinDirectory), config);

        var rewritten = subtitles.Select(p => IsStyledSubtitle(p)
            ? Path.Combine(output, Path.GetFileName(p)) : p).ToArray();
        foreach (var file in rewritten.Where(p => p.StartsWith(output, StringComparison.OrdinalIgnoreCase)))
            if (!File.Exists(file)) throw new FileNotFoundException("字集化后的字幕未生成", file);
        var fonts = Directory.EnumerateFiles(output)
            .Where(p => FontExtensions.Contains(Path.GetExtension(p))).Order().ToArray();
        return (rewritten, fonts);
    }

    public static string[] PrepareStyledSubtitles(IReadOnlyList<string> subtitles, string workDirectory, string encodingName)
    {
        var styledFiles = subtitles.Where(IsStyledSubtitle).ToArray();
        if (styledFiles.Length == 0) return styledFiles;

        if (!encodingName.Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var sourceEncoding = Encoding.GetEncoding(encodingName,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            var normalized = Path.Combine(workDirectory, "normalized");
            Directory.CreateDirectory(normalized);
            styledFiles = styledFiles.Select(p =>
            {
                var target = Path.Combine(normalized, Path.GetFileName(p));
                File.WriteAllText(target, File.ReadAllText(p, sourceEncoding), new UTF8Encoding(false));
                return target;
            }).ToArray();
        }
        return styledFiles;
    }

    public static async Task CleanupDirectoryAsync(string path)
    {
        if (!Directory.Exists(path)) return;
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Delete(path, true); return; }
            catch (IOException) when (attempt < 4)
            {
                // Mobsub.Font may release a parsed font stream through a finalizer.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(250 * (attempt + 1));
            }
        }
    }
}
