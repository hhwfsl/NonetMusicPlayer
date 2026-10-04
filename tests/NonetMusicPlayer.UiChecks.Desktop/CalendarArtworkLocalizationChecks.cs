using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Models;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;
using NonetMusicPlayer.Desktop.Views;

internal static class CalendarArtworkLocalizationChecks
{
    public static void Run(MainWindow owner, MainViewModel vm, string output)
    {
        vm.Navigate("statistics"); Pump(owner);
        var picker = owner.GetVisualDescendants().OfType<StatisticsDatePicker>().Single();
        foreach (var target in new[] { new DateTime(StatisticsDatePicker.MinimumYear, 1, 1), new DateTime(StatisticsDatePicker.MaximumYear, 12, 31), new DateTime(2004, 2, 29) })
        {
            picker.GetVisualDescendants().OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner);
            var year = picker.CalendarContent.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "StatisticsYear");
            var choices = year.Items.Cast<int>().ToArray();
            Require(choices.Length == 201 && choices.First() == DateTime.Today.Year - 100 && choices.Last() == DateTime.Today.Year + 100, "Exactly current year ±100, inclusive");
            Require(!year.IsEditable && !year.GetVisualDescendants().OfType<TextBox>().Any(t => t.IsEffectivelyVisible) && !picker.CalendarContent.GetVisualDescendants().OfType<NumericUpDown>().Any(), "Year selector has no editable input");
            year.IsDropDownOpen = true; Pump(owner); Require(year.IsDropDownOpen, "Year dropdown opens without crash"); year.SelectedItem = target.Year; Pump(owner);
            var month = picker.CalendarContent.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "StatisticsMonth"); month.SelectedIndex = target.Month - 1; Pump(owner);
            Require(!picker.CalendarContent.GetVisualDescendants().OfType<Button>().Any(b => b.IsEnabled && b.Name?.StartsWith("StatisticsDay_") == true && (int.Parse(b.Name[14..18]) < choices.First() || int.Parse(b.Name[14..18]) > choices.Last())), "Adjacent month days cannot escape year range");
            var navigation = picker.CalendarContent.GetVisualDescendants().OfType<Button>().Single(b => b.Name == (target.Year == choices.First() ? "StatisticsPreviousMonth" : "StatisticsNextMonth"));
            if (target.Year != 2004) Require(!navigation.IsEnabled, "Navigation stops at range endpoints");
            var day = picker.CalendarContent.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StatisticsDay_" + target.ToString("yyyyMMdd")); day.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner); Require(picker.SelectedDate == target, "Selected year/month/day applied");
        }
        picker.SelectedDate = DateTime.MinValue; Require(picker.SelectedDate!.Value.Year == StatisticsDatePicker.MinimumYear, "Old dates clamp to lower bound");
        picker.SelectedDate = DateTime.MaxValue; Require(picker.SelectedDate!.Value.Year == StatisticsDatePicker.MaximumYear, "Future dates clamp to upper bound");

        var track = vm.State.Tracks.First(t => t.Artwork is not null);
        var coverless = new TrackItem("beta14-coverless", "No artwork", track.Artist, track.Album, track.FilePath, track.Extension, track.FileSize);
        vm.State.Tracks.Insert(0, coverless);
        foreach (var type in new[] { "artist", "album" })
        {
            var name = type == "artist" ? track.Artist : track.Album; var key = type + ":" + name;
            var old = vm.State.GroupCovers.GetValueOrDefault(key); var forced = vm.UsesSoftwareDefaultGroupCover(type, name);
            vm.SetGroupCover(type, name, track.CoverPath); Require(vm.GetGroupCover(type, name) is not null, "Custom group artwork resolves");
            vm.SetGroupSoftwareDefaultCover(type, name); Require(vm.ResolveGroupCover(type, name, [coverless, track]) is null && vm.Storage.Load().SoftwareDefaultGroupCovers.Contains(key), "Explicit application placeholder persists and ignores embedded art");
            vm.Navigate(type + "s"); Pump(owner);
            var card = owner.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "MusicGroupCard" && g.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == name));
            Require(card.GetVisualDescendants().OfType<Image>().Single().Source is null, "Card renders application placeholder");
            var p = card.TranslatePoint(new Point(40, 40), owner)!.Value; owner.MouseDown(p, MouseButton.Right); owner.MouseUp(p, MouseButton.Right); Pump(owner);
            var menu = ActiveMenu(owner); Require(menu.Items.OfType<MenuItem>().Any(m => Equals(m.Header, "恢复默认封面")) && menu.Items.OfType<MenuItem>().Any(m => Equals(m.Header, "设置为软件默认封面")), "Card offers distinct automatic and application defaults"); menu.Close();
            vm.SetGroupCover(type, name, null); Require(ReferenceEquals(vm.ResolveGroupCover(type, name, [coverless, track]), track.Artwork), "Reset finds parsed cover even when first song has none");
            vm.Navigate(type + ":" + name); vm.SearchText = "no match"; Pump(owner);
            Require(owner.FindControl<ContentControl>("PlaylistHeader")!.GetVisualDescendants().OfType<Image>().Single().Source is not null, "Group detail art is independent of search");
            owner.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ClassificationMore").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner);
            Require(ActiveMenu(owner).Items.OfType<MenuItem>().Count() == 3, "Detail shares three cover actions"); ActiveMenu(owner).Close();
            vm.SetGroupCover(type, name, old); if (forced) vm.SetGroupSoftwareDefaultCover(type, name);
        }
        vm.State.Tracks.Remove(coverless);
        var playlist = vm.CreatePlaylist("Beta14 ordering"); vm.AddToPlaylist(playlist, [track]); vm.Navigate("playlist:" + playlist.Id); Pump(owner);
        var row = owner.FindControl<ListBox>("TracksList")!.GetRealizedContainers().First();
        var rowPoint = row.TranslatePoint(new Point(150, 30), owner)!.Value; owner.MouseDown(rowPoint, MouseButton.Right); owner.MouseUp(rowPoint, MouseButton.Right); Pump(owner);
        var rowMenu = ActiveMenu(owner);
        Require(rowMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "在歌单中上移")).Icon is VectorIcon { Kind: IconKind.MoveUp }, "Move up uses upright triangle");
        Require(rowMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "在歌单中下移")).Icon is VectorIcon { Kind: IconKind.MoveDown }, "Move down uses inverted triangle"); rowMenu.Close();

        vm.Navigate("plugins"); Pump(owner);
        var import = owner.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "从 GitHub Release 导入")); import.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(owner);
        var dialog = owner.OwnedWindows.OfType<PlayerDialog>().Single();
        Require(dialog.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "导入")) && !dialog.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "保存")), "Remote import acceptance is Import, not Save");
        dialog.Close(); Pump(owner); Require(import.IsEnabled, "Cancelled import releases button; no network request");

        var empty = vm.CreatePlaylist("User title 中文 stays unchanged");
        var originalLanguage = vm.Settings.Language;
        foreach (var language in new[] { "en-US", "ja-JP", "zh-CN" })
        {
            vm.Settings.Language = language; vm.ApplySettings(); vm.Navigate("playlist:" + empty.Id); Pump(owner);
            Require(vm.EmptyStateTitle == L10n.T("Library.MakeMusicPartOfYourDay") && vm.EmptyStateDescription == L10n.T("Playlists.AddMusicFilesOrDragSongsIntoAPlaylist"), "Empty playlist translates live");
            Require(vm.PageTitle == empty.Name, "User playlist names stay verbatim");
            if (language != "zh-CN") Require(vm.EmptyStateTitle != "让音乐成为日常的一部分" && !vm.EmptyStateDescription.Contains("添加音乐"), "Empty state is not untranslated Chinese");
            vm.Navigate("favorites"); Pump(owner); Require(vm.EmptyStateTitle == L10n.T("Common.KeepYourFavoriteSongsHere") || vm.VisibleTracks.Count > 0, "Liked empty state localized");
            vm.Navigate("songs"); vm.SearchText = "beta14-no-match-9999"; Pump(owner); Require(vm.EmptyStateTitle == L10n.T("Common.NoMatchingSongs"), "No-result search localized");
        }
        vm.Settings.Language = originalLanguage; vm.ApplySettings();
        var labelButton = new Button { Content = "A much longer translated button label than its container", Width = 130, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var textWindow = new Window { Width = 200, Height = 100, Content = labelButton }; textWindow.Show(); Pump(textWindow);
        var label = labelButton.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == (string)labelButton.Content);
        Require(label.TextTrimming == TextTrimming.CharacterEllipsis && label.TextLayout.TextLines.Any(line => line.HasCollapsed), "Constrained string control text actually renders ellipsis");
        using (var image = new RenderTargetBitmap(new PixelSize(200, 100))) { image.Render(textWindow); image.Save(Path.Combine(output, "beta14-ellipsis.png"), PngBitmapEncoderOptions.Default); } textWindow.Close();
        AuditCatalog();
        vm.DeletePlaylist(playlist); vm.DeletePlaylist(empty); vm.Navigate("songs"); Pump(owner);
        Console.WriteLine("PASS BETA14: 201 non-editable years and bounded dates, distinct/persisted cover modes and parsed fallback, triangular reorder icons, Import dialog, live three-language empty states, actual ellipsis rendering, static UI translation audit");
    }
    private static void AuditCatalog()
    {
        var keys = NonetMusicPlayer.Core.Localization.LocalizationCatalog.Keys.ToHashSet(StringComparer.Ordinal);
        Require(keys.Count >= 760, "Feature catalogs are embedded in the shared core");
        foreach (var key in keys)
        {
            Require(Regex.IsMatch(key, @"^[A-Za-z][A-Za-z0-9]*\.[A-Za-z][A-Za-z0-9]*$"), "Stable English resource ID: " + key);
            var values = NonetMusicPlayer.Core.Localization.LocalizationCatalog.Translations(key);
            Require(values.Count == 3 && values.Values.All(v => !string.IsNullOrWhiteSpace(v)), "Complete three-language resource: " + key);
            var placeholders = Regex.Matches(values["zh-CN"], @"\{\d+(?:[^}]*)\}").Select(m => m.Value).Order().ToArray();
            foreach (var value in values.Values) Require(placeholders.SequenceEqual(Regex.Matches(value, @"\{\d+(?:[^}]*)\}").Select(m => m.Value).Order()), "Translation placeholders match: " + key);
        }
        var sourceRoot = Path.GetFullPath("src/NonetMusicPlayer.Desktop");
        foreach (var folder in new[] { "Views", "Controls", "ViewModels" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(sourceRoot, folder), "*.cs", SearchOption.AllDirectories))
        foreach (Match match in Regex.Matches(File.ReadAllText(file), "(?:Ui\\.(?:Text|Button|AsyncButton|Card)|Menu|L10n\\.(?:T|Format))\\(\\s*\"(?<text>(?:[^\"\\\\]|\\\\.)*)\""))
        {
            var text = Regex.Unescape(match.Groups["text"].Value);
            Require(!Regex.IsMatch(text, @"[\p{IsCJKUnifiedIdeographs}]"), "Static UI calls use English resource IDs: " + text);
        }
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.axaml", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
        foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\{ui:Loc Key='(?<text>[^']+)'\}"))
        {
            var text = match.Groups["text"].Value;
            Require(keys.Contains(text), "XAML resource exists: " + text);
        }
    }
    private static ContextMenu ActiveMenu(MainWindow owner) => (ContextMenu)typeof(MainWindow).GetField("_activeMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Pump(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("Beta14: " + message); }
}
