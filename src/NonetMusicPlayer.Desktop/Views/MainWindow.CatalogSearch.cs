using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using NonetMusicPlayer.Desktop.Controls;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Views;

public sealed partial class MainWindow
{
    private string _catalogQuery = "";
    private Grid? _catalogSearchField;
    private bool PlaylistCatalogPage => _vm?.Page is "playlists" or "interface:playlists";
    internal static bool CatalogMatches(string name, string query) => query.Length == 0 || L10n.Culture.CompareInfo.IndexOf(name, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    private void BuildCatalogSearchHeader(bool navigated)
    {
        if (navigated) _catalogQuery = "";
        var visible = _vm?.Page is "albums" or "artists" || PlaylistCatalogPage;
        CatalogSearchHeader.IsVisible = visible; CatalogSearchHeader.Content = null; _catalogSearchField = null;
        if (!visible) return;
        PageBackToTop.IsVisible = true;
        var key = PlaylistCatalogPage ? "MusicSearch.Playlists" : _vm!.Page == "albums" ? "MusicSearch.Albums" : "MusicSearch.Artists";
        var editor = new TextBox { Name = "CatalogSearch", Text = _catalogQuery, PlaceholderText = L10n.T(key), MaxLength = 300, MinHeight = 40, Padding = new(34, 0, 32, 0) };
        editor.Classes.Add("web-search");
        var field = _catalogSearchField = new Grid { Name = "CatalogSearchField", Width = CatalogSearchWidth(), Height = 40, ClipToBounds = true };
        field.Children.Add(editor);
        var page = _vm!.Page;
        void Search()
        {
            if (_vm?.Page != page || !ReferenceEquals(_catalogSearchField, field)) return;
            var query = (editor.Text ?? "").Trim();
            if (query == _catalogQuery) return;
            _catalogQuery = query; ApplyCatalogQuery();
        }
        InputCommitService.Bind(editor, _ => Search());
        var search = Ui.IconButton(IconKind.Search, key, () => { Search(); FocusManager?.Focus(null); });
        search.Name = "CatalogSearchButton"; search.Classes.Add("search-icon"); search.Width = 32; search.Height = 36; search.HorizontalAlignment = HorizontalAlignment.Left; search.Cursor = new(StandardCursorType.Hand);
        var clear = Ui.IconButton(IconKind.Close, "Common.Clear", () => { editor.Text = ""; Search(); FocusManager?.Focus(null); });
        clear.Name = "CatalogSearchClear"; clear.Classes.Add("search-icon"); clear.Width = 30; clear.Height = 36; clear.HorizontalAlignment = HorizontalAlignment.Right; clear.Cursor = new(StandardCursorType.Hand);
        field.Children.Add(search); field.Children.Add(clear);
        var row = Ui.Actions(field); row.Spacing = 8;
        if (PlaylistCatalogPage)
        {
            var add = Ui.IconButton(IconKind.Plus, "Playlists.NewPlaylist", () => CreatePlaylist_Click(null, new())); add.Name = "CatalogCreatePlaylist";
            row.Children.Add(add);
        }
        CatalogSearchHeader.Content = row;
    }
    private double CatalogSearchWidth() => Math.Clamp(ViewportWidth * .22, 140, 300);
    private void UpdateCatalogSearchWidth() => _catalogSearchField?.SetCurrentValue(WidthProperty, CatalogSearchWidth());
    private void ApplyCatalogQuery(bool initial = false)
    {
        if (_vm is null) return;
        if (_vm.Page is "albums" or "artists")
        {
            // 只重建结果视口，不替换标题输入框，避免丢失焦点、光标或误搜索歌曲。
            if (initial) return;
            _groupList?.Dispose(); _groupList = null;
            AlternatePage.Content = Groups(_vm.Page == "artists");
        }
        else if (PlaylistCatalogPage && AlternatePage.Content is Control content)
        {
            foreach (var gallery in content.GetVisualDescendants().Prepend(content).OfType<InterfacePlaylistGallery>()) gallery.SetFilter(_catalogQuery);
        }
    }
}
