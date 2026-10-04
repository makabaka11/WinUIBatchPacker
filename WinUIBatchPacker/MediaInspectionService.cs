using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WinUIBatchPacker;

internal static class MediaInspectionService
{
    public static async Task<string> InspectMkvAsync(string file, string ffmpegOption, CancellationToken cancellationToken = default)
    {
        using var document = await ProbeAsync(file, ffmpegOption, cancellationToken);
        var streams = Streams(document.RootElement);
        var audio = streams.Where(s => Value(s, "codec_type") == "audio").ToArray();
        var subtitles = streams.Where(s => Value(s, "codec_type") == "subtitle").ToArray();
        var attachments = streams.Where(s => Value(s, "codec_type") == "attachment").ToArray();
        var fonts = attachments.Where(IsFont).ToArray();
        var result = new StringBuilder();
        result.AppendLine($"文件：{Path.GetFileName(file)}");
        result.AppendLine($"检测结果：音频 {audio.Length} 条，字幕 {subtitles.Length} 条，字体附件 {fonts.Length} 个");
        result.AppendLine();
        result.AppendLine($"音频轨道（{audio.Length}）");
        if (audio.Length == 0) result.AppendLine("  无");
        foreach (var stream in audio)
        {
            result.AppendLine($"  #{Value(stream, "index")}  {Value(stream, "codec_name", "未知编码")}  {ChannelText(stream)}  {SampleRate(stream)}{TrackTags(stream)}");
        }
        result.AppendLine();
        result.AppendLine($"字幕轨道（{subtitles.Length}）");
        if (subtitles.Length == 0) result.AppendLine("  无");
        foreach (var stream in subtitles)
        {
            result.AppendLine($"  #{Value(stream, "index")}  {Value(stream, "codec_name", "未知编码")}{TrackTags(stream)}");
        }
        result.AppendLine();
        result.AppendLine($"字体附件（{fonts.Length}；全部附件 {attachments.Length}）");
        if (fonts.Length == 0) result.AppendLine("  无");
        foreach (var stream in fonts)
        {
            result.AppendLine($"  #{Value(stream, "index")}  {Tag(stream, "filename", "未命名字体")}  {Value(stream, "codec_name", "未知格式")}");
        }
        if (attachments.Length > fonts.Length)
        {
            result.AppendLine($"其他附件（{attachments.Length - fonts.Length}）");
            foreach (var stream in attachments.Where(s => !IsFont(s)))
                result.AppendLine($"  #{Value(stream, "index")}  {Tag(stream, "filename", "未命名附件")}");
        }
        return result.ToString().TrimEnd();
    }

    public static async Task<string> InspectVideoAsync(string file, string ffmpegOption, CancellationToken cancellationToken = default)
    {
        using var document = await ProbeAsync(file, ffmpegOption, cancellationToken);
        var root = document.RootElement;
        var format = Property(root, "format");
        var streams = Streams(root);
        var video = streams.Where(s => Value(s, "codec_type") == "video").ToArray();
        var audio = streams.Where(s => Value(s, "codec_type") == "audio").ToArray();
        var result = new StringBuilder();
        result.AppendLine($"文件：{Path.GetFileName(file)}");
        result.AppendLine($"大小：{Size(new FileInfo(file).Length)}");
        result.AppendLine($"容器：{Value(format, "format_long_name", Value(format, "format_name", "未知"))}");
        result.AppendLine($"时长：{Duration(Value(format, "duration"))}");
        result.AppendLine($"总码率：{Bitrate(Value(format, "bit_rate"))}");
        result.AppendLine();
        result.AppendLine($"视频轨道（{video.Length}）");
        if (video.Length == 0) result.AppendLine("  无");
        foreach (var stream in video)
        {
            result.AppendLine($"  #{Value(stream, "index")}  编码：{Value(stream, "codec_name", "未知")}{Profile(stream)}");
            result.AppendLine($"    分辨率：{Value(stream, "width", "?")} × {Value(stream, "height", "?")}");
            result.AppendLine($"    帧率：{FrameRate(stream)}  像素格式：{Value(stream, "pix_fmt", "未知")}");
            result.AppendLine($"    码率：{Bitrate(Value(stream, "bit_rate"))}");
        }
        result.AppendLine();
        result.AppendLine($"音频轨道（{audio.Length}）");
        if (audio.Length == 0) result.AppendLine("  无");
        foreach (var stream in audio)
        {
            result.AppendLine($"  #{Value(stream, "index")}  {Value(stream, "codec_name", "未知编码")}  {ChannelText(stream)}  {SampleRate(stream)}  {Bitrate(Value(stream, "bit_rate"))}{TrackTags(stream)}");
        }
        return result.ToString().TrimEnd();
    }

