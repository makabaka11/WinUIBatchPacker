using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssFontSubset.Core;

namespace WinUIBatchPacker;

internal static class EmbeddedFontService
{
    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".ttf", ".otf", ".ttc", ".otc" };

    private sealed record StreamInfo(int Index, string Type, string Codec, string Filename,
        string MimeType, string Disposition);

    public static async Task<(int Code, string Output, string Command)> ProcessAsync(
        string video, string target, string workDirectory, string fontsDirectory,
        FontToolsLocation tools, string ffmpegOption,
        Func<string, FontValidationResult, Task<IssueDecision>>? onFontIssue = null,
        Func<FontValidationResult, Task<IssueDecision>>? onFontsComplete = null)
    {
        if (!Path.GetExtension(video).Equals(".mkv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("仅补充字体模式只支持 MKV 文件。");
        var ffmpeg = string.IsNullOrWhiteSpace(ffmpegOption) ? "ffmpeg" : ffmpegOption;
        var ffprobe = Path.GetFileName(ffmpeg).Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe") : "ffprobe";
        var probe = await Run(ffprobe, ["-v", "error", "-show_streams", "-of", "json", video]);
        if (probe.Code != 0) throw new InvalidDataException("无法读取 MKV 轨道：" + probe.Output);
        var streams = ParseStreams(probe.Output);
        var styled = streams.Where(s => s.Type == "subtitle" && s.Codec is "ass" or "ssa").ToArray();
        if (styled.Length == 0) throw new InvalidDataException("MKV 中没有 ASS/SSA 字幕轨道，无法进行字体子集化。");
        var fontStreams = streams.Where(s => s.Type == "attachment" && IsFont(s)).ToArray();

        var extractedFonts = Path.Combine(workDirectory, "extracted-fonts");
        Directory.CreateDirectory(extractedFonts);
        var originalNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var attachmentOrdinal = 0;
        foreach (var stream in streams.Where(s => s.Type == "attachment"))
        {
            if (IsFont(stream))
            {
                var ext = FontExtensions.Contains(Path.GetExtension(stream.Filename))
                    ? Path.GetExtension(stream.Filename) : stream.Codec == "otf" ? ".otf" : ".ttf";
                var destination = Path.Combine(extractedFonts, $"font_{attachmentOrdinal:D3}{ext}");
                var extracted = await Run(ffmpeg,
                    ["-v", "error", "-y", $"-dump_attachment:t:{attachmentOrdinal}", destination,
                     "-i", video, "-map", "0:v:0", "-frames:v", "0", "-f", "null", "-"]);
                if (extracted.Code != 0 || !File.Exists(destination))
                    throw new InvalidDataException($"提取字体附件 #{stream.Index} 失败：{extracted.Output}");
                originalNames[destination] = stream.Filename;
            }
            attachmentOrdinal++;
        }

        var subtitlesDirectory = Path.Combine(workDirectory, "extracted-subtitles");
        Directory.CreateDirectory(subtitlesDirectory);
        var subtitles = new string[styled.Length];
        for (var i = 0; i < styled.Length; i++)
        {
            subtitles[i] = Path.Combine(subtitlesDirectory, $"subtitle_{i:D3}.ass");
            var extracted = await Run(ffmpeg,
                ["-v", "error", "-i", video, "-map", $"0:{styled[i].Index}",
                 "-c:s", "copy", "-y", subtitles[i]]);
            if (extracted.Code != 0 || !File.Exists(subtitles[i]))
                throw new InvalidDataException($"提取字幕轨道 #{styled[i].Index} 失败：{extracted.Output}");
        }

        var fontAliases = fontStreams.Length > 0
            ? await Task.Run(() => BuildFontAliases(fontsDirectory, extractedFonts, originalNames))
            : null;
        var sourceValidation = await FontValidationService.CheckAsync(subtitles, fontsDirectory, "UTF-8",
            fontAliases: fontAliases);
        var continueWithIssues = false;
        if (sourceValidation.HasIssues)
        {
            if (onFontIssue is null) throw new InvalidDataException(sourceValidation.Summary);
            var decision = await onFontIssue("所选字体来源", sourceValidation);
            if (decision != IssueDecision.Continue) throw new BatchControlException(decision);
            continueWithIssues = true;
        }
        if (fontStreams.Length > 0)
        {
            var existingValidation = await FontValidationService.CheckAsync(subtitles, extractedFonts, "UTF-8");
            if (existingValidation.HasIssues)
            {
                if (onFontIssue is null) throw new InvalidDataException(existingValidation.Summary);
                var decision = await onFontIssue("原 MKV 字体附件", existingValidation);
                if (decision != IssueDecision.Continue) throw new BatchControlException(decision);
            }
            else if (onFontsComplete is not null)
            {
                var decision = await onFontsComplete(existingValidation);
                if (decision != IssueDecision.Continue) throw new BatchControlException(decision);
            }
        }
        var subset = await FontPackagingService.SubsetEpisodeAsync(subtitles, fontsDirectory,
            Path.Combine(workDirectory, "episode"), tools, allowMissingFonts: continueWithIssues,
            fontAliases: fontAliases);
        if (subset.Fonts.Length == 0 && !(continueWithIssues && fontStreams.Length > 0))
            throw new InvalidDataException("未生成子集化字体，原视频未修改。");

        var args = new List<string> { "-i", video };
        foreach (var subtitle in subset.Subtitles) args.AddRange(["-i", subtitle]);
        var originalToNew = styled.Select((stream, index) => (stream.Index, Input: index + 1))
            .ToDictionary(pair => pair.Index, pair => pair.Input);
        var outputIndex = 0;
        var subtitleOrdinal = 0;
        var retainedAttachments = 0;
        foreach (var stream in streams)
        {
            if (stream.Type == "attachment" && IsFont(stream) && !continueWithIssues) continue;
            args.AddRange(["-map", originalToNew.TryGetValue(stream.Index, out var input)
                ? $"{input}:0" : $"0:{stream.Index}"]);
            args.AddRange([$"-map_metadata:s:{outputIndex}", $"0:s:{stream.Index}"]);
            if (input > 0)
            {
                args.AddRange([$"-disposition:s:{subtitleOrdinal}", stream.Disposition]);
            }
            if (stream.Type == "subtitle") subtitleOrdinal++;
            if (stream.Type == "attachment") retainedAttachments++;
            outputIndex++;
        }
        args.AddRange(["-map_metadata", "0", "-map_chapters", "0", "-c", "copy"]);
        for (var i = 0; i < subset.Fonts.Length; i++)
        {
            var font = subset.Fonts[i];
            args.AddRange(["-attach", font]);
            var mime = Path.GetExtension(font).Equals(".otf", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.ms-opentype" : "application/x-truetype-font";
            args.AddRange([$"-metadata:s:t:{retainedAttachments + i}", $"mimetype={mime}"]);
        }
        args.AddRange(["-y", target]);
        return await Run(ffmpeg, args);
    }

    private static bool IsFont(StreamInfo stream) =>
        FontExtensions.Contains(Path.GetExtension(stream.Filename)) ||
        stream.MimeType.Contains("font", StringComparison.OrdinalIgnoreCase) ||
        stream.Codec is "ttf" or "otf";

    private static Dictionary<string, string> BuildFontAliases(string sourceDirectory,
        string extractedDirectory, IReadOnlyDictionary<string, string> originalNames)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var sourceFaces = FontParse.GetFontInfos(new DirectoryInfo(sourceDirectory));
        var embeddedFaces = FontParse.GetFontInfos(new DirectoryInfo(extractedDirectory));
        foreach (var embedded in embeddedFaces)
        {
            if (!originalNames.TryGetValue(embedded.FileName, out var originalFileName)) continue;
            var source = sourceFaces.FirstOrDefault(face =>
                NormalizeFontFileName(face.FileName).Equals(NormalizeFontFileName(originalFileName), StringComparison.OrdinalIgnoreCase)
                && face.Index == embedded.Index);
            if (source.FamilyNames is null) continue;
            var originalFamily = source.FamilyNames[FontConstant.LanguageIdEnUs];
            foreach (var name in embedded.MatchNames ?? []) aliases.TryAdd(name, originalFamily);
        }
        return aliases;
    }

    private static string NormalizeFontFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
        name = Regex.Replace(name, @"^(?:\d{4}_)+", "");
        return Regex.Replace(name, @"\.0\.[A-Z0-9]{8}$", "", RegexOptions.IgnoreCase);
    }

    private static List<StreamInfo> ParseStreams(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<StreamInfo>();
        foreach (var item in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            var index = item.GetProperty("index").GetInt32();
            var type = GetString(item, "codec_type");
            var codec = GetString(item, "codec_name");
            var tags = item.TryGetProperty("tags", out var value) ? value : default;
            var filename = GetString(tags, "filename");
            var mime = GetString(tags, "mimetype");
            var disposition = "0";
            if (item.TryGetProperty("disposition", out value))
            {
                var enabled = value.EnumerateObject()
                    .Where(p => p.Value.ValueKind == JsonValueKind.Number && p.Value.GetInt32() == 1 &&
                        p.Name is not "attached_pic" and not "timed_thumbnails")
                    .Select(p => p.Name).ToArray();
                if (enabled.Length > 0) disposition = string.Join('+', enabled);
            }
            result.Add(new StreamInfo(index, type, codec, filename, mime, disposition));
        }
        return result.OrderBy(s => s.Index).ToList();
    }

    private static string GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return "";
        foreach (var property in element.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value.ToString();
        return "";
    }

    private static async Task<(int Code, string Output, string Command)> Run(string file, IReadOnlyList<string> args)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 " + file);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout + await stderr, string.Join(" ", args));
    }
}
