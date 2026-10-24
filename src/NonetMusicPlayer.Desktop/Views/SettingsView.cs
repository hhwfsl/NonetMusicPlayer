using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;

namespace NonetMusicPlayer.Desktop.Views;

public sealed class SettingsView : UserControl, IDisposable
{
    private readonly MainViewModel _vm;
    private readonly List<IDisposable> _extensionViews = [];
    private readonly ComboBox _devices;
    private bool _updatingDevices;
    private readonly Border _layoutCard;
    private readonly ScrollViewer _scroll;
    private readonly List<(Border Card, string Keywords)> _sections = [];
    private readonly TextBlock _empty;
    public SettingsView(MainWindow owner, MainViewModel vm)
    {
        _vm = vm;
        var s = vm.Settings;
        void Changed() => vm.ApplySettings();
        var languageNames = new Dictionary<string, string> { ["zh-CN"] = "简体中文", ["ja-JP"] = "日本語", ["en-US"] = "English" };
        var language = Ui.Choice(languageNames.Values, languageNames[s.Language], value => { s.Language = languageNames.First(pair => pair.Value == value).Key; Changed(); L10n.SetLanguage(s.Language); });
        var themeNames = new Dictionary<string, string> { ["System"] = L10n.T("Common.FollowSystem"), ["Light"] = L10n.T("Common.Light"), ["Dark"] = L10n.T("Common.Dark") };
        var theme = Ui.Choice(themeNames.Values, themeNames[s.Theme], value => { s.Theme = themeNames.First(pair => pair.Value == value).Key; Changed(); });
        var accent = new TextBox { Name = "AccentHex", Text = s.Accent, PlaceholderText = "#A895FF", MinHeight = 38 };
        var preview = new Border { Name = "AccentPreview", Width = 32, Height = 32, CornerRadius = new(7), Background = new SolidColorBrush(Color.Parse(s.Accent)), BorderBrush = Ui.Brush("DividerBrush"), BorderThickness = new(1) };
        accent.TextChanged += (_, _) => { if (Color.TryParse(accent.Text, out var color)) preview.Background = new SolidColorBrush(color); };
        var hexRow = new Grid { ColumnDefinitions = new("40,*,Auto") }; hexRow.Children.Add(preview); Grid.SetColumn(accent, 1); hexRow.Children.Add(accent);
        void ApplyAccent(string value) { if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$")) throw new InvalidDataException(L10n.T("Common.UseRRGGBBSuchAsA895FF")); s.Accent = value; Changed(); }
        InputCommitService.Bind(accent, ApplyAccent);
        var apply = Ui.Button(L10n.T("Common.Apply"), () => { ApplyAccent(accent.Text ?? ""); owner.FocusManager?.Focus(null); }); Grid.SetColumn(apply, 2); hexRow.Children.Add(apply);
        var accentEditor = Ui.Stack(hexRow, Ui.AsyncButton(L10n.T("Settings.ColorWheelAndRGB"), async () => { var selected = await ColorPickerDialog.Show(owner, s.Accent); if (selected is null) return; accent.Text = s.Accent = selected; Changed(); }));
        void RefreshAccent(object? sender, EventArgs e)
        {
            if (!accent.IsKeyboardFocusWithin) accent.Text = vm.Settings.Accent;
            accentEditor.IsEnabled = !vm.Settings.AccentFromBackground || string.IsNullOrWhiteSpace(vm.Settings.BackgroundImagePath);
        }
        vm.SettingsChanged += RefreshAccent;
        _extensionViews.Add(new ElementLayoutService.ActionLease(() => vm.SettingsChanged -= RefreshAccent));
        RefreshAccent(null, EventArgs.Empty);
        var automaticAccent = Ui.Toggle(s.AccentFromBackground, value => { s.AccentFromBackground = value; Changed(); }); automaticAccent.Name = "AccentFromBackground";
        var backgroundPath = new ContentControl { Content = string.IsNullOrWhiteSpace(s.BackgroundImagePath) ? Ui.Text(L10n.T("Settings.NoBackgroundImage"), 12, true) : Ui.DataPathLink(L10n.T("Settings.BackgroundImage"), s.BackgroundImagePath, vm.Storage.Root) };
        var backgroundButtons = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        var selectBackground = Ui.AsyncButton(L10n.T("Settings.ChooseBackgroundImage"), async () =>
        {
            var files = await owner.OpenFilesAsync(L10n.T("Settings.ChooseBackgroundImage"), AppBackgroundService.MediaPatterns, false); if (files.Length == 0) return;
            var stored = await CoverCropDialog.ShowBackground(owner, vm.Storage, files[0]); if (stored is null) return;
            s.BackgroundImagePath = stored; Changed(); backgroundPath.Content = Ui.DataPathLink(L10n.T("Settings.BackgroundImage"), stored, vm.Storage.Root);
        }); selectBackground.Margin = new(0, 0, 8, 4); backgroundButtons.Children.Add(selectBackground);
        backgroundButtons.Children.Add(Ui.Button(L10n.T("Settings.RemoveBackgroundImage"), () => { s.BackgroundImagePath = null; Changed(); backgroundPath.Content = Ui.Text(L10n.T("Settings.NoBackgroundImage"), 12, true); }));
        var advancedAppearance = Ui.Stack(
            Ui.SettingRow("controlCornerRadius", L10n.T("Common.CornerRadius"), L10n.T("Common.AdjustRoundedCornersOnButtonsInputsAndSelectedItems"), Ui.Range(0, 22, s.ControlCornerRadius, v => { s.ControlCornerRadius = v; Changed(); }, " px")),
            Ui.Row(L10n.T("Settings.BackgroundImage"), L10n.T("Background.StaticHint"), Ui.Stack(backgroundPath, backgroundButtons)),
            Ui.SettingRow("backgroundImageOpacity", L10n.T("Settings.BackgroundOpacity35693E"), L10n.T("Settings.TheBackgroundCoversTheEntireWindowAdjustEachRegion"), Ui.Range(0, 100, s.BackgroundImageOpacity * 100, v => { s.BackgroundImageOpacity = v / 100; Changed(); }, "%")),
            Ui.SettingRow("titleBarOpacity", L10n.T("Settings.TitleBarOpacity"), L10n.T("Settings.OnlyChangesTheBackgroundOverlayTextAndButtonsRemain"), Ui.Range(0, 100, s.TitleBarOpacity * 100, v => { s.TitleBarOpacity = v / 100; Changed(); }, "%")),
            Ui.SettingRow("navigationOpacity", L10n.T("Settings.SidebarOpacity"), L10n.T("Settings.AppliesWhenABackgroundImageIsEnabled"), Ui.Range(0, 100, s.NavigationOpacity * 100, v => { s.NavigationOpacity = v / 100; Changed(); }, "%")),
            Ui.SettingRow("contentOpacity", L10n.T("Settings.ContentAreaOpacity"), L10n.T("Settings.AppliesWhenABackgroundImageIsEnabled"), Ui.Range(0, 100, s.ContentOpacity * 100, v => { s.ContentOpacity = v / 100; Changed(); }, "%")),
            Ui.SettingRow("playerOpacity", L10n.T("Settings.PlayerBarOpacity"), L10n.T("Settings.AppliesWhenABackgroundImageIsEnabled"), Ui.Range(0, 100, s.PlayerOpacity * 100, v => { s.PlayerOpacity = v / 100; Changed(); }, "%")),
            Ui.SettingRow("uiOpacity", L10n.T("Settings.WindowOpacity"), L10n.T("Common.AdjustTheOpacityOfTheEntireWindow"), Ui.Range(35, 100, s.UiOpacity * 100, v => { s.UiOpacity = v / 100; Changed(); }, "%")),
            Ui.SettingRow("backgroundImageStretch", L10n.T("Settings.BackgroundFit"), L10n.T("Common.FillCropsEdgesFitKeepsTheCompleteImageAt"), Ui.Choice([L10n.T("Common.Fill"), L10n.T("Common.Fit")], s.BackgroundImageStretch == "Uniform" ? L10n.T("Common.Fit") : L10n.T("Common.Fill"), v => { s.BackgroundImageStretch = v == L10n.T("Common.Fit") ? "Uniform" : "UniformToFill"; Changed(); })));
        var appearance = Ui.Card(L10n.T("Common.Appearance"),
            Ui.Row(L10n.T("Common.DisplayLanguage"), L10n.T("Common.ChangesTakeEffectImmediately"), language),
            Ui.SettingRow("theme", L10n.T("Settings.Theme"), L10n.T("Common.UseSystemLightOrDarkAppearance"), theme),
            Ui.SettingRow("accent", L10n.T("Settings.ThemeColor"), L10n.T("Settings.OneThemeColorForHoverSelectionButtonsAndPlayback"), accentEditor),
            Ui.SettingRow("accentFromBackground", L10n.T("Background.AutomaticAccent"), L10n.T("Background.AutomaticAccentHint"), automaticAccent),
            Ui.Row(L10n.T("Settings.InterfaceFont"), L10n.T("Settings.ChooseASystemFontOrImportAFontFile"), FontControls(owner, vm, Changed)),
            Ui.SettingRow("fontSize", L10n.T("Common.TextSize"), L10n.T("Common.AdjustBodyTextSize"), Ui.Range(11, 18, s.FontSize, v => { s.FontSize = v; Changed(); }, " px")),
            advancedAppearance);
        var layout = LayoutCard(owner, vm);
        // 标题栏由应用自绘，三个桌面平台都可切换位置；平台默认值仍由设置模型决定。
        var titleButtonPosition = Ui.Choice([L10n.T("Common.TopRight"), L10n.T("Common.TopLeft")], s.TitleButtonsOnLeft ? L10n.T("Common.TopLeft") : L10n.T("Common.TopRight"), v => { s.TitleButtonsOnLeft = v == L10n.T("Common.TopLeft"); Changed(); });
        titleButtonPosition.Name = "TitleButtonPosition";
        advancedAppearance.Children.Add(Ui.SettingRow("titleButtonsOnLeft", L10n.T("Common.WindowButtonPosition"), L10n.T("Common.WindowsOrderOnTheRightMacOSOrderOnThe"), titleButtonPosition));
        _layoutCard = layout;
        var devices = new[] { "Playback.SystemDefault" };
        devices = vm.AudioDevices.ToArray();
        _devices = Ui.Choice(devices.Append(s.DeviceName).Distinct(), s.DeviceName, v => { if (_updatingDevices) return; s.DeviceName = v; Changed(); });
        vm.AudioDevicesChanged += RefreshDevices;
        vm.SettingsChanged += RefreshDevices;
        var historyLimit = new NumericUpDown { Name = "HistoryLimit", Value = s.HistoryLimit, Minimum = 0, Maximum = 100000, Increment = 100, FormatString = "0", TextAlignment = TextAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center };
        InputCommitService.Bind(historyLimit, value => { if (value != decimal.Truncate(value)) throw new InvalidDataException(L10n.T("Common.HistoryLimitMustBeAnInteger")); s.HistoryLimit = (int)value; Changed(); });
        var playback = Ui.Card(L10n.T("Playback.PlaybackAndAudio"),
            Ui.Row(L10n.T("Playback.OutputDevice"), L10n.T("Playback.SwitchingPausesPlaybackAndKeepsYourPositionPressPlay"), _devices),
            Ui.Row(L10n.T("Playback.RecentHistoryLimit"), L10n.T("Common.KeepTracksByDefaultZeroDisablesHistoryLoweringThe"), historyLimit),
            Ui.Row(L10n.T("Common.ConfirmBeforeClosing"), L10n.T("Common.ShowAConfirmationBeforeClosing"), Ui.Toggle(s.ConfirmClose, v => { s.ConfirmClose = v; Changed(); })));
        ((StackPanel)playback.Child!).Children.Add(Ui.Row(L10n.T("Common.OptimizeMemoryWhenMinimized"), L10n.T("Settings.MemoryContinuity"), Ui.Toggle(s.OptimizeMemoryWhenMinimized, v => { s.OptimizeMemoryWhenMinimized = v; Changed(); })));
        ((StackPanel)playback.Child!).Children.Add(Ui.Row(L10n.T("Common.CloseButtonAction"), L10n.T("Common.KeepRunningInTheTrayExitFromItsMenu"), Ui.Choice([L10n.T("Common.HideToTray"), L10n.T("Common.ExitApplication4A67B7")], s.CloseToTray ? L10n.T("Common.HideToTray") : L10n.T("Common.ExitApplication4A67B7"), v => { s.CloseToTray = v == L10n.T("Common.HideToTray"); Changed(); })));
        Avalonia.Controls.ToggleSwitch? startOnLogin = null;
        startOnLogin = Ui.Toggle(s.StartOnLogin, value => { if (!vm.SetStartOnLogin(value)) startOnLogin!.IsChecked = s.StartOnLogin; });
        startOnLogin.Name = "StartOnLoginToggle";
        ((StackPanel)playback.Child!).Children.Add(Ui.Row(L10n.T("Startup.StartOnLogin"), L10n.T("Startup.Hint"), startOnLogin));
        if (OperatingSystem.IsWindows()) ((StackPanel)playback.Child!).Children.Add(Ui.Row(L10n.T("Playback.DefaultAudioPlayer"), L10n.T("Playback.RegisterAudioFormatsThenChooseTheDefaultAppIn"), Ui.AsyncButton(L10n.T("Playback.SetDefaultPlayer"), async () =>
        {
            if (!await PlayerDialog.Confirm(owner, L10n.T("Playback.RegisterAudioFileSupport"), L10n.T("Playback.AddThisAppToYourAudioFileHandlersAnd"), L10n.T("Common.RegisterAndOpenSettings"))) return;
            DefaultAudioAppService.RegisterAndOpenSettings();
        })));
        if (OperatingSystem.IsLinux()) ((StackPanel)playback.Child!).Children.Add(Ui.Row(L10n.T("Playback.DefaultAudioPlayer"), L10n.T("Playback.AfterRegistrationChooseThisAppAsTheAudioDefault"), Ui.AsyncButton(L10n.T("Playback.RegisterAudioHandler"), async () =>
        {
            if (!await PlayerDialog.Confirm(owner, L10n.T("Playback.RegisterAudioFileSupport"), L10n.T("Common.RegisterThisAppInTheCurrentUserSSystem"), L10n.T("Common.Register"))) return;
            await DefaultAudioAppService.RegisterLinuxAsync(vm.Storage); vm.ReportWarning(L10n.T("Playback.AudioSupportRegisteredChooseThisAppAsTheDefault"));
        })));
        var lyricsFont = new NumericUpDown { Name = "DesktopLyricsFontSize", Value = (decimal)s.DesktopLyricsFontSize, Minimum = 14, Maximum = 48, Increment = 1, FormatString = "0", TextAlignment = TextAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center };
        InputCommitService.Bind(lyricsFont, value => { if (value != decimal.Truncate(value)) throw new InvalidDataException(L10n.T("Common.FontSizeMustBeAnInteger")); s.DesktopLyricsFontSize = (double)value; Changed(); });
        var lyrics = Ui.Card(L10n.T("Lyrics.Lyrics"),
            Ui.Row(L10n.T("Lyrics.DesktopLyricsFontSize"), L10n.T("Lyrics.DesktopMarqueeHint"), lyricsFont),
            Ui.PathLink(L10n.T("Lyrics.LyricsFolder"), vm.Lyrics.Folder),
            Ui.Actions(Ui.AsyncButton(L10n.T("Lyrics.ChooseLyricsFolder"), async () =>
            {
                var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L10n.T("Lyrics.SelectLyricsFolder") });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                if (!await PlayerDialog.Confirm(owner, L10n.T("Lyrics.ChangeLyricsFolder"), L10n.T("Lyrics.ExistingLyricsAreKeptAndNotMovedAutomaticallyThey"), L10n.T("Common.Change"))) return;
                s.LyricsFolder = path; Changed(); vm.ReloadLyrics(); owner.ShowPage();
            }), Ui.Button(L10n.T("Lyrics.OpenLyricsFolder"), () => MainWindow.OpenPath(vm.Lyrics.Folder))),
            Ui.Row(L10n.T("Lyrics.LyricsTimingOffset"), L10n.T("Lyrics.PositiveValuesShowLyricsEarlierNegativeValuesLaterUnit"), Ui.Range(-10, 10, s.LyricOffset, v => { s.LyricOffset = v; Changed(); }, " s")),
            Ui.Text(L10n.T("Lyrics.MatchingLRCTXTFilesNextToMusicFilesAre"), 12, true));
        var pendingDataPath = new ContentControl();
        var data = Ui.Card(L10n.T("Common.DataAndRecovery"),
            Ui.AsyncButton(L10n.T("Common.ResetSettings"), async () =>
            {
                if (!await PlayerDialog.Confirm(owner, L10n.T("Common.ResetSettings4D41F7"), L10n.T("Lyrics.ResetAppearancePlaybackShortcutsAndLayoutMusicPlaylistsPlugins"), L10n.T("Common.RestoreDefaults"))) return;
                vm.ResetSettings(); owner.RestoreDefaultLayout(); owner.ShowPage();
            }),
            Ui.PathLink(L10n.T("Storage.DataFolder"), vm.Storage.Root),
            Ui.Text(L10n.T("Library.DataIsStoredInDataBesideTheAppChanging"), 12, true),
            Ui.AsyncButton(L10n.T("Storage.ChooseDataFolder"), async () =>
            {
                var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L10n.T("Storage.SelectAppDataFolder") });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                if (!await PlayerDialog.Confirm(owner, L10n.T("Storage.MoveAppData"), L10n.T("Common.DataIsCopiedAndOriginalsAreKeptTheNew"), L10n.T("Common.CopyApplyAfterRestart"))) return;
                var change = await vm.ConfigureDataDirectoryAsync(path); vm.ReportWarning(L10n.T("Storage.DataCopiedRestartTheAppToUseTheNew"));
                pendingDataPath.Content = Ui.PathLink(L10n.T("Common.AfterRestart"), change.NewRoot);
            }),
            pendingDataPath,
            Ui.PathLink(L10n.T("Storage.BackupFolder"), vm.Storage.BackupFolder),
            Ui.AsyncButton(L10n.T("Storage.ChooseBackupFolder"), async () =>
            {
                var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L10n.T("Storage.SelectBackupAndRecoveryFolder") });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
                vm.SetBackupFolder(path); owner.LayoutConfiguration.SetBackupFolder(path); owner.ShowPage();
            }),
            Ui.Actions(Ui.AsyncButton(L10n.T("Library.RestorePreviousLibrary"), async () =>
            {
                if (!await PlayerDialog.Confirm(owner, L10n.T("Library.RestorePreviousLibrary4E6137"), L10n.T("Statistics.LibraryPlaylistsStatisticsAndSettingsWillBeReplacedAfter"), L10n.T("Common.Restore"))) return;
                vm.RestoreLibraryBackup(); owner.ShowPage();
            }), Ui.AsyncButton(L10n.T("Library.RestoreLibraryFromFile"), async () =>
            {
                var paths = await owner.OpenFilesAsync(L10n.T("Common.SelectStateJsonOrBak"), ["*.json", "*.db", "*.bak"], false); if (paths.Length == 0) return;
                if (!await PlayerDialog.Confirm(owner, L10n.T("Storage.ImportLibraryBackup"), L10n.T("Storage.ReplaceTheCurrentLibraryAndSettingsTheSourceFile"), L10n.T("Common.ImportAndRestore"))) return;
                vm.RestoreLibraryBackup(paths[0]); owner.ShowPage();
            })), Ui.Button(L10n.T("Common.UndoLastAction"), () => vm.UndoCommand.Execute(null)));
        var sections = Ui.Stack();
        var terminalLines = new NumericUpDown { Name = "TerminalScrollbackLines", Value = s.TerminalScrollbackLines, Minimum = 1, Maximum = 10000, Increment = 100, FormatString = "0", TextAlignment = TextAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center };
        InputCommitService.Bind(terminalLines, value => { if (value != decimal.Truncate(value)) throw new InvalidDataException(L10n.T("Commands.IntegerRequired")); s.TerminalScrollbackLines = (int)value; Changed(); });
        var terminal = Ui.Card(L10n.T("Terminal.Title"),
            Ui.Row(L10n.T("Terminal.Opacity"), L10n.T("Terminal.OpacityHint"), Ui.Range(0, 100, s.TerminalOpacity * 100, v => { s.TerminalOpacity = v / 100; Changed(); }, "%")),
            Ui.Row(L10n.T("Terminal.Scrollback"), L10n.T("Terminal.ScrollbackHint"), terminalLines),
            Ui.Row(L10n.T("Terminal.FontSize"), L10n.T("Terminal.FontSizeHint"), Ui.Range(10, 32, s.TerminalFontSize, v => { s.TerminalFontSize = v; Changed(); }, " px")),
            Ui.Row(L10n.T("Terminal.MinimumLevel"), L10n.T("Terminal.MinimumLevelHint"), Ui.Choice(["INFO", "WARN", "ERROR"], s.TerminalMinimumLogLevel, v => { s.TerminalMinimumLogLevel = v; Changed(); })));
        AddSection(sections, appearance, L10n.T("Common.Appearance"), "语言 language 日本語 theme 主题 色轮 RGB 背景 background opacity 透明度 图片 字体 font 字号 圆角 窗口按钮", true);
        AddSection(sections, layout, L10n.T("Layout.Title"), "布局 layout 导航 播放栏 位置 position", false);
        AddSection(sections, playback, L10n.T("Playback.PlaybackAndAudio"), "输出 设备 device 定时 sleep 拖入 关闭 历史 history 最近", false);
        AddSection(sections, terminal, L10n.T("Terminal.Title"), "terminal cli 终端 ターミナル log 日志 字体 font 历史 scrollback 透明度 opacity", false);
        var pairingWait = new NumericUpDown { Name = "LanApprovalWaitSeconds", Value = s.LanApprovalWaitSeconds, Minimum = 10, Maximum = 120, Increment = 5, FormatString = "0", TextAlignment = TextAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center };
        InputCommitService.Bind(pairingWait, value => { if (value != decimal.Truncate(value)) throw new InvalidDataException(L10n.T("Commands.IntegerRequired")); s.LanApprovalWaitSeconds = (int)value; Changed(); });
        AddSection(sections, Ui.Card(L10n.T("Lan.Title"), Ui.Row(L10n.T("Lan.ApprovalWait"), L10n.T("Lan.ApprovalWaitHint"), pairingWait)), L10n.T("Lan.Title"), "network lan pair 设备 互听 配对 等待 timeout 通知", false);
        AddSection(sections, lyrics, L10n.T("Lyrics.Lyrics"), "lyrics 歌词 文件夹 路径 时间 微调 offset", false);
        AddSection(sections, ShortcutSettings.Create(vm), L10n.T("Settings.KeyboardShortcuts"), "shortcut keyboard 快捷键 按键 hotkey", false);
        AddSection(sections, data, L10n.T("Common.DataAndRecovery"), "data 数据 目录 路径 backup 恢复 备份 undo 撤销", false);
        foreach (var (card, title) in owner.ExtensionSettingsSections())
        {
            foreach (var view in card.GetVisualDescendants().OfType<IDisposable>()) _extensionViews.Add(view);
            AddSection(sections, card, title, title, true);
        }
        AddSection(sections, (Border)owner.AboutSettings(), L10n.T("Update.About"), "about version update repository github 关于 バージョン 更新 仓库", true);
        _empty = Ui.Text(L10n.T("Common.NoMatchingSettings1616FA"), 13, true); _empty.IsVisible = false; sections.Children.Add(_empty);
        _scroll = Ui.Scroll(sections);
        var search = new TextBox { Name = "SettingsSearch", PlaceholderText = L10n.T("Common.SearchSettings"), MinHeight = 44 };
        Avalonia.Automation.AutomationProperties.SetName(search, L10n.T("Common.SearchSettings"));
        InputCommitService.Bind(search, Filter);
        var clearSearch = Ui.Button("", () => { search.Text = ""; Filter(""); }); clearSearch.Content = new NonetMusicPlayer.Desktop.Controls.VectorIcon { Kind = NonetMusicPlayer.Desktop.Controls.IconKind.Close }; ToolTip.SetTip(clearSearch, L10n.T("Common.ClearSearch"));
        var backToTop = Ui.IconButton(IconKind.BackToTop, "Common.BackToTop", () => _scroll.Offset = default); backToTop.Name = "SettingsBackToTop";
        var searchRow = new Grid { ColumnDefinitions = new("*,44,44"), ColumnSpacing = 8, Margin = new(0, 0, 0, 12) }; searchRow.Children.Add(search); Grid.SetColumn(clearSearch, 1); searchRow.Children.Add(clearSearch); Grid.SetColumn(backToTop, 2); searchRow.Children.Add(backToTop);
        var page = new Grid { RowDefinitions = new("Auto,*"), Background = Brushes.Transparent }; page.Children.Add(searchRow); Grid.SetRow(_scroll, 1); page.Children.Add(_scroll); Content = page;
        AddHandler(Avalonia.Input.InputElement.PointerWheelChangedEvent, (_, e) =>
        {
            if (e.Handled) return;
            _scroll.Offset = new Vector(0, Math.Clamp(_scroll.Offset.Y - e.Delta.Y * 60, 0, Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height)));
            e.Handled = true;
        });
    }

    /// <summary>仅禁用对应编辑器，值仍显示用户原设置；关闭方案后按原状态还原。</summary>
    public void RefreshInterfaceOwnership(MainWindow owner)
    {
        foreach (var row in this.GetLogicalDescendants().OfType<ResponsiveSettingsRow>().Where(r => r.InterfaceSettingKey.Length > 0 && r.Children.Count > 1))
        {
            var editor = row.Children[1];
            var controlled = owner.IsInterfaceSettingOwned(row.InterfaceSettingKey);
            editor.IsEnabled = !controlled;
            if (row.InterfaceSettingKey == "accent" && (_vm.Settings.AccentFromBackground && !string.IsNullOrEmpty(_vm.Settings.BackgroundImagePath))) editor.IsEnabled = false;
            ToolTip.SetTip(row, controlled ? L10n.Format("Settings.InterfaceOwned", owner.InterfaceSettingOwner(row.InterfaceSettingKey) ?? "") : null);
            ToolTip.SetShowOnDisabled(row, true);
            if (row.Children[0] is StackPanel labels)
            {
                var note = labels.Children.OfType<TextBlock>().FirstOrDefault(t => t.Name == "InterfaceSettingOwnership");
                if (controlled && note is null) { note = Ui.Text("", 11, true); note.Name = "InterfaceSettingOwnership"; labels.Children.Add(note); }
                if (note is not null) { note.IsVisible = controlled; if (controlled) note.Text = L10n.Format("Settings.InterfaceOwned", owner.InterfaceSettingOwner(row.InterfaceSettingKey) ?? ""); }
            }
        }
    }
    private void AddSection(StackPanel parent, Border card, string title, string keywords, bool expanded)
    {
        _sections.Add((card, title + " " + keywords)); parent.Children.Add(card);
    }

    private void Filter(string query)
    {
        query = query.Trim(); var any = false;
        foreach (var section in _sections)
        {
            var stack = (StackPanel)section.Card.Child!;
            bool Matches(string value) => value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
            var wholeSection = string.IsNullOrEmpty(query) || Matches(section.Keywords);
            var matchingChildren = false;
            foreach (var child in stack.Children)
            {
                var controls = child.GetLogicalDescendants().OfType<Control>().Prepend(child).ToArray();
                var text = string.Join(' ', controls.Select(control => control switch { TextBlock block => block.Text, ContentControl content => content.Content as string, _ => null }));
                var matched = wholeSection || Matches(child.Tag?.ToString() ?? "") || Matches(text);
                child.IsVisible = matched; matchingChildren |= matched;
            }
            section.Card.IsVisible = wholeSection || matchingChildren; any |= section.Card.IsVisible;
        }
        _empty.IsVisible = !any; _scroll.Offset = default;
    }

    public void FocusLayoutConfiguration() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        UpdateLayout();
        var location = _layoutCard.TranslatePoint(default, (Visual)_scroll.Content!); _scroll.Offset = new Avalonia.Vector(0, location?.Y ?? 0);
    }, Avalonia.Threading.DispatcherPriority.Loaded);

    // 布局定制由插件承担；设置仅保留两个常用位置偏好，不再提供 JSON 文件编辑入口。
    private static Border LayoutCard(MainWindow owner, MainViewModel vm)
    {
        var s = vm.Settings;
        var navigation = Ui.Choice([L10n.T("Layout.Left"), L10n.T("Layout.Right")],
            L10n.T(s.NavigationRight ? "Layout.Right" : "Layout.Left"),
            value => { s.NavigationRight = value == L10n.T("Layout.Right"); vm.ApplySettings(); });
        navigation.Name = "NavigationPosition";
        var player = Ui.Choice([L10n.T("Layout.Bottom"), L10n.T("Layout.Top")],
            L10n.T(s.PlayerTop ? "Layout.Top" : "Layout.Bottom"),
            value => { s.PlayerTop = value == L10n.T("Layout.Top"); vm.ApplySettings(); });
        player.Name = "PlayerPosition";
        return Ui.Card(L10n.T("Layout.Title"),
            Ui.SettingRow("navigationRight", L10n.T("Layout.NavigationPosition"), L10n.T("Layout.NavigationPositionHint"), navigation),
            Ui.SettingRow("playerTop", L10n.T("Layout.PlayerPosition"), L10n.T("Layout.PlayerPositionHint"), player));
    }
    private void RefreshDevices(object? sender, EventArgs e)
    {
        _updatingDevices = true;
        try { _devices.ItemsSource = _vm.AudioDevices.Append(_vm.Settings.DeviceName).Distinct().ToArray(); _devices.SelectedItem = _vm.Settings.DeviceName; }
        finally { _updatingDevices = false; }
    }
    private static readonly Lazy<string[]> InstalledFonts = new(() => Avalonia.Media.FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().Order().ToArray());
    private static Control FontControls(MainWindow owner, MainViewModel vm, Action changed)
    {
        var settings = vm.Settings;
        var status = new ContentControl();
        void RefreshStatus() => status.Content = settings.FontFilePath is { } path ? Ui.DataPathLink(L10n.T("Settings.FontFile"), AppFontService.FilePath(vm.Storage, path), vm.Storage.Root) : null;
        try { RefreshStatus(); } catch (InvalidDataException) { settings.FontFilePath = null; }
        var combo = FontSelector(settings, () => { settings.FontFilePath = null; RefreshStatus(); changed(); }); combo.Name = "SystemFontSelector";
        var import = Ui.AsyncButton(L10n.T("Settings.ChooseFontFile"), async () =>
        {
            var files = await owner.OpenFilesAsync(L10n.T("Settings.ChooseFontFile"), ["*.ttf", "*.otf", "*.ttc"], false); if (files.Length == 0) return;
            var relative = AppFontService.Import(vm.Storage, files[0]); settings.FontFilePath = relative; settings.FontFamily = "";
            combo.Tag = true;
            try { combo.ItemsSource = new[] { L10n.T("Settings.DefaultFont"), L10n.T("Settings.CustomFontFile") }; combo.SelectedItem = L10n.T("Settings.CustomFontFile"); }
            finally { combo.Tag = null; }
            changed(); RefreshStatus();
        });
        return Ui.Stack(combo, import, status);
    }
    private static ComboBox FontSelector(AppSettings settings, Action changed)
    {
        var defaultFont = L10n.T("Settings.DefaultFont");
        var customFont = L10n.T("Settings.CustomFontFile");
        string SelectedName() => settings.FontFilePath is not null ? customFont : string.IsNullOrEmpty(settings.FontFamily) ? defaultFont : settings.FontFamily;
        var combo = new ComboBox { ItemsSource = new[] { defaultFont, customFont }.Concat(string.IsNullOrEmpty(settings.FontFamily) ? [] : new[] { settings.FontFamily }), SelectedItem = SelectedName(), MinHeight = 38 };
        var refreshing = false;
        combo.DropDownOpened += async (_, _) =>
        {
            var names = await Task.Run(() => InstalledFonts.Value); refreshing = true;
            try { combo.ItemsSource = new[] { defaultFont }.Concat(settings.FontFilePath is null ? [] : new[] { customFont }).Concat(names).ToArray(); combo.SelectedItem = SelectedName(); }
            finally { refreshing = false; }
        };
        combo.SelectionChanged += (_, _) => { if (!refreshing && combo.Tag is not true && combo.SelectedItem is string name && name != customFont) { settings.FontFamily = name == defaultFont ? "" : name; changed(); } };
        return combo;
    }
    public void Dispose() { foreach (var view in _extensionViews) view.Dispose(); _extensionViews.Clear(); _vm.AudioDevicesChanged -= RefreshDevices; _vm.SettingsChanged -= RefreshDevices; }
}
