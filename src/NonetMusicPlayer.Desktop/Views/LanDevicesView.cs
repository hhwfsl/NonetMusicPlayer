using Avalonia.Controls;
using Avalonia.Input;
using NonetMusicPlayer.Desktop.Services;
using NonetMusicPlayer.Desktop.ViewModels;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>配对界面不显示或保存会话认证；仅复制五分钟有效的邀请。</summary>
public sealed class LanDevicesView : UserControl, IDisposable
{
    private readonly MainViewModel _vm;
    private readonly MainWindow _owner;
    private readonly StackPanel _body = Ui.Stack();
    private string _draft = "";
    public LanDevicesView(MainWindow owner, MainViewModel vm) { _vm = vm; _owner = owner; Content = Ui.Scroll(_body); vm.LanChanged += Refresh; Refresh(null, EventArgs.Empty); }
    private void Refresh(object? sender, EventArgs e)
    {
        _body.Children.Clear();
        var sharing = Ui.Toggle(_vm.LanSharing, _vm.SetLanSharing);
        var card = Ui.Card(L10n.T("Lan.Share"), Ui.Row(L10n.T("Lan.Share"), L10n.T("Lan.Privacy"), sharing));
        var contents = (StackPanel)card.Child!;
        if (_vm.LanSharing)
        {
            foreach (var invitation in _vm.LanInvitations)
            {
                var address = Core.Network.LanAddress.ParseInvitation(invitation).Origin.Authority;
                contents.Children.Add(Ui.Row(address, L10n.T("Lan.InvitationHint"), Ui.AsyncButton(L10n.T("Lan.CopyInvitation"), async () => { if (_owner.Clipboard is { } clipboard) { var data = new DataTransfer(); data.Add(DataTransferItem.CreateText(invitation)); await clipboard.SetDataAsync(data); } })));
            }
            contents.Children.Add(Ui.Button(L10n.T("Lan.RenewInvitation"), _vm.RenewLanInvitation));
        }
        _body.Children.Add(card);
        var input = new TextBox { Text = _draft, PlaceholderText = "nonet://connect?…", AcceptsReturn = true, MinHeight = 72, MaxHeight = 140, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        input.TextChanged += (_, _) => _draft = input.Text ?? "";
        _body.Children.Add(Ui.Card(L10n.T("Lan.Connect"), Ui.Text(L10n.T("Lan.ConnectHint"), 13, true), input, Ui.AsyncButton(L10n.T("Lan.Connect"), async () => { var invitation = _draft; _draft = ""; await _vm.ConnectLanAsync(invitation); })));
        foreach (var peer in _vm.LanPeers)
        {
            var lists = Ui.Stack(Ui.Row(peer.Name, L10n.Format("Lan.TrackCount", peer.Catalog.Total), Ui.Button(L10n.T("Lan.Disconnect"), () => _vm.DisconnectLan(peer))));
            foreach (var list in peer.Catalog.Playlists)
                lists.Children.Add(Ui.Button($"{list.Name}  ·  {list.TrackIds.Length}", () => _vm.Navigate($"lan:{peer.DeviceId}:{list.Id}", list.Name)));
            _body.Children.Add(Ui.Card(peer.Name, lists));
        }
    }
    public void Dispose() => _vm.LanChanged -= Refresh;
}
