using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WinUIBatchPacker;

if (args.Length == 3 && args[0] == "--real")
{
    await RunRealFixture(args[1], args[2]);
    return;
}

var work = Path.Combine(Path.GetTempPath(), "WinUIBatchPacker-Smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
try
{
    var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
    if (!File.Exists(font)) throw new FileNotFoundException("Arial fixture font not found", font);
    var zip = Path.Combine(work, "fonts.zip");
    using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
    {
        archive.CreateEntryFromFile(font, "wrapper/fonts/arial.ttf");
        archive.CreateEntryFromFile(font, "wrapper/duplicates/arial-copy.ttf");
    }
    var stagedFonts = FontPackagingService.PrepareFonts(zip, Path.Combine(work, "stage"));
    if (Directory.GetFiles(stagedFonts).Length != 1) throw new Exception("Nested ZIP font detection failed");
    var sevenZip = Path.GetFullPath("tests/FontIntegrationSmoke/Fixtures/font-sample.7z");
    var staged7z = FontPackagingService.PrepareFonts(sevenZip, Path.Combine(work, "stage-7z"));
    if (Directory.GetFiles(staged7z).Length != 1) throw new Exception("Bundled 7-Zip font extraction failed");
    var tarSource = Path.Combine(work, "tar-fonts");
    Directory.CreateDirectory(tarSource);
    File.Copy(font, Path.Combine(tarSource, "arial.ttf"));
    var tarArchive = Path.Combine(work, "fonts.tar");
    await Capture("tar", ["-cf", tarArchive, "-C", tarSource, "arial.ttf"]);
    var stagedTar = FontPackagingService.PrepareFonts(tarArchive, Path.Combine(work, "stage-tar"));
    if (Directory.GetFiles(stagedTar).Length != 1) throw new Exception("TAR font extraction failed");

    var detection = await FontPackagingService.DetectFontToolsAsync();
    if (detection.Location is null) throw new Exception(detection.Warning);

    var ass = Path.Combine(work, "episode.zh.ass");
    await File.WriteAllTextAsync(ass, """
        [Script Info]
        Title: Smoke test
        ScriptType: v4.00+
        PlayResX: 640
        PlayResY: 360

        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
        Style: Default,Arial,24,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1

        [Events]
        Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
        Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,Hello font subset
        """);

    var checkedFonts = await FontValidationService.CheckAsync([ass], stagedFonts, "UTF-8");
    if (checkedFonts.HasIssues) throw new Exception("Font preflight should pass: " + checkedFonts.Summary);
    var missingAss = Path.Combine(work, "missing-font.ass");
    File.WriteAllText(missingAss, (await File.ReadAllTextAsync(ass)).Replace("Default,Arial,", "Default,MissingFont,"));
    var missingCheck = await FontValidationService.CheckAsync([missingAss], stagedFonts, "UTF-8");
    if (missingCheck.Missing.Count == 0) throw new Exception("Font preflight should report a missing font");

    var prepared = await FontPackagingService.SubsetEpisodeAsync([ass], stagedFonts, Path.Combine(work, "episode"), detection.Location);
    if (prepared.Fonts.Length == 0) throw new Exception("No subset font generated");
    var rewritten = await File.ReadAllTextAsync(prepared.Subtitles[0]);
    if (rewritten.Contains("Style: Default,Arial,")) throw new Exception("ASS font name was not rewritten");

    var ssa = Path.Combine(work, "episode.tc.ssa");
    File.Copy(ass, ssa);
    var preparedSsa = await FontPackagingService.SubsetEpisodeAsync([ssa], stagedFonts, Path.Combine(work, "episode-ssa"), detection.Location);
    if (preparedSsa.Fonts.Length == 0 || !File.Exists(preparedSsa.Subtitles[0]))
        throw new Exception("SSA font subset failed");

    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var gbk = Path.Combine(work, "episode.gbk.ass");
    File.WriteAllText(gbk, (await File.ReadAllTextAsync(ass)).Replace("Hello font subset", "café"), Encoding.GetEncoding("gbk"));
    var preparedGbk = await FontPackagingService.SubsetEpisodeAsync([gbk], stagedFonts,
        Path.Combine(work, "episode-gbk"), detection.Location, "gbk");
    if (!File.Exists(preparedGbk.Subtitles[0]) || preparedGbk.Fonts.Length == 0)
        throw new Exception("GBK ASS font subset failed");

    var video = Path.Combine(work, "video.mp4");
    await Run("ffmpeg", ["-v", "error", "-f", "lavfi", "-i", "color=c=black:s=640x360:d=1", "-c:v", "mpeg4", "-y", video]);
    var forced = await FontPackagingService.SubsetEpisodeAsync([missingAss], stagedFonts,
        Path.Combine(work, "forced-first"), detection.Location, allowMissingFonts: true);
    var forcedOutput = Path.Combine(work, "forced-first.mkv");
    var forcedPack = await MediaService.Pack(video, forced.Subtitles, forcedOutput,
        new PackOptions("ffmpeg", "UTF-8", "简体中文", true, false, work), forced.Fonts, [missingAss]);
    if (forcedPack.Code != 0 || !File.Exists(forcedOutput))
        throw new Exception("First mode should allow forced packaging with a missing font: " + forcedPack.Output);
    var mkv = Path.Combine(work, "video.mkv");
    var packed = await MediaService.Pack(video, prepared.Subtitles, mkv,
        new PackOptions("ffmpeg", "UTF-8", "简体中文", true, false, work), prepared.Fonts, [ass]);
    if (packed.Code != 0) throw new Exception(packed.Output);
    var probe = await Capture("ffprobe", ["-v", "error", "-show_entries", "stream=codec_type:stream_tags=mimetype,filename", "-of", "csv=p=0", mkv]);
    if (!probe.Contains("subtitle") || !probe.Contains("attachment") || !probe.Contains("application/"))
        throw new Exception("MKV subtitle/attachment verification failed: " + probe);

    var replacementSource = Path.Combine(work, "replace.mp4");
    File.Copy(video, replacementSource);
    var replacementMkv = Path.Combine(work, "replace.mkv");
    var warning = MediaService.CommitOutput(replacementSource, replacementMkv, mkv, true);
    if (warning is not null || File.Exists(replacementSource) || !File.Exists(replacementMkv))
        throw new Exception("Non-MKV safe replacement failed: " + warning);
    var secondTemp = Path.Combine(work, "second.mkv");
    File.Copy(replacementMkv, secondTemp);
    warning = MediaService.CommitOutput(replacementMkv, replacementMkv, secondTemp, true);
    if (warning is not null || !File.Exists(replacementMkv) || File.Exists(secondTemp))
        throw new Exception("MKV safe replacement failed: " + warning);

    var remuxed = Path.Combine(work, "with-existing-attachment.mkv");
    var secondPack = await MediaService.Pack(replacementMkv, prepared.Subtitles, remuxed,
        new PackOptions("ffmpeg", "UTF-8", "简体中文", true, false, work), prepared.Fonts, [ass]);
    if (secondPack.Code != 0) throw new Exception(secondPack.Output);
    var attachments = await Capture("ffprobe", ["-v", "error", "-select_streams", "t", "-show_entries", "stream_tags=mimetype", "-of", "csv=p=0", remuxed]);
    if (attachments.Split('\n', StringSplitOptions.RemoveEmptyEntries).Count(x => x.Contains("application/")) != 2)
        throw new Exception("Existing/new attachment metadata failed: " + attachments);

    var originalSubtitleMkv = Path.Combine(work, "original-subtitle-font.mkv");
    await Capture("ffmpeg", ["-v", "error", "-i", video, "-i", ass,
        "-map", "0", "-map", "1:0", "-c", "copy",
        "-metadata:s:s:0", "language=chi",
        "-attach", font, "-metadata:s:t:0", "mimetype=application/x-truetype-font",
        "-y", originalSubtitleMkv]);
    var supplemented = Path.Combine(work, "supplemented.mkv");
    var completeDecisionCount = 0;
    var supplementalResult = await EmbeddedFontService.ProcessAsync(originalSubtitleMkv, supplemented,
        Path.Combine(work, "supplement-work"), stagedFonts, detection.Location, "ffmpeg", onFontsComplete: check =>
        {
            if (check.HasIssues) throw new Exception("Complete font callback received issues");
            completeDecisionCount++;
            return Task.FromResult(IssueDecision.Continue);
        });
    if (supplementalResult.Code != 0) throw new Exception(supplementalResult.Output);
    if (completeDecisionCount != 1) throw new Exception("Complete font decision was not requested");
    var supplementProbe = await Capture("ffprobe", ["-v", "error", "-show_entries",
        "stream=codec_type:stream_tags=language,title,mimetype", "-of", "csv=p=0", supplemented]);
    if (supplementProbe.Split('\n').Count(x => x.Contains("attachment")) != 1 ||
        supplementProbe.Split('\n').Count(x => x.Contains("subtitle")) != 1 ||
        !supplementProbe.Contains("chi"))
        throw new Exception("Supplement mode stream verification failed: " + supplementProbe);
    var subtitleOnly = Path.Combine(work, "subtitle-only.mkv");
    await Capture("ffmpeg", ["-v", "error", "-i", video, "-i", ass,
        "-map", "0", "-map", "1:0", "-c", "copy", "-y", subtitleOnly]);
    var newlyAttached = Path.Combine(work, "newly-attached.mkv");
    var noAttachmentPrompted = false;
    var newlyAttachedResult = await EmbeddedFontService.ProcessAsync(subtitleOnly, newlyAttached,
        Path.Combine(work, "no-attachment-work"), stagedFonts, detection.Location, "ffmpeg",
        (_, _) => { noAttachmentPrompted = true; return Task.FromResult(IssueDecision.Stop); },
        _ => { noAttachmentPrompted = true; return Task.FromResult(IssueDecision.Stop); });
    if (newlyAttachedResult.Code != 0 || noAttachmentPrompted)
        throw new Exception("Subtitle-only MKV should gain fonts without an existing-font prompt: " + newlyAttachedResult.Output);
    var newAttachmentProbe = await Capture("ffprobe", ["-v", "error", "-select_streams", "t",
        "-show_entries", "stream=index", "-of", "csv=p=0", newlyAttached]);
    if (newAttachmentProbe.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length != 1)
        throw new Exception("Subtitle-only MKV did not gain a font attachment");
    var completeSkip = Path.Combine(work, "complete-skip.mkv");
    try
    {
        await EmbeddedFontService.ProcessAsync(originalSubtitleMkv, completeSkip,
            Path.Combine(work, "complete-skip-work"), stagedFonts, detection.Location, "ffmpeg",
            onFontsComplete: _ => Task.FromResult(IssueDecision.Skip));
        throw new Exception("Complete-font skip decision was ignored");
    }
    catch (BatchControlException ex) when (ex.Decision == IssueDecision.Skip) { }
    if (File.Exists(completeSkip)) throw new Exception("Complete-font skip produced output");
    var completeStop = Path.Combine(work, "complete-stop.mkv");
    try
    {
        await EmbeddedFontService.ProcessAsync(originalSubtitleMkv, completeStop,
            Path.Combine(work, "complete-stop-work"), stagedFonts, detection.Location, "ffmpeg",
            onFontsComplete: _ => Task.FromResult(IssueDecision.Stop));
        throw new Exception("Complete-font stop decision was ignored");
    }
    catch (BatchControlException ex) when (ex.Decision == IssueDecision.Stop) { }
    if (File.Exists(completeStop)) throw new Exception("Complete-font stop produced output");

    var boldFont = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arialbd.ttf");
    var note = Path.Combine(work, "note.txt");
    await File.WriteAllTextAsync(note, "keep this attachment");
    var multiSource = Path.Combine(work, "multi-source.mkv");
    await Capture("ffmpeg", ["-v", "error", "-i", video, "-i", ass, "-i", ssa,
        "-map", "0", "-map", "1:0", "-map", "2:0", "-c", "copy",
        "-metadata:s:s:0", "language=chi", "-metadata:s:s:1", "language=eng",
        "-disposition:s:0", "default", "-disposition:s:1", "0",
        "-attach", font, "-metadata:s:t:0", "mimetype=application/x-truetype-font",
        "-attach", boldFont, "-metadata:s:t:1", "mimetype=application/x-truetype-font",
        "-attach", note, "-metadata:s:t:2", "mimetype=text/plain", "-y", multiSource]);
    var multiOutput = Path.Combine(work, "multi-output.mkv");
    var multiResult = await EmbeddedFontService.ProcessAsync(multiSource, multiOutput,
        Path.Combine(work, "multi-work"), stagedFonts, detection.Location, "ffmpeg");
    if (multiResult.Code != 0) throw new Exception(multiResult.Output);
    var multiProbe = await Capture("ffprobe", ["-v", "error", "-show_entries",
        "stream=codec_type:stream_tags=language,mimetype", "-of", "csv=p=0", multiOutput]);
    if (multiProbe.Split('\n').Count(x => x.Contains("subtitle")) != 2 ||
        multiProbe.Split('\n').Count(x => x.Contains("attachment")) != 2 ||
        !multiProbe.Contains("chi") || !multiProbe.Contains("eng") || !multiProbe.Contains("text/plain"))
        throw new Exception("Multiple subtitle/font and non-font attachment verification failed: " + multiProbe);
    var dispositionJson = await Capture("ffprobe", ["-v", "error", "-select_streams", "s",
        "-show_entries", "stream_disposition=default", "-of", "json", multiOutput]);
    using (var dispositions = JsonDocument.Parse(dispositionJson))
    {
        var subtitleStreams = dispositions.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (subtitleStreams.Length != 2 ||
            subtitleStreams[0].GetProperty("disposition").GetProperty("default").GetInt32() != 1 ||
            subtitleStreams[1].GetProperty("disposition").GetProperty("default").GetInt32() != 0)
            throw new Exception("Subtitle default disposition was not preserved: " + dispositionJson);
    }

    var incompleteSource = Path.Combine(work, "incomplete.mkv");
    await Capture("ffmpeg", ["-v", "error", "-i", video, "-i", missingAss,
        "-map", "0", "-map", "1:0", "-c", "copy", "-attach", font,
        "-metadata:s:t:0", "mimetype=application/x-truetype-font", "-y", incompleteSource]);
    try
    {
        await EmbeddedFontService.ProcessAsync(incompleteSource, Path.Combine(work, "skip.mkv"),
            Path.Combine(work, "skip-work"), stagedFonts, detection.Location, "ffmpeg",
            (_, _) => Task.FromResult(IssueDecision.Skip));
        throw new Exception("Skip decision was ignored");
    }
    catch (BatchControlException ex) when (ex.Decision == IssueDecision.Skip) { }
    try
    {
        await EmbeddedFontService.ProcessAsync(incompleteSource, Path.Combine(work, "stop.mkv"),
            Path.Combine(work, "stop-work"), stagedFonts, detection.Location, "ffmpeg",
            (_, _) => Task.FromResult(IssueDecision.Stop));
        throw new Exception("Stop decision was ignored");
    }
    catch (BatchControlException ex) when (ex.Decision == IssueDecision.Stop) { }
    var continued = Path.Combine(work, "continued.mkv");
    var continuedResult = await EmbeddedFontService.ProcessAsync(incompleteSource, continued,
        Path.Combine(work, "continue-work"), stagedFonts, detection.Location, "ffmpeg",
        (_, _) => Task.FromResult(IssueDecision.Continue));
    if (continuedResult.Code != 0) throw new Exception(continuedResult.Output);
    var continuedAttachments = await Capture("ffprobe", ["-v", "error", "-select_streams", "t",
        "-show_entries", "stream=index", "-of", "csv=p=0", continued]);
    if (continuedAttachments.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length != 1)
        throw new Exception("Continue decision did not preserve original attachment");
    Console.WriteLine("PASS: ZIP/7Z/TAR, fontTools, ASS/SSA/GBK, MKV attachments, replacement, supplement mode");
}
finally
{
    await FontPackagingService.CleanupDirectoryAsync(work);
}

static async Task Run(string file, IEnumerable<string> args)
{
    var output = await Capture(file, args);
    if (output.Contains("Error", StringComparison.OrdinalIgnoreCase)) throw new Exception(output);
}

static async Task<string> Capture(string file, IEnumerable<string> args)
{
    var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    foreach (var arg in args) start.ArgumentList.Add(arg);
    using var process = Process.Start(start) ?? throw new Exception("Cannot start " + file);
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    var result = await stdout + await stderr;
    if (process.ExitCode != 0) throw new Exception(file + " exited " + process.ExitCode + ": " + result);
    return result;
}

static async Task RunRealFixture(string video, string archive)
{
    var work = Path.Combine(Path.GetTempPath(), "WinUIBatchPacker-Real-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(work);
    try
    {
        var archiveFiles = await Capture("tar", ["-tf", archive]);
        var archiveFonts = archiveFiles.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(x => x.Trim().EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                x.Trim().EndsWith(".otf", StringComparison.OrdinalIgnoreCase));
    var archiveDir = Path.Combine(work, "archive");
    Directory.CreateDirectory(archiveDir);
    await Capture("tar", ["-xf", archive, "-C", archiveDir]);
    if (Directory.GetFiles(archiveDir, "*", SearchOption.AllDirectories).Length != archiveFonts)
        throw new Exception("Archive font extraction count mismatch");
    var originalProbe = await Capture("ffprobe", ["-v", "error", "-show_streams", "-of", "json", video]);
    string[] embeddedNames;
    using (var original = JsonDocument.Parse(originalProbe))
    {
        embeddedNames = original.RootElement.GetProperty("streams").EnumerateArray()
            .Where(s => s.GetProperty("codec_type").GetString() == "attachment")
            .Select(s => s.GetProperty("tags").GetProperty("filename").GetString() ?? "")
            .Select(name => System.Text.RegularExpressions.Regex.Replace(name,
                @"\.0\.[A-Z0-9]{8}(?=\.(?:ttf|otf)$)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var archiveNames = Directory.GetFiles(archiveDir, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!embeddedNames.SequenceEqual(archiveNames, StringComparer.OrdinalIgnoreCase))
            throw new Exception("Archive fonts and MKV attachment names differ");
    }

        var stagedArchive = FontPackagingService.PrepareFonts(archive, Path.Combine(work, "font-stage"));
        if (Directory.GetFiles(stagedArchive).Length != archiveFonts)
            throw new Exception("Bundled 7-Zip extraction count differs from archive");
        var detection = await FontPackagingService.DetectFontToolsAsync();
        if (detection.Location is null) throw new Exception(detection.Warning);
        var output = Path.Combine(work, "processed.mkv");
        var hadIssues = false;
        var completePrompted = false;
        var processed = await EmbeddedFontService.ProcessAsync(video, output, Path.Combine(work, "task"),
            stagedArchive, detection.Location, "ffmpeg", (source, check) =>
            {
                hadIssues = true;
                Console.WriteLine("REAL FIXTURE FONT WARNING (" + source + "): " + check.Summary);
                return Task.FromResult(IssueDecision.Continue);
            }, check =>
            {
                completePrompted = true;
                Console.WriteLine("REAL FIXTURE FONTS COMPLETE: " + check.Summary);
                return Task.FromResult(IssueDecision.Continue);
            });
        if (processed.Code != 0) throw new Exception(processed.Output);
        var json = await Capture("ffprobe", ["-v", "error", "-show_streams", "-of", "json", output]);
        using var doc = JsonDocument.Parse(json);
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var subtitles = streams.Count(s => s.GetProperty("codec_type").GetString() == "subtitle");
        var attachments = streams.Count(s => s.GetProperty("codec_type").GetString() == "attachment");
        if (subtitles != 2 || attachments == 0) throw new Exception($"Unexpected real MKV output: {subtitles} subtitles, {attachments} attachments");
        var outputNames = streams.Where(s => s.GetProperty("codec_type").GetString() == "attachment")
            .Select(s => s.GetProperty("tags").GetProperty("filename").GetString() ?? "")
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (outputNames.SequenceEqual(embeddedNames, StringComparer.OrdinalIgnoreCase))
            throw new Exception("Real fixture retained original font attachments instead of replacing them");
        if (!hadIssues && !completePrompted) throw new Exception("Real fixture font decision was not requested");
        Console.WriteLine($"PASS REAL: 7z fonts={archiveFonts}, subtitles={subtitles}, attachments={attachments}, warning={hadIssues}, completePrompted={completePrompted}, outputMB={new FileInfo(output).Length / 1048576}");
    }
    finally { await FontPackagingService.CleanupDirectoryAsync(work); }
}
