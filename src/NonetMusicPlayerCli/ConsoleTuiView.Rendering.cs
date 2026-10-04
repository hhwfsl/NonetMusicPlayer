using System.Globalization;
using System.Text;
using NonetMusicPlayer.Core.Commands;
using NonetMusicPlayer.Core.Localization;

namespace NonetMusicPlayerCli;

internal sealed partial class ConsoleTuiView
{
    private static readonly string[] HelpSections = ["Controls", "PlayerKeys", "MusicKeys", "PlaylistKeys", "PluginKeys", "SettingsKeys", "FormKeys", "TerminalKeys", "ActionsHelp"];
    internal static string Fit(string value, int width)
    {
        // 元数据内的控制字符不得注入终端转义序列；按显示单元处理东亚字符和组合字符。
        var result = new StringBuilder(); var cells = 0; width = Math.Max(0, width);
        foreach (var rune in value.EnumerateRunes()) { if (Rune.IsControl(rune)) continue; var size = Cells(rune); if (cells + size > width) break; result.Append(rune.ToString()); cells += size; }
        return result + new string(' ', Math.Max(0, width - cells));
    }
    private static int Cells(Rune rune) => rune.Value is >= 0x1100 and <= 0x115f or >= 0x2e80 and <= 0xa4cf or >= 0xac00 and <= 0xd7a3 or >= 0xf900 and <= 0xfaff or >= 0xfe10 and <= 0xfe6f or >= 0xff00 and <= 0xff60 or >= 0x1f300 and <= 0x1faff ? 2 : Rune.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark ? 0 : 1;
    private void Draw(TerminalSnapshot terminal)
    {
        var width = Console.WindowWidth > 0 ? Console.WindowWidth - 1 : 79; var height = Console.WindowHeight > 0 ? Console.WindowHeight : 24;
        var frame = RenderFrame(terminal, width, height);
        if (_lastPaint == frame) return;
        Console.Write(frame); _lastPaint = frame;
    }
    internal string RenderFrame(TerminalSnapshot terminal, int width, int height)
    {
        width = Math.Max(20, width); height = Math.Max(8, height);
        var lines = new List<string>(); var cursorRow = 0; var cursorColumn = 0; var cursorVisible = false;
        void Line(string value = "") => lines.Add(Fit(value, width));
        Line("╭─ Nonet CLI · " + (_view?.Playing == true ? T("Playing") : T("Paused")));
        // 窄终端的导航从当前栏目附近展开，不能把设置或终端截断到永远看不到。
        var labels = Tabs.Select((key, i) => (_page == i ? "[" : " ") + T(key) + (_page == i ? "]" : " ")).ToArray();
        var startTab = 0;
        while (startTab < _page && string.Join(" ", labels.Skip(startTab).Take(_page - startTab + 1)).EnumerateRunes().Sum(Cells) > width - 4) startTab++;
        Line((startTab > 0 ? "‹ " : "") + string.Join(" ", labels.Skip(startTab)));
        Line("│ " + (_view?.Title ?? "—") + " · " + (_view?.Artist ?? ""));
        var barWidth = Math.Clamp(width - 35, 4, 48); var position = _view?.Position ?? 0; var duration = _view?.Duration ?? 0;
        var filled = (int)Math.Clamp(duration > 0 ? position / duration * barWidth : 0, 0, barWidth);
        Line("│ [" + new string('━', filled) + new string('─', barWidth - filled) + $"] {Time(position)} / {Time(duration)}  {_view?.Volume ?? 0:0}%  {T(_view?.Mode ?? "RepeatAll")}");
        Line("├" + new string('─', width - 1));
        var rows = Math.Max(1, height - 8);
        if (_confirmation is not null)
        {
            Line(T("Confirm") + ": " + _confirmationText); Line(T("ConfirmKeys"));
        }
        else if (_result is not null)
        {
            foreach (var text in _result.Skip(_resultOffset).Take(rows)) Line(text);
        }
        else if (_palette)
        {
            Line(T("Actions")); var first = Math.Max(0, _actionIndex - Math.Max(1, rows - 2));
            foreach (var entry in Actions.Skip(first).Take(Math.Max(1, rows - 1)).Select((d, i) => (Definition: d, Index: i + first)))
                Line((entry.Index == _actionIndex ? "▶ " : "  ") + entry.Definition.PublicUsage);
        }
        else if (_form is not null)
        {
            var form = _form; Line(form.Title); var first = Math.Max(0, form.Selected - Math.Max(1, rows - 3));
            for (var i = first; i < form.Fields.Count && lines.Count < height - 3; i++)
            {
                var field = form.Fields[i]; var prefix = (i == form.Selected ? "▶ " : "  ") + field.Label + (field.Optional ? " (?)" : "") + ": ";
                var value = field.Private ? new string('•', field.Value.Length) : field.Value;
                if (field.IsChoice) value = "‹ " + ChoiceLabel(field.Value) + " ›";
                if (i == form.Selected && !field.IsChoice) InputLine(prefix, value, form.Caret); else Line(prefix + value);
            }
            if (form.Error is not null) Line(form.Error);
        }
        else if (Page == "Terminal")
        {
            var history = terminal.Transcript.Replace("\r", "").Split('\n'); _historyOffset = Math.Clamp(_historyOffset, 0, Math.Max(0, history.Length - 1));
            var end = Math.Max(0, history.Length - _historyOffset);
            foreach (var text in history.Take(end).TakeLast(rows - 1)) Line(text);
            while (lines.Count < height - 4) Line();
            var privateInput = CommandSyntax.IsPrivate(terminal.Draft);
            InputLine(terminal.Prompt, privateInput ? LocalizationCatalog.Get("Terminal.PrivateConfig") : terminal.Draft, privateInput ? 0 : _caret);
        }
        else if (Page == "Help")
        {
            foreach (var key in HelpSections.Skip(_selected).Take(rows)) Line(T(key));
        }
        else if (Page == "Statistics")
        {
            Line(T("Listening") + ": " + Time(_view?.ListeningSeconds ?? 0)); Line(T("StatisticsKeys"));
        }
        else
        {
            var items = Items(); _selected = Math.Clamp(_selected, 0, Math.Max(0, items.Count - 1));
            var first = Math.Max(0, _selected - rows + 1);
            if (items.Count == 0) Line(T("Empty"));
            foreach (var pair in items.Skip(first).Take(rows).Select((item, i) => (Item: item, Index: i + first)))
                Line((pair.Index == _selected ? "▶ " : "  ") + (pair.Index + 1) + "  " + pair.Item.Title + "  ·  " + pair.Item.Detail + (_view?.Favorites.Contains(pair.Item.Id) == true ? " ♥" : ""));
        }
        while (lines.Count < height - 3) Line();
        if (_notice is not null && _form is null && _palette == false && _confirmation is null && _result is null && Page != "Terminal") Line("│ " + _notice);
        else Line("├" + new string('─', width - 1));
        Line("╰─ " + (_confirmation is not null ? T("ConfirmKeys") : _result is not null ? T("ResultKeys") : _form is not null ? T("FormKeys") : _palette ? T("ActionKeys") : Page switch { "Terminal" => T("TerminalKeys"), "Settings" => T("SettingsKeys"), "Plugins" => T("PluginKeys"), "Playlists" => T("PlaylistKeys"), "Statistics" => T("StatisticsKeys"), _ => T("MusicKeys") }));
        var frame = "\u001b[?25l\u001b[H" + string.Join("\r\n", lines.Take(height - 1)) + "\u001b[J";
        return cursorVisible ? frame + $"\u001b[{cursorRow};{cursorColumn + 1}H\u001b[?25h" : frame;

        void InputLine(string prefix, string value, int caret)
        {
            var room = Math.Max(4, width - Math.Min(width - 4, prefix.EnumerateRunes().Sum(Cells)));
            var safeCaret = Math.Clamp(caret, 0, value.Length); var start = 0;
            foreach (var boundary in StringInfo.ParseCombiningCharacters(value))
            {
                if (boundary >= safeCaret || value[boundary..safeCaret].EnumerateRunes().Sum(Cells) < room - 1) break;
                start = boundary;
            }
            var visiblePrefix = Fit(prefix, width - room);
            cursorRow = lines.Count + 1; cursorColumn = Math.Min(width - 1, (visiblePrefix + value[start..safeCaret]).EnumerateRunes().Sum(Cells)); cursorVisible = true;
            Line(visiblePrefix + value[start..]);
        }
    }
    private string ChoiceLabel(string value) => _view?.Playlists.Concat(_view.Music).FirstOrDefault(item => item.Id == value)?.Title ?? value;
    private static string Time(double seconds)
    {
        var whole = (long)Math.Clamp(double.IsFinite(seconds) ? seconds : 0, 0, TimeSpan.MaxValue.TotalSeconds);
        return whole >= 3600 ? $"{whole / 3600}:{whole / 60 % 60:00}:{whole % 60:00}" : $"{whole / 60}:{whole % 60:00}";
    }
}
