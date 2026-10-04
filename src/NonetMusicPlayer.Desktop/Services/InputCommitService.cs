using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>编辑文本作为草稿，仅在回车或失焦时提交。</summary>
public static class InputCommitService
{
    public static void Install(Window window)
    {
        window.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (window.FocusManager?.GetFocusedElement() is not TextBox focused) return;
            var source = e.Source as Avalonia.Visual;
            if (source == focused || source?.GetVisualAncestors().Contains(focused) == true) return;
            var numeric = focused.GetVisualAncestors().OfType<NumericUpDown>().FirstOrDefault();
            if (numeric is not null && (source == numeric || source?.GetVisualAncestors().Contains(numeric) == true)) return;
            window.FocusManager.Focus(null);
        }, RoutingStrategies.Tunnel, true);
        window.Deactivated += (_, _) => window.FocusManager?.Focus(null);
    }

    public static void Bind(TextBox editor, Action<string> commit)
    {
        var applied = editor.Text ?? "";
        editor.GotFocus += (_, _) => applied = editor.Text ?? "";
        void Apply()
        {
            var candidate = editor.Text ?? "";
            if (candidate == applied) return;
            try { commit(candidate); applied = editor.Text ?? candidate; }
            catch (Exception error)
            {
                editor.Text = applied;
                if (TopLevel.GetTopLevel(editor)?.DataContext is ViewModels.MainViewModel vm) vm.ReportError(L10n.T("Common.InputNotApplied"), error);
            }
        }
        editor.LostFocus += (_, _) => Apply();
        editor.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter || editor.AcceptsReturn && !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            Apply(); TopLevel.GetTopLevel(editor)?.FocusManager?.Focus(null); e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }

    public static void Bind(NumericUpDown editor, Action<decimal> commit)
    {
        var applying = false; var applied = editor.Value ?? 0;
        editor.GotFocus += (_, _) => applied = editor.Value ?? 0;
        void Apply()
        {
            if (applying) return;
            var input = editor.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            var text = input?.Text ?? editor.Text;
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var candidate) || candidate < editor.Minimum || candidate > editor.Maximum)
            {
                applying = true; try { editor.Value = applied; editor.Text = applied.ToString(editor.FormatString, CultureInfo.CurrentCulture); } finally { applying = false; }
                return;
            }
            applying = true;
            try { editor.Value = candidate; if (candidate != applied) { commit(candidate); applied = candidate; } }
            catch (Exception error) { editor.Value = applied; if (TopLevel.GetTopLevel(editor)?.DataContext is ViewModels.MainViewModel vm) vm.ReportError(L10n.T("Common.InputNotApplied"), error); }
            finally { applying = false; }
        }
        editor.LostFocus += (_, _) => Apply();
        editor.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (e.Key != Key.Enter) return; Apply(); TopLevel.GetTopLevel(editor)?.FocusManager?.Focus(null); e.Handled = true; }, RoutingStrategies.Tunnel);
    }
}
