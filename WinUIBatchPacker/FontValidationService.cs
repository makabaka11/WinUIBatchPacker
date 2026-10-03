using AssFontSubset.Core;

namespace WinUIBatchPacker;

internal sealed record FontValidationResult(int SubtitleCount, int FontCount,
    IReadOnlyList<string> Missing, IReadOnlyList<string> Problems)
{
    public bool HasIssues => Missing.Count > 0 || Problems.Count > 0;
    public string Summary
    {
        get
        {
            if (!HasIssues) return SubtitleCount == 0
                ? "没有需要字集化的 ASS/SSA 字幕。"
                : $"字体预检通过：{SubtitleCount} 个 ASS/SSA 字幕，扫描到 {FontCount} 个字体文件。";
            var details = new List<string>();
            if (Missing.Count > 0)
                details.Add($"缺少 {Missing.Count} 个字体：{string.Join("、", Missing.Take(8))}{(Missing.Count > 8 ? "…" : "")}");
            details.AddRange(Problems.Take(3));
            return string.Join("；", details);
        }
    }
}

internal static class FontValidationService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static async Task<FontValidationResult> CheckAsync(
        IReadOnlyList<string> subtitles, string fontSource, string encodingName,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        var work = Path.Combine(Path.GetTempPath(), "WinUIBatchPacker", "font-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            return await Task.Run(() => Check(subtitles, fontSource, encodingName, work), cancellationToken);
        }
        finally
        {
            try { await FontPackagingService.CleanupDirectoryAsync(work); }
            finally { Gate.Release(); }
        }
    }

    private static FontValidationResult Check(IReadOnlyList<string> subtitles,
        string fontSource, string encodingName, string work)
    {
        var problems = new List<string>();
        var missing = new HashSet<string>(StringComparer.Ordinal);
        string fontsDirectory;
        try { fontsDirectory = FontPackagingService.PrepareFonts(fontSource, work); }
        catch (Exception ex) { return new FontValidationResult(0, 0, [], ["字体来源读取失败：" + ex.Message]); }

        var styledFiles = FontPackagingService.PrepareStyledSubtitles(subtitles,
            Path.Combine(work, "subtitles"), encodingName);
        if (styledFiles.Length == 0)
            return new FontValidationResult(0, Directory.GetFiles(fontsDirectory).Length, [], []);

        List<FontInfo> fonts;
        try { fonts = FontParse.GetFontInfos(new DirectoryInfo(fontsDirectory)); }
        catch (Exception ex)
        {
            return new FontValidationResult(styledFiles.Length, 0, [], ["字体解析失败：" + ex.Message]);
        }
        var groups = fonts.GroupBy(fi => fi.FamilyNames[FontConstant.LanguageIdEnUs]).ToArray();
        foreach (var group in groups)
        {
            var duplicates = group.GroupBy(fi => new { fi.Bold, fi.Italic, fi.Weight, fi.Index, fi.MaxpNumGlyphs })
                .Where(g => g.Count() > 1);
            foreach (var duplicate in duplicates)
                problems.Add("重复字体：" + string.Join("、", duplicate.Select(x => Path.GetFileName(x.FileName))));
        }

        foreach (var file in styledFiles)
        {
            try
            {
                var required = AssFont.GetAssFonts(file, out _);
                foreach (var font in required.Keys)
                    if (!groups.Any(group => AssFont.GetMatchedFontInfo(font, group) is not null))
                        missing.Add(font.ToString());
            }
            catch (Exception ex) { problems.Add(Path.GetFileName(file) + " 解析失败：" + ex.Message); }
        }
        return new FontValidationResult(styledFiles.Length, fonts.Count,
            missing.Order(StringComparer.Ordinal).ToArray(), problems);
    }
}
