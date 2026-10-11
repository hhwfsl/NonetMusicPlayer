using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private string _selectedMusicSource = "", _homeMusicQuery = "";
    private void MusicSearchStateChanged(object? sender, EventArgs e) => BuildMusicSearchHeader();
    /// <summary>来源来自音源声明，不包含供应商专用分支；未启用或卸载的音源立即退出列表。</summary>
    private void BuildMusicSearchHeader()
    {
        if (_vm is null) return;
        MusicSearchHeader.IsVisible = _vm.Page is "library" or "cloud-search";
        if (!MusicSearchHeader.IsVisible) { MusicSearchHeader.Content = null; return; }
        var sources = new[] { (Id: "", Label: L10n.T("Common.Local")) }.Concat(_vm.Plugins.MusicSearchSources.Select(p => (p.Id, p.MusicSearch!.Label))).ToArray();
        if (sources.All(s => s.Id != _selectedMusicSource)) _selectedMusicSource = "";
        var select = new ComboBox { Name = "MusicSearchSource", ItemsSource = sources.Select(s => s.Label).ToArray(), SelectedIndex = Array.FindIndex(sources, s => s.Id == _selectedMusicSource), Width = 112, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(select, L10n.T("MusicSearch.Source"));
        var editor = new TextBox { Name = "HomeMusicSearch", Text = _homeMusicQuery, PlaceholderText = L10n.T("MusicSearch.Placeholder"), MaxLength = 300, MinHeight = 40, Padding = new(34, 0, 32, 0) };
        editor.Classes.Add("web-search");
        var field = new Grid { Width = Math.Clamp(Bounds.Width * .22, 140, 280), Height = 40, ClipToBounds = true }; field.Children.Add(editor);
        async Task Search()
        {
            _homeMusicQuery = editor.Text ?? ""; FocusManager?.Focus(null);
            await _vm.SearchMusicAsync(_selectedMusicSource, _homeMusicQuery);
        }
        var search = Ui.AsyncButton("MusicSearch.Search", Search); search.Name = "HomeMusicSearchButton";
        search.Classes.Add("search-icon"); search.Content = new VectorIcon { Kind = IconKind.Search, Width = 18, Height = 18 };
        search.Width = 32; search.Height = 36; search.HorizontalAlignment = HorizontalAlignment.Left; search.Cursor = new(StandardCursorType.Hand);
        var clear = Ui.Button("Common.Clear", () => { editor.Text = ""; _homeMusicQuery = ""; FocusManager?.Focus(null); }); clear.Classes.Add("search-icon");
        clear.Content = new VectorIcon { Kind = IconKind.Close, Width = 16, Height = 16 }; clear.Width = 30; clear.Height = 36; clear.HorizontalAlignment = HorizontalAlignment.Right; clear.Cursor = new(StandardCursorType.Hand);
        field.Children.Add(search); field.Children.Add(clear);
        editor.TextChanged += (_, _) => _homeMusicQuery = editor.Text ?? "";
        editor.KeyDown += async (_, e) => { if (e.Key != Key.Enter) return; e.Handled = true; await Search(); };
        select.SelectionChanged += (_, _) => { if (select.SelectedIndex >= 0) _selectedMusicSource = sources[select.SelectedIndex].Id; };
        var row = Ui.Actions(field, select); row.Spacing = 8;
        if (_vm.Page == "cloud-search" && _vm.HasMoreMusicResults)
        {
            var next = Ui.AsyncButton("MusicSearch.NextPage", () => _vm.SearchMusicAsync(_vm.LastMusicSearchSource, _vm.LastMusicSearchQuery, true));
            next.Content = new VectorIcon { Kind = IconKind.Next }; next.Width = 40; next.IsEnabled = !_vm.MusicSearchBusy; row.Children.Add(next);
        }
        search.IsEnabled = !_vm.MusicSearchBusy; MusicSearchHeader.Content = row;
    }
}
