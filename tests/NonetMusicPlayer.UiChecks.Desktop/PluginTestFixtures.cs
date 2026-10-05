using System.IO.Compression;
using System.Text;

/// <summary>仅供通用宿主回归的最小协议夹具；不引用、复制或分发用户的插件项目。</summary>
internal static class PluginTestFixtures
{
    internal const string PetManifest = """
        {"id":"tests.native-pet","name":"Contract pet fixture","version":"1.0.0","contractVersion":1,"type":"ui",
        "permissions":["player-control", "navigation", "statistics", "desktop-widget"],"pageEntry":"page.json"}
        """;
    internal const string PetPage = """
        {"schemaVersion":1,"title":"Contract pet fixture","widgets":[
        {"type":"pet","name":"Fixture","text":"Ready","body":"#B8DCF2","floating":true,"actions":[
        {"label":"Common.Previous","action":{"kind": "previous"}},
        {"label":"Playback.PlayPause","action":{"kind": "play-pause"}},
        {"label":"Common.Next","action":{"kind": "next"}},
        {"label":"Common.Like","action":{"kind": "favorite"}},
        {"label":"Search","action":{"kind": "search","value":"music"}}]},
        {"type":"listening-summary","title":"Statistics"}],"flows":[
        {"event":"track-changed","action":{"kind": "show-message", "value": "正在播放：{title} · {artist}"}},
        {"event":"interval","intervalSeconds":120,"action":{"kind": "show-message","value":"{title}"}}]}
        """;
    internal static string Pet(string output) => Create(output, PetManifest, PetPage);
    internal static string Snake(string output) => Create(output,
        """{"id":"tests.native-snake","name":"贪吃蛇小游戏测试插件","navigationLabel":"贪吃蛇小游戏测试插件","version":"1.0.0","contractVersion":1,"type":"ui","permissions":[],"pageEntry":"page.json"}""",
        """{"schemaVersion":1,"title":"Contract game fixture","widgets":[{"type":"snake","columns":24,"rows":18,"tickMilliseconds":150}]}""");
    internal static string Timing(string output) => Create(output,
        """{"id":"tests.native-timing","name":"Contract timing fixture","version":"1.0.0","contractVersion":1,"type":"ui","permissions":["navigation","player-control","lyrics-editor"],"pageEntry":"page.json","menuContributions":[{"location":"lyrics.more","label":"精准歌词工具","action":"open-page"}]}""",
        """{"schemaVersion":1,"title":"Contract timing fixture","widgets":[{"type":"lyrics-timing"}]}""");
    private static string Create(string output, string manifest, string page)
    {
        Directory.CreateDirectory(output);
        var path = Path.Combine(Path.GetFullPath(output), "contract-fixture-" + Guid.NewGuid().ToString("N") + ".impp");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write("manifest.json", manifest); Write("page.json", page); return path;
        void Write(string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text); }
    }
}
