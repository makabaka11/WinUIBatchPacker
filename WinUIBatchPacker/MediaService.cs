using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WinUIBatchPacker;

public static partial class MediaService
{
    public static readonly string[] VideoExtensions = [".mkv", ".mp4", ".mov", ".avi", ".m4v", ".webm"];
    public static readonly string[] SubtitleExtensions = [".ass", ".ssa", ".srt", ".vtt"];

    private static readonly (int Score, Regex Pattern)[] Patterns =
    [
        (100, new(@"(?:^|[^A-Z0-9])S\d{1,2}[ ._-]*E(?:P)?[ ._-]*([0-9]{1,3}(?:\.\d+)?(?:v\d+)?)", RegexOptions.IgnoreCase)),
        (98, new(@"(?:^|[^A-Z0-9])(?:EP?|Episode|第)[ ._-]*([0-9]{1,3}(?:\.\d+)?(?:v\d+)?)(?:集|话|話)?(?:[^A-Z0-9]|$)", RegexOptions.IgnoreCase)),
        (96, new(@"#\s*([0-9]{1,3}(?:\.\d+)?(?:v\d+)?)\s*#", RegexOptions.IgnoreCase)),
        (94, new(@"(?:^|[\[ (._-])((?:OVA|OAD|SP|SPECIAL|NCOP|NCED)\d*)(?=$|[\] )._-])", RegexOptions.IgnoreCase)),
        (85, new(@"[\[(【]\s*([0-9]{1,3}(?:\.\d+)?(?:v\d+)?)\s*[\])】]", RegexOptions.IgnoreCase)),
        (65, new(@"(?<![A-Za-z0-9])([0-9]{1,3}(?:\.\d+)?(?:v\d+)?)(?![A-Za-z0-9])", RegexOptions.IgnoreCase))
    ];

    public static string ExtractEpisode(string path) => ExtractFromStem(Path.GetFileNameWithoutExtension(path));

    // 在"已去掉扩展名的文件名/分组 key"上直接提取集号，避免 .5 小数被当作扩展名截断
    private static string ExtractFromStem(string stem)
    {
        var candidates = new List<(int Score, string Episode)>();
        foreach (var (score, pattern) in Patterns)
        foreach (Match match in pattern.Matches(stem))
        {
            var raw = match.Groups[1].Value;
            var digits = Regex.Match(raw, @"^\d+");
            if (digits.Success)
            {
                var number = int.Parse(digits.Value);
                if (number >= 100 || number is >= 1900 and <= 2099) continue;
                if (score < 80)
                {
                    var start = Math.Max(0, match.Index - 12);
                    var around = stem.Substring(start, Math.Min(stem.Length - start, match.Length + 24));
                    if (Regex.IsMatch(around, $@"(?:x|h\.?)?{number}(?:p|i|bit|kbps)|(?:x|h)26[45]", RegexOptions.IgnoreCase)) continue;
                }
            }
            candidates.Add((score, NormalizeEpisode(raw)));
        }
        return candidates.OrderByDescending(x => x.Score).Select(x => x.Episode).FirstOrDefault() ?? "";
    }

    private static string NormalizeEpisode(string value)
    {
        value = value.Trim().ToUpperInvariant();
        var numeric = Regex.Match(value, @"^0*(\d+)(?:\.(\d+))?(?:V\d+)?$");
        if (numeric.Success)
        {
            var whole = int.Parse(numeric.Groups[1].Value).ToString();
            return numeric.Groups[2].Success ? whole + "." + numeric.Groups[2].Value : whole;
        }
        var special = Regex.Match(value, @"^(OVA|OAD|SP|SPECIAL|NCOP|NCED)0*(\d*)$");
        if (!special.Success) return value;
        var kind = special.Groups[1].Value == "SPECIAL" ? "SP" : special.Groups[1].Value;
        return kind + (special.Groups[2].Value.Length > 0 ? int.Parse(special.Groups[2].Value) : "");
    }

