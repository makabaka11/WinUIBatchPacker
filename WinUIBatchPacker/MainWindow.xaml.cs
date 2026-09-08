using System.Diagnostics;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace WinUIBatchPacker;

public sealed partial class MainWindow : Window
{
    private bool _refreshing;
    private readonly TextBox FfmpegBox = new();
    private readonly TextBox VideoFolderBox = new();
    private readonly TextBox SubtitleFolderBox = new();
    private readonly TextBox InputFolderBox = new();
    private readonly TextBox OutputFolderBox = new();
    private readonly CheckBox SameFolderCheck = new() { Content = "视频和字幕位于同一个文件夹" };
    private readonly CheckBox ReplaceCheck = new() { Content = "封装成功后安全替换原视频" };
    private readonly CheckBox DefaultSubtitleCheck = new() { Content = "将新增的第一条字幕设为默认轨道" };
    private readonly Grid SeparateFoldersPanel = new();
    private readonly Grid InputFolderPanel = new();
    private readonly Grid OutputFolderPanel = new();
    private readonly ComboBox EncodingBox = new();
    private readonly ComboBox LanguageBox = new();
    private readonly ProgressBar Progress = new() { Minimum = 0, Maximum = 1, Height = 6, CornerRadius = new CornerRadius(3) };
    private readonly TextBlock ProgressText = new() { FontSize = 12, Opacity = 0.7, Text = "就绪", Margin = new Thickness(0, 4, 0, 0) };
    private readonly ScrollViewer LogScroller = new() { Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBlock LogText = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 12, IsTextSelectionEnabled = true, Padding = new Thickness(10) };
    private readonly InfoBar MatchInfo = new() { IsOpen = true, Severity = InfoBarSeverity.Informational, Title = "请选择输入路径", IsClosable = false, CornerRadius = new CornerRadius(10) };
    private readonly MediaListView VideoList = new();
    private readonly MediaListView SubtitleList = new();
    private readonly Border TitleBar = new();

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1920, 1000));
        BuildTitleBar();
        BuildInterface();
        VideoList.HeaderText = "视频文件";
        SubtitleList.HeaderText = "字幕组";
        VideoList.SelectionChangedByCheck += ListCheckChanged;
        SubtitleList.SelectionChangedByCheck += ListCheckChanged;
        VideoList.Reordered += ListReordered;
        SubtitleList.Reordered += ListReordered;
        VideoList.RefreshRequested += async (_, _) => await RefreshList(VideoList, true);
        SubtitleList.RefreshRequested += async (_, _) => await RefreshList(SubtitleList, false);
    }

    private void BuildTitleBar()
    {
        var titleText = new TextBlock
        {
            Text = "视频字幕批量封装",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(44, 0, 0, 0)
        };
        TitleBar.Height = 48;
        TitleBar.VerticalAlignment = VerticalAlignment.Top;
        TitleBar.Child = new Grid
        {
            Children = { titleText, BuildAboutButton() },
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))
        };
        SetTitleBar(TitleBar);
    }

    private Button BuildAboutButton()
    {
        var btn = new Button
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = "\uE946", FontSize = 16 }
        };
        ToolTipService.SetToolTip(btn, "开发者信息");
        // 固定在标题栏右侧，紧贴系统最小化按钮（系统三按钮宽约 138px）
        btn.Margin = new Thickness(0, 0, 136, 0);
        btn.Click += ShowAboutDialog;
        return btn;
    }

    private void ShowAboutDialog(object sender, RoutedEventArgs e)
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "Retr0",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        });
        content.Children.Add(new TextBlock
        {
            Text = "GitHub",
            FontSize = 14,
            Opacity = .85
        });
        content.Children.Add(new TextBlock
        {
            Text = "https://github.com/makabaka11",
            FontSize = 14,
            Foreground = Application.Current.Resources["AccentTextFillColorPrimaryBrush"] as Brush
        });
        content.Children.Add(new TextBlock
        {
            Text = "联系邮箱",
            FontSize = 14,
            Opacity = .85,
            Margin = new Thickness(0, 6, 0, 0)
        });
        content.Children.Add(new TextBlock
        {
            Text = "ded000@retr0.xyz",
            FontSize = 14,
            IsTextSelectionEnabled = true
        });

        var dialog = new ContentDialog
        {
            Title = "开发者信息",
            Content = content,
            PrimaryButtonText = "打开主页",
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = TitleBar.XamlRoot
        };
        dialog.PrimaryButtonClick += (_, _) =>
            Process.Start(new ProcessStartInfo("https://github.com/makabaka11") { UseShellExecute = true });
        _ = dialog.ShowAsync();
    }

    private void BuildInterface()
    {
        SameFolderCheck.Checked += FolderModeChanged; SameFolderCheck.Unchecked += FolderModeChanged;
        ReplaceCheck.Checked += ReplaceModeChanged; ReplaceCheck.Unchecked += ReplaceModeChanged;
        VideoFolderBox.TextChanged += FolderTextChanged; SubtitleFolderBox.TextChanged += FolderTextChanged; InputFolderBox.TextChanged += FolderTextChanged;
        EncodingBox.Items.Add("UTF-8"); EncodingBox.Items.Add("gbk"); EncodingBox.Items.Add("cp936"); EncodingBox.Items.Add("gb2312"); EncodingBox.Items.Add("big5"); EncodingBox.SelectedIndex = 0;
        LanguageBox.Items.Add("简体中文"); LanguageBox.Items.Add("繁体中文"); LanguageBox.Items.Add("英语"); LanguageBox.SelectedIndex = 0;

        var root = new Grid { ColumnSpacing = 16, Margin = new Thickness(0), Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(700) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        // Left panel
        var left = new StackPanel { Spacing = 16, Margin = new Thickness(24, 56, 0, 24) };
        left.Children.Add(new TextBlock { Text = "视频字幕批量封装", FontSize = 32, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });

        // FFmpeg card
        var ff = new StackPanel { Spacing = 10 };
        ff.Children.Add(Heading("FFmpeg"));
        ff.Children.Add(new TextBlock { Text = "已加入系统 PATH 时可以留空", Opacity = .55, FontSize = 13 });
        FfmpegBox.PlaceholderText = "ffmpeg.exe 路径";
        FfmpegBox.CornerRadius = new CornerRadius(8);
        ff.Children.Add(PathRow(FfmpegBox, "选择文件", PickFfmpeg_Click));
        left.Children.Add(Card(ff));

        // Folder card
        var folders = new StackPanel { Spacing = 12 };
        folders.Children.Add(Heading("文件位置"));
        folders.Children.Add(SameFolderCheck);
        ConfigureFolderGrid(SeparateFoldersPanel, true);
        AddFolderRow(SeparateFoldersPanel, VideoFolderBox, "视频文件夹", PickVideoFolder_Click, 0);
        AddFolderRow(SeparateFoldersPanel, SubtitleFolderBox, "字幕文件夹", PickSubtitleFolder_Click, 1);
        folders.Children.Add(SeparateFoldersPanel);
        ConfigureFolderGrid(InputFolderPanel, false);
        AddFolderRow(InputFolderPanel, InputFolderBox, "输入文件夹", PickInputFolder_Click, 0);
        InputFolderPanel.Visibility = Visibility.Collapsed;
        folders.Children.Add(InputFolderPanel);
        folders.Children.Add(ReplaceCheck);
        ConfigureFolderGrid(OutputFolderPanel, false);
        AddFolderRow(OutputFolderPanel, OutputFolderBox, "输出文件夹", PickOutputFolder_Click, 0);
        folders.Children.Add(OutputFolderPanel);
        left.Children.Add(Card(folders));

        // Subtitle options card
        var subs = new StackPanel { Spacing = 12 };
        subs.Children.Add(Heading("字幕选项"));
        var combos = new Grid { ColumnSpacing = 12 };
        combos.ColumnDefinitions.Add(new ColumnDefinition());
        combos.ColumnDefinitions.Add(new ColumnDefinition());
        EncodingBox.Header = "文件编码";
        EncodingBox.CornerRadius = new CornerRadius(8);
        LanguageBox.Header = "未知后缀语言";
        LanguageBox.CornerRadius = new CornerRadius(8);
        Grid.SetColumn(LanguageBox, 1);
        combos.Children.Add(EncodingBox);
        combos.Children.Add(LanguageBox);
        subs.Children.Add(combos);
        subs.Children.Add(DefaultSubtitleCheck);
        left.Children.Add(Card(subs));

        // Log card
        var logs = new StackPanel { Spacing = 10 };
        var logHead = new Grid();
        logHead.ColumnDefinitions.Add(new ColumnDefinition());
        logHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        logHead.Children.Add(Heading("执行日志"));
        var clear = AccentButton("清空", ClearLog_Click);
        clear.Margin = new Thickness(0, 0, 0, 0);
        Grid.SetColumn(clear, 1);
        logHead.Children.Add(clear);
        logs.Children.Add(logHead);
        var progressPanel = new StackPanel { Spacing = 0 };
        progressPanel.Children.Add(Progress);
        progressPanel.Children.Add(ProgressText);
        logs.Children.Add(progressPanel);
        LogScroller.Content = LogText;
        logs.Children.Add(LogScroller);
        var start = PrimaryButton("开始批量封装", StartPack_Click);
        start.HorizontalAlignment = HorizontalAlignment.Stretch;
        start.Height = 40;
        start.FontSize = 15;
        start.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        logs.Children.Add(start);
        left.Children.Add(Card(logs));

        var scroll = new ScrollViewer
        {
            Content = left,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Enabled
        };
        scroll.PointerWheelChanged += (s, e) =>
        {
            var delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;
            scroll.ChangeView(null, scroll.VerticalOffset - delta, null, true);
            e.Handled = true;
        };
        root.Children.Add(scroll);

        // Right panel
        var right = new Grid { RowSpacing = 14, Margin = new Thickness(0, 56, 24, 24) };
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition());
        right.RowDefinitions.Add(new RowDefinition());
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(right, 1);

        right.Children.Add(new TextBlock { Text = "匹配方案", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });

        Grid.SetRow(VideoList, 1);
        right.Children.Add(VideoList);
        Grid.SetRow(SubtitleList, 2);
        right.Children.Add(SubtitleList);

        Grid.SetRow(MatchInfo, 3);
        right.Children.Add(MatchInfo);
        root.Children.Add(right);

        // TitleBar on top
        var overlay = new Grid();
        overlay.Children.Add(root);
        overlay.Children.Add(TitleBar);
        Canvas.SetZIndex(TitleBar, 100);
        Content = overlay;
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 17,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Opacity = 0.9
    };

    private static Border Card(UIElement child)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 250, 251, 253)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22),
            Child = child
        };
        return border;
    }

    private static Button PrimaryButton(string text, RoutedEventHandler click)
    {
        var b = new Button
        {
            Content = text,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            CornerRadius = new CornerRadius(10)
        };
        b.Click += click;
        return b;
    }

    private static Button AccentButton(string text, RoutedEventHandler click)
    {
        var b = new Button
        {
            Content = text,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            CornerRadius = new CornerRadius(8)
        };
        b.Click += click;
        return b;
    }

    private static Button Button(string text, RoutedEventHandler click)
    {
        var b = new Button
        {
            Content = text,
            CornerRadius = new CornerRadius(8)
        };
        b.Click += click;
        return b;
    }

    private static Grid PathRow(TextBox box, string label, RoutedEventHandler click)
    {
        var g = new Grid { ColumnSpacing = 8 };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var b = Button(label, click);
        b.CornerRadius = new CornerRadius(8);
        Grid.SetColumn(b, 1);
        g.Children.Add(box);
        g.Children.Add(b);
        return g;
    }

    private static void ConfigureFolderGrid(Grid grid, bool twoRows)
    {
        grid.ColumnSpacing = 8;
        grid.RowSpacing = 10;
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (twoRows) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    }

    private static void AddFolderRow(Grid grid, TextBox box, string header, RoutedEventHandler click, int row)
    {
        box.Header = header;
        box.CornerRadius = new CornerRadius(8);
        var b = Button("浏览", click);
        b.CornerRadius = new CornerRadius(8);
        b.Margin = new Thickness(0, 25, 0, 0);
        Grid.SetRow(box, row);
        Grid.SetRow(b, row);
        Grid.SetColumn(b, 1);
        grid.Children.Add(box);
        grid.Children.Add(b);
    }

    private IntPtr Hwnd => WindowNative.GetWindowHandle(this);
    private async Task<string?> PickFolder()
    {
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); InitializeWithWindow.Initialize(picker, Hwnd);
        return (await picker.PickSingleFolderAsync())?.Path;
    }
    private async void PickVideoFolder_Click(object s, RoutedEventArgs e) { var p = await PickFolder(); if (p != null) VideoFolderBox.Text = p; }
    private async void PickSubtitleFolder_Click(object s, RoutedEventArgs e) { var p = await PickFolder(); if (p != null) SubtitleFolderBox.Text = p; }
    private async void PickInputFolder_Click(object s, RoutedEventArgs e) { var p = await PickFolder(); if (p != null) InputFolderBox.Text = p; }
    private async void PickOutputFolder_Click(object s, RoutedEventArgs e) { var p = await PickFolder(); if (p != null) OutputFolderBox.Text = p; }
    private async void PickFfmpeg_Click(object s, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".exe"); InitializeWithWindow.Initialize(picker, Hwnd);
        var file = await picker.PickSingleFileAsync(); if (file != null) FfmpegBox.Text = file.Path;
    }
    private void FolderModeChanged(object s, RoutedEventArgs e)
    {
        var same = SameFolderCheck.IsChecked == true;
        SeparateFoldersPanel.Visibility = same ? Visibility.Collapsed : Visibility.Visible;
        InputFolderPanel.Visibility = same ? Visibility.Visible : Visibility.Collapsed;
        RefreshLists();
    }
    private void ReplaceModeChanged(object s, RoutedEventArgs e) => OutputFolderPanel.Visibility = ReplaceCheck.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
    private void FolderTextChanged(object s, TextChangedEventArgs e) => RefreshLists();
    private async Task RefreshList(MediaListView list, bool isVideo)
    {
        list.SetRefreshing(true);
        try
        {
            var same = SameFolderCheck.IsChecked == true;
            var folder = same ? InputFolderBox.Text.Trim()
                : isVideo ? VideoFolderBox.Text.Trim() : SubtitleFolderBox.Text.Trim();
            var rows = isVideo ? MediaService.LoadVideos(folder) : MediaService.LoadSubtitleGroups(folder);
            await Task.Delay(300); // 让转圈动画可见
            list.SetItems(rows);
            UpdateMatchInfo();
        }
        catch (Exception ex) { AppendLog($"刷新失败：{ex.Message}"); }
        finally { list.SetRefreshing(false); }
    }
    private void RefreshLists()
    {
        if (_refreshing || VideoList == null) return; _refreshing = true;
        try
        {
            var same = SameFolderCheck.IsChecked == true;
            var videoFolder = same ? InputFolderBox.Text.Trim() : VideoFolderBox.Text.Trim();
            var subtitleFolder = same ? InputFolderBox.Text.Trim() : SubtitleFolderBox.Text.Trim();
            VideoList.SetItems(MediaService.LoadVideos(videoFolder)); SubtitleList.SetItems(MediaService.LoadSubtitleGroups(subtitleFolder)); UpdateMatchInfo();
        }
        catch (Exception ex) { AppendLog($"扫描失败：{ex.Message}"); }
        finally { _refreshing = false; }
    }
    private void ListCheckChanged(object? s, EventArgs e) => UpdateMatchInfo();
    private void ListReordered(object? s, EventArgs e) => UpdateMatchInfo();
    private void UpdateMatchInfo()
    {
        var videos = VideoList.SelectedRows().Count; var subtitles = SubtitleList.SelectedRows().Count; var matched = videos > 0 && videos == subtitles;
        MatchInfo.Title = matched ? $"✓ 当前将按序号处理 {videos} 对" : $"已选视频 {videos} 个，字幕组 {subtitles} 个；数量必须相等";
        MatchInfo.Severity = matched ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
    }
    private async void StartPack_Click(object sender, RoutedEventArgs e)
    {
        var videos = VideoList.SelectedRows(); var subtitles = SubtitleList.SelectedRows();
        if (videos.Count == 0 || videos.Count != subtitles.Count) { AppendLog("错误：两侧已选数量必须相等且不能为零。"); return; }
        var replace = ReplaceCheck.IsChecked == true; var output = OutputFolderBox.Text.Trim();
        if (!replace && output.Length == 0) { AppendLog("错误：请选择输出文件夹。"); return; }
        if (!replace) Directory.CreateDirectory(output);
        var encoding = EncodingBox.SelectedItem?.ToString() ?? "UTF-8";
        var language = LanguageBox.SelectedItem?.ToString() ?? "简体中文";
        var options = new PackOptions(FfmpegBox.Text.Trim(), encoding, language, DefaultSubtitleCheck.IsChecked == true, replace, output);
        Progress.Maximum = videos.Count; Progress.Value = 0;
        ProgressText.Text = "正在处理...";
        AppendLog($"开始处理 {videos.Count} 对。");
        for (var i = 0; i < videos.Count; i++)
        {
            var video = videos[i].Paths[0]; var subs = MediaService.Deduplicate(subtitles[i].Paths);
            var target = replace ? Path.Combine(Path.GetDirectoryName(video)!, $".__muxing_{Guid.NewGuid():N}{Path.GetExtension(video)}") : Path.Combine(output, Path.GetFileName(video));
            if (!replace && Path.GetFullPath(target).Equals(Path.GetFullPath(video), StringComparison.OrdinalIgnoreCase)) { AppendLog($"#{i + 1} 跳过：输出与原视频路径相同。"); continue; }
            AppendLog($"#{i + 1} {Path.GetFileName(video)}：加入 {subs.Count} 条字幕");
            try
            {
                var result = await MediaService.Pack(video, subs, target, options);
                AppendLog($"  ↳ ffmpeg {result.Command}");
                if (result.Code != 0) { AppendLog($"  ✗ 失败(退出码 {result.Code})：{result.Output[^Math.Min(2000, result.Output.Length)..]}"); if (replace && File.Exists(target)) File.Delete(target); }
                else { if (replace) File.Move(target, video, true); AppendLog("  ✓ 已完成"); }
            }
            catch (Exception ex) { AppendLog($"#{i + 1} 异常：{ex.Message}"); if (replace && File.Exists(target)) File.Delete(target); }
            Progress.Value = i + 1;
            ProgressText.Text = $"{i + 1} / {videos.Count}";
        }
        ProgressText.Text = "全部完成";
        AppendLog("全部任务执行完毕。");
    }
    private void AppendLog(string text)
    {
        LogText.Text += $"[{DateTime.Now:HH:mm:ss}] {text}\r\n";
        if (LogScroller.ScrollableHeight > 0) LogScroller.ChangeView(null, double.MaxValue, null);
    }
    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogText.Text = "";
}