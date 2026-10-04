using System.Globalization;
using System.Text;
using System.Text.Json;
using NonetMusicPlayer.Desktop.Models;

namespace NonetMusicPlayer.Desktop.Services;

/// <summary>先验证用户 JSON 布局，再替换当前可用布局；失败时保留可恢复版本。</summary>
public sealed class UiLayoutService
{
    public const int MaximumBytes = 100 * 1024;
    public const int CurrentSchemaVersion = 2;
    private static readonly string[] RequiredIds = ["artwork", "title", "artist", "previous", "play", "next", "time", "mode", "favorite", "more", "volume", "progress"];
    private static readonly string[] WorkspaceIds = ["Navigation", "Content", "Player"];
    public string Path { get; }
    private string _backupFolder;
    public string BackupPath => System.IO.Path.Combine(_backupFolder, "active.layout.json.bak");
    public string LegacyBackupPath => System.IO.Path.Combine(_backupFolder, "active.layout.json.schema1.bak");
    public UiLayoutDocument Current { get; private set; }
    public string AppliedJson { get; private set; } = DefaultJson;
    public string? LastError { get; private set; }
    public string? MigrationMessage { get; private set; }
    public bool HasBackup => File.Exists(BackupPath);
    public bool HasLegacyBackup => File.Exists(LegacyBackupPath);