    public static (int Group, int Kind, double Number, string Text) EpisodeSortKey(string episode)
    {
        var numeric = Regex.Match(episode, @"^(\d+)(?:\.(\d+))?$");
        if (numeric.Success)
        {
            var value = double.Parse(numeric.Groups[1].Value + (numeric.Groups[2].Success ? "." + numeric.Groups[2].Value : ""), System.Globalization.CultureInfo.InvariantCulture);
            return (0, 0, value, episode);
        }
        var special = Regex.Match(episode, @"^(SP|OVA|OAD|NCOP|NCED)(\d*)$", RegexOptions.IgnoreCase);
        if (!special.Success) return (2, 0, 0, episode);
        string[] order = ["SP", "OVA", "OAD", "NCOP", "NCED"];
        return (1, Array.IndexOf(order, special.Groups[1].Value.ToUpperInvariant()), special.Groups[2].Value.Length > 0 ? double.Parse(special.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : 0, episode);
    }

    public static List<MediaRow> LoadVideos(string folder) => Directory.Exists(folder)
        ? Directory.EnumerateFiles(folder).Where(p => VideoExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .Select(p => new MediaRow { DisplayName = Path.GetFileName(p), Paths = [p], Episode = ExtractEpisode(p) })
            .OrderBy(x => EpisodeSortKey(x.Episode)).ToList() : [];

    public static List<MediaRow> LoadSubtitleGroups(string folder) => Directory.Exists(folder)
        ? Directory.EnumerateFiles(folder).Where(p => SubtitleExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .GroupBy(SubtitleGroupKey)
            .Select(g =>
            {
                var ep = ExtractFromStem(g.Key);
                return new MediaRow { DisplayName = string.Join("  +  ", g.Select(Path.GetFileName)), Paths = g.ToList(), Episode = ep.Length > 0 ? ep : "" };
            })
            .OrderBy(x => EpisodeSortKey(x.Episode)).ToList() : [];

    // 语言后缀列表：出现在文件名的最后一个点之后才视为语言后缀（如 Show.06.zh.ass 中的 zh）
    private static readonly Regex LanguageSuffix = new(@"[.\-_](?:zh[.\-_]?(?:cn|hans|hant|tw|hk|sc|tc|chs|cht)|\bzh\b|chs|cht|sc|tc|gb|big5|en|eng|english|jpn|ja|jp|ko|kor|kr|fr|fra|de|ger|es|spa|pt|por|ru|rus|it|ita|th|tha|vi|vie|fr|ar|ara|id|ms|fil|tr|pl|nl|el|hu|cs|cz|da|sv|fi|no|uk|he|hi|bn|zh-Hans|zh-Hant)$", RegexOptions.IgnoreCase);

    // 组合式语言段：如 Nekoake 组的 .JPSC(简) / .JPTC(繁)，末尾 . 段整体为 "字母+SC/TC/CHS/CHT" 时视为语言标记
    private static readonly Regex ComboLanguageSuffix = new(@"[.\-_]([A-Za-z]{1,4}(?:SC|TC|CHS|CHT))$", RegexOptions.IgnoreCase);

    // 分组 key：去掉扩展名和末尾语言后缀，仅保留主体 + 集号，保证仅语言不同的字幕归为同一组
    private static string SubtitleGroupKey(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var s = LanguageSuffix.Replace(name, "");
        var m = ComboLanguageSuffix.Match(s);
        if (m.Success) s = s.Substring(0, m.Index);
        return s;
    }

    // 组合语言标记判定：JPSC=简体，JPTC=繁体（Nekoake 等组的多语言命名）
    private static (bool IsChinese, bool IsTraditional) ComboLanguage(string token)
    {
        var m = Regex.Match(token, @"^(?:[a-zA-Z]{1,4})?(SC|TC|CHS|CHT)$", RegexOptions.IgnoreCase);
        if (!m.Success) return (false, false);
        return (true, Regex.IsMatch(m.Groups[1].Value, @"^(?:TC|CHT)$", RegexOptions.IgnoreCase));
    }

    public static IReadOnlyList<string> Deduplicate(IReadOnlyList<string> paths)
    {
        var seen = new HashSet<string>(); var result = new List<string>();
        foreach (var path in paths)
        {
            using var stream = File.OpenRead(path);
            var key = stream.Length + ":" + Convert.ToHexString(SHA256.HashData(stream));
            if (seen.Add(key)) result.Add(path);
        }
        return result;
    }

    public static (string Code, string Title) SubtitleLanguage(string path, string fallback)
    {
        var tokens = Regex.Split(Path.GetFileNameWithoutExtension(path).ToLowerInvariant(), @"[^a-z0-9]+");
        // 组合标记：JPSC/CHS…=简体，JPTC/CHT…=繁体
        foreach (var t in tokens)
        {
            var (isChinese, isTrad) = ComboLanguage(t);
            if (isChinese) return isTrad ? ("chi", "繁体中文") : ("chi", "简体中文");
        }
        if (tokens.Any(x => new[] { "sc", "chs", "zhcn", "zhs", "gb", "simp", "simplified" }.Contains(x))) return ("chi", "简体中文");
        if (tokens.Any(x => new[] { "tc", "cht", "zhtw", "zht", "big5", "trad", "traditional" }.Contains(x))) return ("chi", "繁体中文");
        if (tokens.Any(x => new[] { "en", "eng", "english" }.Contains(x))) return ("eng", "English");
        return fallback == "英语" ? ("eng", "English") : ("chi", fallback);
    }

    public static async Task<int> CountSubtitleStreams(string video, string ffprobe)
    {
        var result = await Run(ffprobe, ["-v", "error", "-select_streams", "s", "-show_entries", "stream=index", "-of", "csv=p=0", video]);
        return result.Code == 0 ? result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length : 0;
    }

    public static async Task<(int Code, string Output, string Command)> Pack(string video, IReadOnlyList<string> subtitles, string target, PackOptions options, IReadOnlyList<string>? fonts = null, IReadOnlyList<string>? originalSubtitles = null)
    {
        fonts ??= [];
        originalSubtitles ??= subtitles;
        if (originalSubtitles.Count != subtitles.Count) throw new ArgumentException("字幕路径数量不一致");
        var ffmpeg = string.IsNullOrWhiteSpace(options.Ffmpeg) ? "ffmpeg" : options.Ffmpeg;
        var ffprobe = Path.GetFileName(ffmpeg).Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) ? Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe") : "ffprobe";
        var existing = await CountSubtitleStreams(video, ffprobe);
        var existingAttachments = fonts.Count > 0 ? await CountAttachmentStreams(video, ffprobe) : 0;
        var args = new List<string> { "-i", video };
        foreach (var subtitle in subtitles)
        {
            if ((Path.GetExtension(subtitle).Equals(".ass", StringComparison.OrdinalIgnoreCase) ||
                 Path.GetExtension(subtitle).Equals(".ssa", StringComparison.OrdinalIgnoreCase)) &&
                !originalSubtitles.Contains(subtitle)) args.AddRange(["-i", subtitle]);
            else args.AddRange(["-sub_charenc", options.Encoding, "-i", subtitle]);
        }
        args.AddRange(["-map", "0"]);
        for (var i = 0; i < subtitles.Count; i++) args.AddRange(["-map", $"{i + 1}:0"]);
        args.AddRange(["-map_metadata", "0", "-map_chapters", "0", "-c", "copy"]);
        for (var i = 0; i < subtitles.Count; i++)
        {
            var (code, title) = SubtitleLanguage(originalSubtitles[i], options.FallbackLanguage);
            args.AddRange([$"-metadata:s:s:{existing + i}", $"language={code}", $"-metadata:s:s:{existing + i}", $"title={title}"]);
        }
        if (options.DefaultSubtitle && subtitles.Count > 0) args.AddRange(["-disposition:s", "0", $"-disposition:s:{existing}", "default"]);
        for (var i = 0; i < fonts.Count; i++)
        {
            args.AddRange(["-attach", fonts[i]]);
            var mime = Path.GetExtension(fonts[i]).Equals(".otf", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.ms-opentype" : "application/x-truetype-font";
            args.AddRange([$"-metadata:s:t:{existingAttachments + i}", $"mimetype={mime}"]);
        }
        args.AddRange(["-y", target]);
        return await Run(ffmpeg, args, string.Join(" ", args));
    }

    private static async Task<int> CountAttachmentStreams(string video, string ffprobe)
    {
        var result = await Run(ffprobe, ["-v", "error", "-select_streams", "t", "-show_entries", "stream=index", "-of", "csv=p=0", video]);
        if (result.Code != 0) throw new InvalidOperationException("无法读取原视频的附件轨道：" + result.Output);
        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public static string? CommitOutput(string video, string final, string temporary, bool replace)
    {
        if (!replace)
        {
            File.Move(temporary, final, true);
            return null;
        }
        if (Path.GetFullPath(video).Equals(Path.GetFullPath(final), StringComparison.OrdinalIgnoreCase))
        {
            var backup = video + ".__backup_" + Guid.NewGuid().ToString("N");
            File.Replace(temporary, video, backup);
            try { File.Delete(backup); }
            catch (Exception ex) { return "备份文件清理失败：" + ex.Message; }
            return null;
        }

        // For MP4 and other non-MKV sources, keep the original until the MKV is in place.
        File.Move(temporary, final);
        try { File.Delete(video); }
        catch (Exception ex) { return "新 MKV 已生成，但原视频未删除：" + ex.Message; }
        return null;
    }

    private static async Task<(int Code, string Output, string Command)> Run(string file, IEnumerable<string> args, string commandOverride = "")
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        var command = string.IsNullOrEmpty(commandOverride) ? $"{file} {string.Join(" ", args)}" : commandOverride;
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"无法启动 {file}");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, (await stdout) + (await stderr), command);
    }
}