    private static async Task<JsonDocument> ProbeAsync(string file, string ffmpegOption, CancellationToken cancellationToken)
    {
        var ffprobe = string.IsNullOrWhiteSpace(ffmpegOption) ? "ffprobe" :
            Path.GetFileName(ffmpegOption).Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(Path.GetDirectoryName(ffmpegOption)!, "ffprobe.exe") : "ffprobe";
        var start = new ProcessStartInfo(ffprobe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-v", "error", "-show_format", "-show_streams", "-of", "json", file })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 ffprobe。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill();
            throw;
        }
        if (process.ExitCode != 0)
            throw new InvalidDataException("媒体信息读取失败：" + (await stderr).Trim());
        return JsonDocument.Parse(await stdout);
    }

    private static JsonElement[] Streams(JsonElement root)
    {
        var streams = Property(root, "streams");
        return streams.ValueKind == JsonValueKind.Array ? streams.EnumerateArray().ToArray() : [];
    }

    private static JsonElement Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return default;
        foreach (var property in element.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return default;
    }

    private static string Value(JsonElement element, string name, string fallback = "")
    {
        var value = Property(element, name);
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? fallback,
            JsonValueKind.Number => value.GetRawText(),
            _ => fallback
        };
    }

    private static string Tag(JsonElement stream, string name, string fallback = "") =>
        Value(Property(stream, "tags"), name, fallback);

    private static bool IsFont(JsonElement stream)
    {
        var extension = Path.GetExtension(Tag(stream, "filename"));
        return extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".otf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".otc", StringComparison.OrdinalIgnoreCase) ||
               Value(stream, "codec_name") is "ttf" or "otf" ||
               Tag(stream, "mimetype").Contains("font", StringComparison.OrdinalIgnoreCase) ||
               Tag(stream, "mimetype").Contains("opentype", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrackTags(JsonElement stream)
    {
        var tags = new List<string>();
        var language = Tag(stream, "language");
        var title = Tag(stream, "title");
        if (language.Length > 0) tags.Add("语言：" + language);
        if (title.Length > 0) tags.Add("标题：" + title);
        var disposition = Property(stream, "disposition");
        if (Value(disposition, "default") == "1") tags.Add("默认");
        if (Value(disposition, "forced") == "1") tags.Add("强制");
        return tags.Count > 0 ? "  [" + string.Join("，", tags) + "]" : "";
    }

    private static string ChannelText(JsonElement stream) =>
        Value(stream, "channel_layout", Value(stream, "channels", "未知声道") + " 声道");

    private static string SampleRate(JsonElement stream) =>
        double.TryParse(Value(stream, "sample_rate"), CultureInfo.InvariantCulture, out var hertz)
            ? $"{hertz / 1000:0.###} kHz" : "未知采样率";

    private static string Profile(JsonElement stream)
    {
        var profile = Value(stream, "profile");
        return profile.Length > 0 ? $"（{profile}）" : "";
    }

    private static string FrameRate(JsonElement stream)
    {
        var raw = Value(stream, "avg_frame_rate", Value(stream, "r_frame_rate"));
        var parts = raw.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], CultureInfo.InvariantCulture, out var denominator) && denominator > 0)
            return $"{numerator / denominator:0.###} fps";
        return "未知";
    }

    private static string Bitrate(string raw) =>
        double.TryParse(raw, CultureInfo.InvariantCulture, out var bits) ? $"{bits / 1000:0.##} kb/s" : "未知";

    private static string Duration(string raw)
    {
        if (!double.TryParse(raw, CultureInfo.InvariantCulture, out var seconds) || seconds < 0)
            return "未知";
        var duration = TimeSpan.FromSeconds(seconds);
        var clock = duration.ToString(@"hh\:mm\:ss\.fff");
        return duration.Days > 0 ? $"{duration.Days} 天 {clock}" : clock;
    }

    private static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return index == 0 ? $"{bytes} B" : $"{value:0.##} {units[index]}";
    }
}
