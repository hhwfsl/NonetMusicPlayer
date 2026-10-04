using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Desktop.Controls;

namespace NonetMusicPlayer.Desktop.Views;

/// <summary>单一终端正文：历史输出只读，命令直接在末尾提示符后编辑。</summary>
public sealed class TerminalView : Border, IDisposable
{
    private readonly TerminalEditor _editor;
    public TerminalView(MainWindow owner)
    {
        Name = "TerminalSurface"; CornerRadius = new(10); ClipToBounds = true;
        Background = Ui.Brush("TerminalBackgroundBrush");
        Child = _editor = new TerminalEditor(owner.Terminal);
    }
    public void Dispose() => _editor.Dispose();

    /// <summary>保留原生文本选择、复制和输入法，但任何编辑都不能改写输出和提示符。</summary>
    private sealed class TerminalEditor : TextBox, IPluginKeyboardScope, IDisposable
    {
        protected override Type StyleKeyOverride => typeof(TextBox);
        private readonly PlayerTerminalSession _session;
        private TerminalSnapshot _snapshot;
        private bool _rendering, _disposed;
        public TerminalEditor(PlayerTerminalSession session)
        {
            _session = session; _snapshot = session.Snapshot;
            Name = "TerminalOutput"; AcceptsReturn = true; IsUndoEnabled = false;
            TextWrapping = TextWrapping.Wrap; FontFamily = new("Cascadia Mono, Consolas, DejaVu Sans Mono, monospace");
            FontSize = 14; Padding = new(16); VerticalContentAlignment = VerticalAlignment.Top;
            HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Stretch;
            Background = Brushes.Transparent; BorderBrush = Brushes.Transparent; BorderThickness = new(0); CornerRadius = new(0);
            FocusAdorner = null; ContextMenu = null;
            FullTextToolTips.SetEnabled(this, false); ToolTip.SetTip(this, null);
            // Fluent 的悬停/焦点边框也取消，只保留真正终端中的插入点和主动文本选择。
            Resources["TextControlBackground"] = Brushes.Transparent;
            Resources["TextControlBackgroundPointerOver"] = Brushes.Transparent;
            Resources["TextControlBackgroundFocused"] = Brushes.Transparent;
            Resources["TextControlBorderBrush"] = Brushes.Transparent;
            Resources["TextControlBorderBrushPointerOver"] = Brushes.Transparent;
            Resources["TextControlBorderBrushFocused"] = Brushes.Transparent;
            TextChanging += Edited; _session.Changed += SessionChanged;
            Render(true);
        }

        private void SessionChanged(object? sender, EventArgs e)
        {
            if (_rendering) return;
            if (Dispatcher.UIThread.CheckAccess()) Render(false);
            else Dispatcher.UIThread.Post(() => { if (!_disposed) Render(false); });
        }
        private void Render(bool end)
        {
            var next = _session.Snapshot;
            var offset = Math.Max(0, CaretIndex - _snapshot.Prefix.Length);
            _snapshot = next; _rendering = true;
            try
            {
                FontSize = _session.Options.FontSize;
                if (Text != next.Text) Text = next.Text;
                IsReadOnly = !next.AcceptsInput;
                CaretIndex = Math.Clamp(next.Prefix.Length + (end ? next.Draft.Length : offset), 0, next.Text.Length);
                SelectionStart = SelectionEnd = CaretIndex;
            }
            finally { _rendering = false; }
        }
        private void Edited(object? sender, TextChangingEventArgs e)
        {
            if (_rendering) return;
            var text = Text ?? "";
            if (!_snapshot.AcceptsInput || !text.StartsWith(_snapshot.Prefix, StringComparison.Ordinal)) { Render(true); return; }
            var draft = text[_snapshot.Prefix.Length..];
            // TextChanging 在插入点更新前触发；不要同步重新写 Text，否则输入法和光标会跳动。
            _rendering = true;
            try { _session.SetDraft(draft); _snapshot = _session.Snapshot; }
            finally { _rendering = false; }
            if (draft != _snapshot.Draft) Dispatcher.UIThread.Post(() => Render(true));
        }
        private void PrepareEdit()
        {
            if (Math.Min(SelectionStart, SelectionEnd) < _snapshot.Prefix.Length)
                SelectionStart = SelectionEnd = CaretIndex = _snapshot.Text.Length;
        }
        protected override void OnTextInput(TextInputEventArgs e) { PrepareEdit(); base.OnTextInput(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            var control = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (e.Key == Key.Enter)
            {
                e.Handled = true; _ = Submit(); return;
            }
            if (e.Key is Key.Up or Key.Down && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                _session.Recall(e.Key == Key.Up ? -1 : 1); Render(true); e.Handled = true; return;
            }
            if (control && e.Key == Key.C && SelectionStart == SelectionEnd) { _session.CancelInput(); e.Handled = true; return; }
            if (control && e.Key == Key.L) { _ = _session.ClearAsync(); e.Handled = true; return; }
            if (e.Key == Key.Escape) { _session.SetDraft(""); e.Handled = true; return; }
            if (e.Key == Key.Home && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { CaretIndex = _snapshot.Prefix.Length; e.Handled = true; return; }
            if ((e.Key == Key.Back || e.Key == Key.Left) && SelectionStart == SelectionEnd && CaretIndex <= _snapshot.Prefix.Length) { e.Handled = true; return; }
            if (e.Key is Key.Back or Key.Delete || control && e.Key is Key.V or Key.X) PrepareEdit();
            base.OnKeyDown(e);
        }
        private async Task Submit() { await _session.SubmitAsync(); if (!_disposed) Render(true); }
        public void Dispose() { _disposed = true; _session.Changed -= SessionChanged; TextChanging -= Edited; }
    }
}