    public UiLayoutService(AppStorage storage)
    {
        Path = System.IO.Path.Combine(storage.Root, "Layouts", "active.layout.json");
        _backupFolder = storage.BackupFolder;
        ImportOldBackup(Path + ".bak", BackupPath); ImportOldBackup(Path + ".schema1.bak", LegacyBackupPath);
        Current = Read(DefaultJson);
        if (!File.Exists(Path))
        {
            try { Apply(DefaultJson); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { LastError = "默认布局已在内存中加载，但配置文件暂时无法保存：" + error.Message; }
            return;
        }
        try { Reload(); }
        catch (Exception e) when (e is UiLayoutException or IOException or UnauthorizedAccessException)
        {
            LastError = "布局配置无法使用，原文件已保留。" + e.Message;
            if (!File.Exists(BackupPath)) return;
            try { var backupJson = ReadFile(BackupPath); Current = Read(backupJson); AppliedJson = NormalizeJson(backupJson); LastError += " 已使用上次有效备份。"; }
            catch (Exception backupError) when (backupError is UiLayoutException or IOException or UnauthorizedAccessException) { LastError += " 已使用默认布局。"; }
        }
    }

    public UiLayoutDocument Read(string json) => Parse(json, out _);

    /// <summary>保留版本 2 文本，无需反射即可迁移合法版本 1 布局。</summary>
    public string NormalizeJson(string json)
    {
        var document = Parse(json, out var sourceVersion);
        return sourceVersion == CurrentSchemaVersion ? json : WriteDocument(document);
    }

    private static UiLayoutDocument Parse(string json, out int sourceVersion)
    {
        sourceVersion = CurrentSchemaVersion;
        if (json is null) throw Error("$", "配置内容不能为空。");
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw Error("$", "配置文件不能超过 100 KB。");
        try
        {
            // 单个布局网格含多层 JSON 对象；解析器预留深度，再单独验证四层网格限制。
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 32 });
            var root = parsed.RootElement;
            CheckObject(root, "$", "schemaVersion", "workspace", "player");
            var version = Integer(Required(root, "schemaVersion", "$"), "$.schemaVersion");
            if (version is not (1 or CurrentSchemaVersion)) throw Error("$.schemaVersion", $"暂不支持版本 {version}，请使用 2；版本 1 可以自动迁移。");
            sourceVersion = version;
            var workspace = ReadWorkspace(root.TryGetProperty("workspace", out var value) ? value : default);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var count = 0;
            var player = ReadPlayer(Required(root, "player", "$"), "$.player", 1, ids, ref count, version == 1);
            foreach (var id in RequiredIds) if (!ids.Contains(id)) throw Error("$.player.items", $"缺少必要控件“{id}”。");
            return new UiLayoutDocument { SchemaVersion = CurrentSchemaVersion, Workspace = workspace, Player = player };
        }
        catch (JsonException e)
        {
            throw new UiLayoutException($"JSON 格式错误：第 {(e.LineNumber ?? 0) + 1} 行，第 {(e.BytePositionInLine ?? 0) + 1} 字节。请检查引号、括号和逗号。");
        }
    }

    public void Apply(string json, string? previousUsableJson = null)
    {
        var candidate = Parse(json, out var sourceVersion);
        var normalized = sourceVersion == CurrentSchemaVersion ? json : WriteDocument(candidate);
        if (previousUsableJson is not null)
        {
            // 磁盘语法合法不等于布局可用；备份最后实际可用的布局，不覆盖为外部编辑的不可用文件。
            var previousNormalized = NormalizeJson(previousUsableJson);
            if (sourceVersion == 1) PreserveLegacyText(json);
            AppStorage.AtomicWrite(BackupPath, previousNormalized);
            AppStorage.AtomicWrite(Path, normalized);
            Current = candidate;
            AppliedJson = normalized;
            LastError = null;
            MigrationMessage = sourceVersion == 1 ? "旧版布局已升级为版本 2；歌词入口保留在封面，原始配置保存在 .schema1.bak。" : null;
            return;
        }
        // 手工编辑的无效文件不得覆盖最后有效备份。
        var backup = false;
        if (File.Exists(Path))
        {
            try { Read(ReadFile(Path)); backup = true; }
            catch (Exception e) when (e is UiLayoutException or IOException or UnauthorizedAccessException) { }
        }
        if (sourceVersion == 1) PreserveLegacyText(json);
        if (backup) AppStorage.AtomicWrite(BackupPath, ReadFile(Path));
        AppStorage.AtomicWrite(Path, normalized);
        Current = candidate;
        AppliedJson = normalized;
        LastError = null;
        MigrationMessage = sourceVersion == 1 ? "旧版布局已升级为版本 2；歌词入口保留在封面，原始配置保存在 .schema1.bak。" : null;
    }

    public void RestoreDefault(string? previousUsableJson = null) => Apply(DefaultJson, previousUsableJson);

    public void RestoreBackup(string? previousUsableJson = null)
    {
        if (!HasBackup) throw Error("$", "还没有布局备份；首次成功修改后会保存上次有效配置。");
        Apply(ReadFile(BackupPath), previousUsableJson);
    }

    public void Reload()
    {
        var json = ReadFile(Path);
        var candidate = Parse(json, out var sourceVersion);
        var normalized = sourceVersion == CurrentSchemaVersion ? json : WriteDocument(candidate);
        Current = candidate;
        AppliedJson = normalized;
        LastError = null;
        MigrationMessage = null;
        if (sourceVersion != 1) return;
        try
        {
            PreserveLegacyText(json);
            // 规范升级不能用旧活动文件替换已有可用的 .bak。
            AppStorage.AtomicWrite(Path, normalized);
            MigrationMessage = "旧版布局已升级为版本 2；只移除独立歌词按钮，原始配置保存在 .schema1.bak。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LastError = "旧版布局已在内存中兼容，但升级后的配置暂时无法保存。原文件未覆盖：" + error.Message;
        }
    }

    private void PreserveLegacyText(string json)
    {
        if (!File.Exists(LegacyBackupPath)) AppStorage.AtomicWrite(LegacyBackupPath, json);
    }

    public void SetBackupFolder(string folder)
    {
        var oldBackup = BackupPath; var oldLegacy = LegacyBackupPath;
        _backupFolder = System.IO.Path.GetFullPath(folder);
        ImportOldBackup(oldBackup, BackupPath); ImportOldBackup(oldLegacy, LegacyBackupPath);
    }
    private static void ImportOldBackup(string oldPath, string newPath)
    {
        if (File.Exists(newPath) || !File.Exists(oldPath)) return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(newPath)!); File.Copy(oldPath, newPath, false);
    }

    private static string WriteDocument(UiLayoutDocument document)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", CurrentSchemaVersion);
            writer.WriteStartObject("workspace");
            foreach (var (id, rect) in document.Workspace)
            {
                writer.WriteStartObject(id); writer.WriteNumber("x", rect.X); writer.WriteNumber("y", rect.Y);
                writer.WriteNumber("width", rect.Width); writer.WriteNumber("height", rect.Height); writer.WriteEndObject();
            }
            writer.WriteEndObject(); writer.WritePropertyName("player"); WriteGrid(writer, document.Player); writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteGrid(Utf8JsonWriter writer, UiPlayerLayout grid)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("rows"); foreach (var row in grid.Rows) writer.WriteStringValue(row); writer.WriteEndArray();
        writer.WriteStartArray("columns"); foreach (var column in grid.Columns) writer.WriteStringValue(column); writer.WriteEndArray();
        writer.WriteStartArray("items");
        foreach (var item in grid.Items)
        {
            writer.WriteStartObject();
            if (item.Id is not null) writer.WriteString("id", item.Id);
            if (item.Grid is { } child) { writer.WritePropertyName("grid"); WriteGrid(writer, child); }
            writer.WriteNumber("row", item.Row); writer.WriteNumber("column", item.Column);
            writer.WriteNumber("rowSpan", item.RowSpan); writer.WriteNumber("columnSpan", item.ColumnSpan);
            writer.WriteStartArray("margin"); foreach (var edge in item.Margin) writer.WriteNumberValue(edge); writer.WriteEndArray();
            writer.WriteString("horizontalAlignment", item.HorizontalAlignment); writer.WriteString("verticalAlignment", item.VerticalAlignment);
            if (item.FontSize is { } fontSize) writer.WriteNumber("fontSize", fontSize);
            if (item.Width is { } width) writer.WriteNumber("width", width);
            if (item.Height is { } height) writer.WriteNumber("height", height);
            writer.WriteEndObject();
        }
        writer.WriteEndArray(); writer.WriteEndObject();
    }

    private static string ReadFile(string path)
    {
        if (new FileInfo(path).Length > MaximumBytes) throw Error("$", "配置文件不能超过 100 KB。");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static Dictionary<string, LayoutRect> ReadWorkspace(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined) return [];
        CheckObject(element, "$.workspace", WorkspaceIds);
        var result = new Dictionary<string, LayoutRect>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var path = "$.workspace." + property.Name;
            CheckObject(property.Value, path, "x", "y", "width", "height");
            var rect = new LayoutRect(Number(Required(property.Value, "x", path), path + ".x"), Number(Required(property.Value, "y", path), path + ".y"),
                Number(Required(property.Value, "width", path), path + ".width"), Number(Required(property.Value, "height", path), path + ".height"));
            if (!rect.IsValid) throw Error(path, "位置和大小使用 0～1 的比例；宽至少 0.12，高至少 0.1，且不能超出工作区。");
            result.Add(property.Name, rect);
        }
        if (result.Count == 0) return result;
        foreach (var id in WorkspaceIds) if (!result.ContainsKey(id)) throw Error("$.workspace", $"自定义工作区必须同时包含 Navigation、Content、Player，缺少 {id}。");
        var entries = result.ToArray();
        for (var i = 0; i < entries.Length; i++)
            for (var j = i + 1; j < entries.Length; j++)
            {
                var a = entries[i].Value; var b = entries[j].Value;
                if (a.X < b.X + b.Width - .000001 && b.X < a.X + a.Width - .000001 && a.Y < b.Y + b.Height - .000001 && b.Y < a.Y + a.Height - .000001)
                    throw Error("$.workspace", $"{entries[i].Key} 与 {entries[j].Key} 重叠，请分配独立区域。");
            }
        return result;
    }

    private static UiPlayerLayout ReadPlayer(JsonElement player, string gridPath, int depth, HashSet<string> ids, ref int count, bool legacy)
    {
        if (depth > 4) throw Error(gridPath, "嵌套网格最多为 4 层。");
        CheckObject(player, gridPath, "rows", "columns", "items");
        var rows = ReadTracks(Required(player, "rows", gridPath), gridPath + ".rows");
        var columns = ReadTracks(Required(player, "columns", gridPath), gridPath + ".columns");
        var values = Required(player, "items", gridPath);
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > 128) throw Error(gridPath + ".items", "应为 1～128 项的控件数组。");
        var result = new List<UiLayoutItem>();
        var occupied = new string?[rows.Length, columns.Length];
        var index = 0;
        foreach (var item in values.EnumerateArray())
        {
            var path = $"{gridPath}.items[{index++}]";
            if (++count > 128) throw Error(path, "布局最多为 128 项（包括容器）。");
            CheckObject(item, path, "id", "grid", "row", "column", "rowSpan", "columnSpan", "margin", "horizontalAlignment", "verticalAlignment", "fontSize", "width", "height");
            var hasId = item.TryGetProperty("id", out var idValue);
            var hasGrid = item.TryGetProperty("grid", out var gridValue);
            if (hasId == hasGrid) throw Error(path, "每一项必须且只能指定 id 或 grid，不能同时指定。");
            string? id = null; UiPlayerLayout? grid = null;
            if (hasId)
            {
                id = String(idValue, path + ".id");
                if (!RequiredIds.Contains(id, StringComparer.Ordinal) && !(legacy && id == "lyrics"))
                    throw Error(path + ".id", id == "lyrics" ? "版本 2 已移除独立歌词按钮，请点击封面打开歌词；旧配置请保留 schemaVersion 为 1 以便迁移。" : $"未知控件“{id}”；只允许内置播放器控件。");
                if (!ids.Add(id)) throw Error(path + ".id", $"控件“{id}”重复，每个控件只能出现一次。");
            }
            else grid = ReadPlayer(gridValue, path + ".grid", depth + 1, ids, ref count, legacy);
            var row = Integer(Required(item, "row", path), path + ".row");
            var column = Integer(Required(item, "column", path), path + ".column");
            var rowSpan = OptionalInteger(item, "rowSpan", path, 1);
            var columnSpan = OptionalInteger(item, "columnSpan", path, 1);
            if (row < 0 || column < 0 || rowSpan < 1 || columnSpan < 1 || rowSpan > rows.Length || columnSpan > columns.Length || row > rows.Length - rowSpan || column > columns.Length - columnSpan)
                throw Error(path, "行列索引从 0 开始，跨度至少为 1，控件不能超出网格。");
            var margin = item.TryGetProperty("margin", out var marginValue) ? ReadMargin(marginValue, path + ".margin") : new double[4];
            var horizontal = OptionalString(item, "horizontalAlignment", path, "Stretch");
            var vertical = OptionalString(item, "verticalAlignment", path, "Center");
            if (horizontal is not ("Stretch" or "Left" or "Center" or "Right")) throw Error(path + ".horizontalAlignment", "只允许 Stretch、Left、Center、Right。");
            if (vertical is not ("Stretch" or "Top" or "Center" or "Bottom")) throw Error(path + ".verticalAlignment", "只允许 Stretch、Top、Center、Bottom。");
            double? fontSize = item.TryGetProperty("fontSize", out var fontValue) ? Number(fontValue, path + ".fontSize") : null;
            if (fontSize is < 10 or > 32) throw Error(path + ".fontSize", "字号应在 10～32 之间。");
            var width = OptionalDimension(item, "width", path, id);
            var height = OptionalDimension(item, "height", path, id);
            if (legacy)
            {
                // 当前默认按钮变大时，仍保持版本 1 布局的隐式尺寸。
                var legacyButtonSize = id switch { "artwork" => 60d, "play" => 42d, "previous" or "next" or "mode" or "favorite" or "more" or "volume" or "lyrics" => 36d, _ => (double?)null };
                width ??= legacyButtonSize;
                height ??= id == "progress" ? 32d : legacyButtonSize;
                fontSize ??= id switch { "title" => 16d, "artist" => 12d, "time" => 11d, _ => (double?)null };
            }
            var label = id ?? "子网格";
            for (var r = row; r < row + rowSpan; r++)
                for (var c = column; c < column + columnSpan; c++)
                {
                    if (occupied[r, c] is { } previous) throw Error(path, $"控件“{label}”与“{previous}”占用了同一个网格单元，请修改行列或跨度。");
                    occupied[r, c] = label;
                }
            // 移除旧按钮前先验证原网格占用；仅由移除产生的空容器可删，父级行列定义保持不变。
            if (id == "lyrics" || grid is { Items.Count: 0 }) continue;
            result.Add(new UiLayoutItem { Id = id, Grid = grid, Row = row, Column = column, RowSpan = rowSpan, ColumnSpan = columnSpan, Margin = margin, HorizontalAlignment = horizontal, VerticalAlignment = vertical, FontSize = fontSize, Width = width, Height = height });
        }
        return new UiPlayerLayout { Rows = rows, Columns = columns, Items = result };
    }

    private static string[] ReadTracks(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() is < 1 or > 24) throw Error(path, "网格行列应为数组，数量应为 1～24。");
        var result = new List<string>();
        foreach (var value in element.EnumerateArray())
        {
            var itemPath = $"{path}[{result.Count}]"; var text = String(value, itemPath);
            if (text == "Auto" || text == "*") { result.Add(text); continue; }
            var isStar = text.EndsWith('*'); var number = isStar ? text[..^1] : text;
            if (!double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) || !double.IsFinite(parsed) || parsed <= 0 || parsed > (isStar ? 100 : 2000))
                throw Error(itemPath, "只允许 Auto、*、正数*（如 2*），或 0～2000 的正像素数（如 48）。");
            result.Add(text);
        }
        return result.ToArray();
    }

    private static double[] ReadMargin(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 4) throw Error(path, "应为四个数字：[左, 上, 右, 下]。");
        var index = 0;
        return element.EnumerateArray().Select(value => { var number = Number(value, $"{path}[{index++}]"); if (number is < 0 or > 64) throw Error(path, "边距应在 0～64 之间，不能用负边距覆盖其他控件。"); return number; }).ToArray();
    }

    private static double? OptionalDimension(JsonElement item, string name, string path, string? id)
    {
        if (!item.TryGetProperty(name, out var value)) return null;
        var result = Number(value, path + "." + name);
        var minimum = id is "previous" or "play" or "next" or "mode" or "favorite" or "more" or "volume" or "lyrics" ? 32 : 8;
        if (result < minimum || result > 512) throw Error(path + "." + name, $"大小应在 {minimum}～512 之间，不能隐藏必要控件。");
        return result;
    }

    private static void CheckObject(JsonElement value, string path, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Error(path, "应为 JSON 对象。");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal)) throw Error(path + "." + property.Name, "未知字段，请检查拼写；不允许自定义代码、绑定或隐藏控件。");
            if (!seen.Add(property.Name)) throw Error(path + "." + property.Name, "字段重复，请只保留一个值。");
        }
    }
    private static JsonElement Required(JsonElement value, string name, string path) => value.TryGetProperty(name, out var result) ? result : throw Error(path + "." + name, "缺少必要字段。");
    private static double Number(JsonElement value, string path) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result) && double.IsFinite(result) ? result : throw Error(path, "应为有限数字。");
    private static int Integer(JsonElement value, string path) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : throw Error(path, "应为整数。");
    private static string String(JsonElement value, string path) => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Error(path, "应为字符串。");
    private static int OptionalInteger(JsonElement value, string name, string path, int fallback) => value.TryGetProperty(name, out var result) ? Integer(result, path + "." + name) : fallback;
    private static string OptionalString(JsonElement value, string name, string path, string fallback) => value.TryGetProperty(name, out var result) ? String(result, path + "." + name) : fallback;
    private static UiLayoutException Error(string path, string message) => new($"{path}：{message}");

    public static string DefaultJson => """
    {
      // 编辑后在「设置 → 布局配置」中校验并应用；所有 12 个控件必须保留，点击封面打开歌词。
      "schemaVersion": 2,
      "workspace": {},
      "player": {
        "rows": ["24", "*"],
        "columns": ["*", "210", "*"],
        "items": [
          { "id": "progress", "row": 0, "column": 0, "columnSpan": 3, "height": 32, "verticalAlignment": "Center" },
          { "row": 1, "column": 0, "grid": {
            "rows": ["*", "*"], "columns": ["80", "*"], "items": [
              { "id": "artwork", "row": 0, "column": 0, "rowSpan": 2, "width": 60, "height": 60, "horizontalAlignment": "Left" },
              { "id": "title", "row": 0, "column": 1, "fontSize": 18, "verticalAlignment": "Bottom", "margin": [0, 0, 12, 3] },
              { "id": "artist", "row": 1, "column": 1, "fontSize": 16, "verticalAlignment": "Top", "margin": [0, 3, 12, 0] }
            ]
          } },
          { "row": 1, "column": 1, "grid": {
            "rows": ["*"], "columns": ["70", "70", "70"], "items": [
              { "id": "previous", "row": 0, "column": 0, "width": 50, "height": 50, "horizontalAlignment": "Center" },
              { "id": "play", "row": 0, "column": 1, "width": 50, "height": 50, "horizontalAlignment": "Center" },
              { "id": "next", "row": 0, "column": 2, "width": 50, "height": 50, "horizontalAlignment": "Center" }
            ]
          } },
          { "row": 1, "column": 2, "horizontalAlignment": "Right", "grid": {
            "rows": ["*"], "columns": ["112", "45", "45", "45", "45"], "items": [
              { "id": "time", "row": 0, "column": 0, "horizontalAlignment": "Center", "margin": [0, 0, 8, 0] },
              { "id": "mode", "row": 0, "column": 1, "width": 40, "height": 40, "horizontalAlignment": "Center" },
              { "id": "favorite", "row": 0, "column": 2, "width": 40, "height": 40, "horizontalAlignment": "Center" },
              { "id": "more", "row": 0, "column": 3, "width": 40, "height": 40, "horizontalAlignment": "Center" },
              { "id": "volume", "row": 0, "column": 4, "width": 40, "height": 40, "horizontalAlignment": "Center" }
            ]
          } }
        ]
      }
    }
    """;
}
