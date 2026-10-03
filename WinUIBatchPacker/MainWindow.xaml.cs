using System.Diagnostics;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
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
    private readonly CheckBox ConfigureFontsCheck = new() { Content = "配置字体" };
    private readonly ComboBox FontModeBox = new() { Header = "字体处理模式" };
    private readonly TextBox FontsSourceBox = new() { PlaceholderText = "选择字体目录或 ZIP / 7Z / RAR 等压缩包" };
    private readonly StackPanel FontsSourcePanel = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly StackPanel FontsPickerPanel = new() { Spacing = 8 };
    private readonly TextBlock SupplementHelp = new() { Text = "从 MKV 提取内嵌 ASS/SSA 字幕，用所选目录或压缩包中的字体生成附件；无需配置外部字幕。", TextWrapping = TextWrapping.Wrap, Opacity = .65 };
    private readonly RowDefinition SubtitleListRow = new() { Height = new GridLength(1, GridUnitType.Star) };
    private UIElement? SubtitleOptionsCard;
    private readonly Grid SeparateFoldersPanel = new();
    private readonly Grid InputFolderPanel = new();
    private readonly Grid OutputFolderPanel = new();
    private readonly ComboBox EncodingBox = new();
    private readonly ComboBox LanguageBox = new();
    private readonly ProgressBar Progress = new() { Minimum = 0, Maximum = 1, Height = 6, CornerRadius = new CornerRadius(3) };
    private readonly TextBlock ProgressText = new() { FontSize = 12, Opacity = 0.7, Text = "就绪", Margin = new Thickness(0, 4, 0, 0) };
    private readonly ScrollViewer LogScroller = new() { Height = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly RichTextBlock LogText = new() { FontFamily = new FontFamily("Consolas"), FontSize = 15, IsTextSelectionEnabled = true, Padding = new Thickness(10) };
    private readonly StackPanel IssueCommandPanel = new() { Spacing = 6, Visibility = Visibility.Collapsed };
    private readonly TextBox IssueCommandBox = new() { PlaceholderText = "输入 Y / A / N / B / Q 后按 Enter" };
    private TaskCompletionSource<IssueDecision>? _pendingIssue;
    private IssueDecision? _stickyIssueDecision;
    private readonly InfoBar MatchInfo = new() { IsOpen = true, Severity = InfoBarSeverity.Informational, Title = "请选择输入路径", IsClosable = false, CornerRadius = new CornerRadius(10) };
    private readonly InfoBar FontCheckInfo = new() { IsOpen = false, Severity = InfoBarSeverity.Informational, Title = "字体预检", IsClosable = false, CornerRadius = new CornerRadius(10) };
    private CancellationTokenSource? _fontCheckDelay;
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
        Closed += (_, _) => _pendingIssue?.TrySetResult(IssueDecision.Stop);
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
        FontsSourceBox.TextChanged += (_, _) => ScheduleFontValidation();
        EncodingBox.SelectionChanged += (_, _) => ScheduleFontValidation();

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
        SubtitleOptionsCard = Card(subs);
        left.Children.Add(SubtitleOptionsCard);

        var fontsCard = new StackPanel { Spacing = 10 };
        fontsCard.Children.Add(Heading("字体选项（内置子集化工具）"));
        ConfigureFontsCheck.Checked += (_, _) => UpdateFontModeUi();
        ConfigureFontsCheck.Unchecked += (_, _) => UpdateFontModeUi();
        fontsCard.Children.Add(ConfigureFontsCheck);
        FontModeBox.Items.Add("首次封装模式");
        FontModeBox.Items.Add("仅补充字体模式");
        FontModeBox.SelectedIndex = 0;
        FontModeBox.SelectionChanged += (_, _) => UpdateFontModeUi();
        FontsSourcePanel.Children.Add(FontModeBox);
        FontsSourceBox.CornerRadius = new CornerRadius(8);
        FontsPickerPanel.Children.Add(FontsSourceBox);
        var fontButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        fontButtons.Children.Add(Button("选择目录", PickFontsFolder_Click));
        fontButtons.Children.Add(Button("选择压缩包", PickFontsArchive_Click));
        FontsPickerPanel.Children.Add(fontButtons);
        FontsSourcePanel.Children.Add(FontsPickerPanel);
        FontsSourcePanel.Children.Add(SupplementHelp);
        fontsCard.Children.Add(FontsSourcePanel);
        fontsCard.Children.Add(FontCheckInfo);
        left.Children.Add(Card(fontsCard));

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
        LogText.SizeChanged += (_, _) => ScrollLogToEnd();
        logs.Children.Add(LogScroller);
        IssueCommandPanel.Children.Add(new TextBlock { Text = "Y 继续当前　A 全部继续　N 跳过当前　B 全部跳过　Q 停止", TextWrapping = TextWrapping.Wrap, FontSize = 14 });
        var commandRow = new Grid { ColumnSpacing = 8 };
        commandRow.ColumnDefinitions.Add(new ColumnDefinition());
        commandRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        IssueCommandBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) { SubmitIssueCommand(); e.Handled = true; }
        };
        commandRow.Children.Add(IssueCommandBox);
        var submitIssue = Button("提交命令", (_, _) => SubmitIssueCommand());
        Grid.SetColumn(submitIssue, 1);
        commandRow.Children.Add(submitIssue);
        IssueCommandPanel.Children.Add(commandRow);
        logs.Children.Add(IssueCommandPanel);
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
            // 除以 3 平缓滚轮速度，避免误触错过
            var delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta / 3;
            scroll.ChangeView(null, scroll.VerticalOffset - delta, null, true);
            e.Handled = true;
        };
        root.Children.Add(scroll);

        // Right panel
        var right = new Grid { RowSpacing = 14, Margin = new Thickness(0, 56, 24, 24) };
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition());
        right.RowDefinitions.Add(SubtitleListRow);
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
            Background = Application.Current.Resources["SolidBackgroundFillColorSecondaryBrush"] as Brush
                      ?? new SolidColorBrush(Color.FromArgb(240, 250, 251, 253)),
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush
                       ?? new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
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
    private async void PickFontsFolder_Click(object s, RoutedEventArgs e) { var p = await PickFolder(); if (p != null) FontsSourceBox.Text = p; }
    private async void PickFontsArchive_Click(object s, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        foreach (var extension in FontPackagingService.ArchiveExtensions) picker.FileTypeFilter.Add(extension);
        InitializeWithWindow.Initialize(picker, Hwnd);
        var file = await picker.PickSingleFileAsync(); if (file != null) FontsSourceBox.Text = file.Path;
    }
    private async void PickFfmpeg_Click(object s, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".exe"); InitializeWithWindow.Initialize(picker, Hwnd);
        var file = await picker.PickSingleFileAsync(); if (file != null) FfmpegBox.Text = file.Path;
    }
    private void FolderModeChanged(object s, RoutedEventArgs e) => ApplyFolderMode();
    private void ApplyFolderMode()
    {
        var same = SameFolderCheck.IsChecked == true || IsSupplementMode;
        SeparateFoldersPanel.Visibility = same ? Visibility.Collapsed : Visibility.Visible;
        InputFolderPanel.Visibility = same ? Visibility.Visible : Visibility.Collapsed;
        RefreshLists();
    }
    private bool IsSupplementMode => ConfigureFontsCheck.IsChecked == true && FontModeBox.SelectedIndex == 1;
    private void UpdateFontModeUi()
    {
        var enabled = ConfigureFontsCheck.IsChecked == true;
        var supplement = IsSupplementMode;
        FontsSourcePanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        FontsPickerPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        SupplementHelp.Visibility = supplement ? Visibility.Visible : Visibility.Collapsed;
        SameFolderCheck.Visibility = supplement ? Visibility.Collapsed : Visibility.Visible;
        SubtitleList.Visibility = supplement ? Visibility.Collapsed : Visibility.Visible;
        SubtitleListRow.Height = supplement ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        if (SubtitleOptionsCard is not null) SubtitleOptionsCard.Visibility = supplement ? Visibility.Collapsed : Visibility.Visible;
        InputFolderBox.Header = supplement ? "视频 MKV 文件夹" : "输入文件夹";
        if (supplement && string.IsNullOrWhiteSpace(InputFolderBox.Text)) InputFolderBox.Text = VideoFolderBox.Text;
        ApplyFolderMode();
        ScheduleFontValidation();
    }
    private void ReplaceModeChanged(object s, RoutedEventArgs e) => OutputFolderPanel.Visibility = ReplaceCheck.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
    private void FolderTextChanged(object s, TextChangedEventArgs e) => RefreshLists();
    private async Task RefreshList(MediaListView list, bool isVideo)
    {
        list.SetRefreshing(true);
        try
        {
            var same = SameFolderCheck.IsChecked == true || IsSupplementMode;
            var folder = same ? InputFolderBox.Text.Trim()
                : isVideo ? VideoFolderBox.Text.Trim() : SubtitleFolderBox.Text.Trim();
            var rows = isVideo ? MediaService.LoadVideos(folder) : MediaService.LoadSubtitleGroups(folder);
            if (IsSupplementMode) rows = isVideo ? rows.Where(r => Path.GetExtension(r.Paths[0]).Equals(".mkv", StringComparison.OrdinalIgnoreCase)).ToList() : [];
            await Task.Delay(300); // 让转圈动画可见
            list.SetItems(rows);
            UpdateMatchInfo();
        }
        catch (Exception ex) { AppendLog($"刷新失败：{ex.Message}", LogLevel.Error); }
        finally { list.SetRefreshing(false); }
    }
    private void RefreshLists()
    {
        if (_refreshing || VideoList == null) return; _refreshing = true;
        try
        {
            var same = SameFolderCheck.IsChecked == true || IsSupplementMode;
            var videoFolder = same ? InputFolderBox.Text.Trim() : VideoFolderBox.Text.Trim();
            var subtitleFolder = same ? InputFolderBox.Text.Trim() : SubtitleFolderBox.Text.Trim();
            var videos = MediaService.LoadVideos(videoFolder);
            if (IsSupplementMode) videos = videos.Where(r => Path.GetExtension(r.Paths[0]).Equals(".mkv", StringComparison.OrdinalIgnoreCase)).ToList();
            VideoList.SetItems(videos);
            SubtitleList.SetItems(IsSupplementMode ? [] : MediaService.LoadSubtitleGroups(subtitleFolder)); UpdateMatchInfo();
        }
        catch (Exception ex) { AppendLog($"扫描失败：{ex.Message}", LogLevel.Error); }
        finally { _refreshing = false; }
    }
    private void ListCheckChanged(object? s, EventArgs e) => UpdateMatchInfo();
    private void ListReordered(object? s, EventArgs e) => UpdateMatchInfo();
    private void UpdateMatchInfo()
    {
        var videos = VideoList.SelectedRows().Count; var subtitles = SubtitleList.SelectedRows().Count;
        var matched = videos > 0 && (IsSupplementMode || videos == subtitles);
        MatchInfo.Title = IsSupplementMode
            ? videos > 0 ? $"✓ 将为 {videos} 个 MKV 的内嵌字幕配置字体" : "请选择包含 ASS/SSA 字幕的 MKV"
            : matched ? $"✓ 当前将按序号处理 {videos} 对" : $"已选视频 {videos} 个，字幕组 {subtitles} 个；数量必须相等";
        MatchInfo.Severity = matched ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        ScheduleFontValidation();
    }
    private void ScheduleFontValidation()
    {
        _fontCheckDelay?.Cancel();
        _fontCheckDelay?.Dispose();
        var subtitles = SubtitleList.SelectedRows().SelectMany(r => r.Paths).Distinct().ToArray();
        var source = FontsSourceBox.Text.Trim();
        if (ConfigureFontsCheck.IsChecked != true || IsSupplementMode ||
            source.Length == 0 || subtitles.Length == 0)
        {
            FontCheckInfo.IsOpen = false;
            return;
        }
        FontCheckInfo.IsOpen = true;
        FontCheckInfo.Severity = InfoBarSeverity.Informational;
        FontCheckInfo.Title = "正在检查字体";
        FontCheckInfo.Message = "正在核对所选 ASS/SSA 字幕与字体来源…";
        var delay = _fontCheckDelay = new CancellationTokenSource();
        var encoding = EncodingBox.SelectedItem?.ToString() ?? "UTF-8";
        _ = ValidateFontsAfterDelayAsync(subtitles, source, encoding, delay.Token);
    }
    private async Task ValidateFontsAfterDelayAsync(string[] subtitles, string source, string encoding, CancellationToken token)
    {
        try
        {
            await Task.Delay(500, token);
            var result = await FontValidationService.CheckAsync(subtitles, source, encoding, token);
            if (token.IsCancellationRequested) return;
            ShowFontValidation(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                FontCheckInfo.IsOpen = true;
                FontCheckInfo.Severity = InfoBarSeverity.Warning;
                FontCheckInfo.Title = "字体预检失败";
                FontCheckInfo.Message = ex.Message;
            }
        }
    }
    private void ShowFontValidation(FontValidationResult result)
    {
        FontCheckInfo.IsOpen = true;
        FontCheckInfo.Severity = result.HasIssues ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        FontCheckInfo.Title = result.HasIssues ? "字体清单存在问题（仍可开始封装）" : "字体清单已核对";
        FontCheckInfo.Message = result.Summary;
    }
    private async void StartPack_Click(object sender, RoutedEventArgs e)
    {
        var videos = VideoList.SelectedRows(); var subtitles = SubtitleList.SelectedRows();
        var supplement = IsSupplementMode;
        if (videos.Count == 0 || (!supplement && videos.Count != subtitles.Count)) { AppendLog(supplement ? "错误：请选择 MKV 视频。" : "错误：两侧已选数量必须相等且不能为零。", LogLevel.Error); return; }
        var replace = ReplaceCheck.IsChecked == true; var output = OutputFolderBox.Text.Trim();
        if (!replace && output.Length == 0) { AppendLog("错误：请选择输出文件夹。", LogLevel.Error); return; }
        var configureFonts = ConfigureFontsCheck.IsChecked == true;
        var fontsSource = FontsSourceBox.Text.Trim();
        if (configureFonts && fontsSource.Length == 0) { AppendLog("错误：请选择字体目录或压缩包。", LogLevel.Error); return; }
        FontValidationResult? fontPreflight = null;
        if (configureFonts && !supplement)
        {
            _fontCheckDelay?.Cancel();
            var selectedSubtitlePaths = subtitles.SelectMany(row => row.Paths).Distinct().ToArray();
            try
            {
                var check = await FontValidationService.CheckAsync(selectedSubtitlePaths, fontsSource,
                    EncodingBox.SelectedItem?.ToString() ?? "UTF-8");
                fontPreflight = check;
                ShowFontValidation(check);
                if (check.HasIssues) AppendLog("字体预检警告（继续封装）：" + check.Summary, LogLevel.Warn);
            }
            catch (Exception ex)
            {
                FontCheckInfo.IsOpen = true;
                FontCheckInfo.Severity = InfoBarSeverity.Warning;
                FontCheckInfo.Title = "字体预检失败（仍可开始封装）";
                FontCheckInfo.Message = ex.Message;
                AppendLog("字体预检失败（继续封装）：" + ex.Message, LogLevel.Warn);
            }
        }
        FontToolsLocation? fontTools = null;
        if (configureFonts)
        {
            var detection = await FontPackagingService.DetectFontToolsAsync();
            fontTools = detection.Location;
            if (fontTools is null) { AppendLog(detection.Warning, LogLevel.Warn); return; }
            AppendLog($"已检测到 Python/fontTools：{fontTools.Python}");
        }
        if (!replace) Directory.CreateDirectory(output);
        var encoding = EncodingBox.SelectedItem?.ToString() ?? "UTF-8";
        var language = LanguageBox.SelectedItem?.ToString() ?? "简体中文";
        var options = new PackOptions(FfmpegBox.Text.Trim(), encoding, language, DefaultSubtitleCheck.IsChecked == true, replace, output);
        _stickyIssueDecision = null;
        Progress.Maximum = videos.Count; Progress.Value = 0;
        ProgressText.Text = "正在处理...";
        AppendLog($"开始处理 {videos.Count} 对。");
        var batchWork = Path.Combine(Path.GetTempPath(), "WinUIBatchPacker", Guid.NewGuid().ToString("N"));
        var wasStopped = false;
        var batchFailed = false;
        try
        {
            string? fontsDirectory = null;
            if (configureFonts)
            {
                fontsDirectory = await Task.Run(() => FontPackagingService.PrepareFonts(fontsSource, batchWork));
                AppendLog("字体已准备就绪。");
            }
            var stopBatch = false;
            for (var i = 0; i < videos.Count; i++)
            {
                if (i > 0) AppendLog(new string('=', 64));
                var video = videos[i].Paths[0];
                var subs = supplement ? [] : MediaService.Deduplicate(subtitles[i].Paths);
                var finalName = configureFonts ? Path.ChangeExtension(Path.GetFileName(video), ".mkv") : Path.GetFileName(video);
                var final = replace ? Path.Combine(Path.GetDirectoryName(video)!, finalName) : Path.Combine(output, finalName);
                var target = Path.Combine(Path.GetDirectoryName(final)!, $".__muxing_{Guid.NewGuid():N}{Path.GetExtension(final)}");
                var episodeWork = Path.Combine(batchWork, $"episode_{i:D4}");
                if (!replace && Path.GetFullPath(final).Equals(Path.GetFullPath(video), StringComparison.OrdinalIgnoreCase))
                {
                    AppendLog($"#{i + 1} 跳过：输出与原视频路径相同。", LogLevel.Warn);
                    Progress.Value = i + 1; continue;
                }
                if (replace && !Path.GetFullPath(final).Equals(Path.GetFullPath(video), StringComparison.OrdinalIgnoreCase) && File.Exists(final))
                {
                    AppendLog($"#{i + 1} 跳过：目标 MKV 已存在：{final}", LogLevel.Warn);
                    Progress.Value = i + 1; continue;
                }
                AppendLog(supplement ? $"#{i + 1} {Path.GetFileName(video)}：处理内嵌字幕与字体" : $"#{i + 1} {Path.GetFileName(video)}：加入 {subs.Count} 条字幕");
                try
                {
                    (int Code, string Output, string Command) result;
                    if (supplement)
                    {
                        result = await EmbeddedFontService.ProcessAsync(video, target, episodeWork, fontsDirectory!, fontTools!, options.Ffmpeg,
                            (source, check) => PromptIssueAsync(source + "检查：" + check.Summary,
                                "Y/A 用所选字体尝试处理；字体来源不全时保留旧附件；N/B 跳过；Q 停止"),
                            check => PromptIssueAsync("字体检查通过：" + check.Summary,
                                "Y/A 重新字集化并替换旧字体附件；N/B 跳过；Q 停止"));
                    }
                    else
                    {
                        var muxSubtitles = subs.ToArray();
                        string[] fonts = [];
                        if (configureFonts)
                        {
                            try
                            {
                                var prepared = await FontPackagingService.SubsetEpisodeAsync(subs, fontsDirectory!, episodeWork,
                                    fontTools!, encoding, allowMissingFonts: fontPreflight?.Missing.Count > 0);
                                muxSubtitles = prepared.Subtitles;
                                fonts = prepared.Fonts;
                                AppendLog($"  字集化完成，准备附加 {fonts.Length} 个字体。");
                            }
                            catch (Exception ex)
                            {
                                AppendLog("字集化失败，改用原字幕与完整字体继续：" + ex.Message, LogLevel.Warn);
                                fonts = Directory.EnumerateFiles(fontsDirectory!)
                                    .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".ttf" or ".otf" or ".ttc" or ".otc")
                                    .ToArray();
                            }
                        }
                        result = await MediaService.Pack(video, muxSubtitles, target, options, fonts, subs);
                    }
                    AppendLog($"执行命令：ffmpeg {result.Command}", LogLevel.Command);
                    if (result.Code != 0) throw new InvalidOperationException($"FFmpeg 退出码 {result.Code}：{result.Output[^Math.Min(2000, result.Output.Length)..]}");
                    if (!File.Exists(target) || new FileInfo(target).Length == 0) throw new InvalidDataException("FFmpeg 未生成有效的目标文件。");
                    var commitWarning = MediaService.CommitOutput(video, final, target, replace);
                    if (commitWarning is not null) AppendLog(commitWarning, LogLevel.Warn);
                    AppendLog("  ✓ 已完成：" + final, LogLevel.Success);
                }
                catch (BatchControlException ex)
                {
                    stopBatch = ex.Decision == IssueDecision.Stop;
                    AppendLog(stopBatch ? "用户停止批次。" : $"#{i + 1} 已跳过。", LogLevel.Warn);
                }
                catch (Exception ex)
                {
                    AppendLog($"#{i + 1} 异常：{ex.Message}", LogLevel.Error);
                    if (supplement)
                    {
                        var decision = await PromptIssueAsync("处理失败：" + ex.Message,
                            "Y/A 保留原 MKV 继续；N/B 跳过；Q 停止");
                        if (decision == IssueDecision.Stop) stopBatch = true;
                        else if (decision == IssueDecision.Continue && !replace)
                        {
                            try
                            {
                                File.Copy(video, target, true);
                                var warning = MediaService.CommitOutput(video, final, target, false);
                                if (warning is not null) AppendLog(warning, LogLevel.Warn);
                                AppendLog("已保留原 MKV 到输出目录：" + final, LogLevel.Warn);
                            }
                            catch (Exception copyError) { AppendLog("保留原 MKV 失败：" + copyError.Message, LogLevel.Error); }
                        }
                        else if (decision == IssueDecision.Continue)
                            AppendLog("原 MKV 保持不变，继续下一项。", LogLevel.Warn);
                    }
                }
                finally
                {
                    try { if (File.Exists(target)) File.Delete(target); } catch (Exception ex) { AppendLog("临时视频清理失败：" + ex.Message, LogLevel.Warn); }
                    try { await FontPackagingService.CleanupDirectoryAsync(episodeWork); } catch (Exception ex) { AppendLog("临时字幕清理失败：" + ex.Message, LogLevel.Warn); }
                }
                Progress.Value = i + 1;
                ProgressText.Text = $"{i + 1} / {videos.Count}";
                if (stopBatch) { wasStopped = true; break; }
            }
        }
        catch (Exception ex)
        {
            batchFailed = true;
            AppendLog("批次准备失败：" + ex.Message, LogLevel.Error);
        }
        finally
        {
            try { await FontPackagingService.CleanupDirectoryAsync(batchWork); }
            catch (Exception ex) { AppendLog("临时字体清理失败：" + ex.Message, LogLevel.Warn); }
            ProgressText.Text = wasStopped ? "已停止" : batchFailed ? "执行失败" : "全部完成";
            AppendLog(wasStopped ? "批次已停止。" : batchFailed ? "批次执行失败。" : "全部任务执行完毕。");
        }
    }
    private void AppendLog(string text, LogLevel level = LogLevel.Info)
    {
        var run = new Run { Text = $"[{DateTime.Now:HH:mm:ss}] [{LogTag(level)}] {text}", Foreground = LogBrush(level) };
        var p = new Paragraph { Inlines = { run } };
        LogText.Blocks.Add(p);
        ScrollLogToEnd();
    }
    private void ScrollLogToEnd()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LogScroller.UpdateLayout();
            LogScroller.ChangeView(null, LogScroller.ScrollableHeight, null, true);
        });
    }
    private async Task<IssueDecision> PromptIssueAsync(string issue, string continuation)
    {
        if (_stickyIssueDecision is { } automatic)
        {
            AppendLog($"自动选择 {(automatic == IssueDecision.Continue ? "继续" : "跳过")}：{issue}", LogLevel.Warn);
            return automatic;
        }
        AppendLog($"已暂停：{issue}", LogLevel.Warn);
        AppendLog(continuation + "。请输入 Y / A / N / B / Q。", LogLevel.Warn);
        IssueCommandBox.Text = "";
        IssueCommandPanel.Visibility = Visibility.Visible;
        IssueCommandBox.Focus(FocusState.Programmatic);
        _pendingIssue = new TaskCompletionSource<IssueDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        try { return await _pendingIssue.Task; }
        finally { _pendingIssue = null; IssueCommandPanel.Visibility = Visibility.Collapsed; }
    }
    private void SubmitIssueCommand()
    {
        if (_pendingIssue is null) return;
        var command = IssueCommandBox.Text.Trim().ToUpperInvariant();
        var decision = command switch
        {
            "Y" or "A" => IssueDecision.Continue,
            "N" or "B" => IssueDecision.Skip,
            "Q" => IssueDecision.Stop,
            _ => (IssueDecision?)null
        };
        if (decision is null)
        {
            AppendLog("无效命令，请输入 Y、A、N、B 或 Q。", LogLevel.Warn);
            IssueCommandBox.SelectAll();
            return;
        }
        if (command is "A" or "B") _stickyIssueDecision = decision;
        AppendLog($"收到命令 {command}：{(decision == IssueDecision.Continue ? "继续" : decision == IssueDecision.Skip ? "跳过" : "停止")}");
        _pendingIssue.TrySetResult(decision.Value);
    }
    private static string LogTag(LogLevel level) => level switch
    {
        LogLevel.Command => "COMMAND",
        LogLevel.Warn    => "WARNING",
        LogLevel.Error   => "ERROR",
        LogLevel.Success => "SUCCESS",
        _                => "INFO",
    };
    private static SolidColorBrush LogBrush(LogLevel level) => level switch
    {
        LogLevel.Command => new SolidColorBrush(Color.FromArgb(255, 193, 154, 0)),
        LogLevel.Warn    => new SolidColorBrush(Color.FromArgb(255, 255, 122, 0)),
        LogLevel.Error   => new SolidColorBrush(Color.FromArgb(255, 233, 17, 35)),
        LogLevel.Success => new SolidColorBrush(Color.FromArgb(255, 14, 122, 13)),
        _                => new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)),
    };
    private enum LogLevel { Info, Command, Success, Warn, Error }
    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogText.Blocks.Clear();
}
