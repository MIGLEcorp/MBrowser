using System.Text.Json;
using System.Text.Json.Serialization;

namespace MBrowser;

// Anime is retained solely for loading legacy 4.x documents.
internal enum BrowserTheme { Anime, Dark, Light }
internal sealed record ThemeColors(Color Background, Color Surface, Color Raised, Color Text, Color Muted, Color Accent, Color Sky);
internal enum ThemeArea { Home, Tabs, Toolbar, Bookmarks, Menu, Chrome }
internal sealed record ThemeLayer(string File = "", string Fit = "cover", int Tint = 150,
    float Zoom = 1, float PanX = 0, float PanY = 0);
internal sealed record ThemeDocument
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "Моя тема";
    public BrowserTheme Base { get; init; } = BrowserTheme.Dark;
    public Dictionary<string, string> Colors { get; init; } = [];
    public Dictionary<ThemeArea, ThemeLayer> Layers { get; init; } = [];
    public Dictionary<string, string> Icons { get; init; } = [];
}

internal static class ThemeSettings
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
        { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private static readonly IReadOnlyDictionary<BrowserTheme, ThemeColors> Themes = new Dictionary<BrowserTheme, ThemeColors>
    {
        [BrowserTheme.Anime] = new(Color.FromArgb(21,19,35), Color.FromArgb(32,28,49), Color.FromArgb(48,41,68),
            Color.FromArgb(249,243,252), Color.FromArgb(180,168,199), Color.FromArgb(242,176,210), Color.FromArgb(34,27,56)),
        // Every non-interactive OLED surface is exactly #000000; no grey panels or address bar.
        [BrowserTheme.Dark] = new(Color.Black, Color.Black, Color.Black,
            Color.FromArgb(245,245,245), Color.FromArgb(162,162,162), Color.FromArgb(183,136,255), Color.Black),
        [BrowserTheme.Light] = new(Color.FromArgb(250,250,250), Color.White, Color.FromArgb(231,231,235),
            Color.FromArgb(28,28,32), Color.FromArgb(105,105,112), Color.FromArgb(110,60,190), Color.White)
    };
    internal static ThemeDocument Document { get; private set; } = Preset(BrowserTheme.Dark);
    internal static BrowserTheme Current => Document.Base;
    internal static string DataPath { get; private set; } = "";
    private static ThemeColors? _previousColors;
    internal static bool IsBuiltin(string id) => id is "builtin-dark" or "builtin-light";
    internal static ThemeDocument Preset(BrowserTheme theme)
    {
        if (theme == BrowserTheme.Anime) theme = BrowserTheme.Dark;
        return new ThemeDocument
        {
            Base = theme,
            Id = theme == BrowserTheme.Light ? "builtin-light" : "builtin-dark",
            Name = theme == BrowserTheme.Light ? "Светлая" : "OLED / AMOLED",
            Layers = new[] { ThemeArea.Home, ThemeArea.Chrome, ThemeArea.Menu }
                .ToDictionary(area => area, area => new ThemeLayer("", "cover", area == ThemeArea.Home ? 0 : 130))
        };
    }
    internal static ThemeColors Colors
    {
        get
        {
            var c = Themes[Current];
            Color Read(string key, Color fallback) => Document.Colors.TryGetValue(key, out string? value) &&
                value.Length == 7 && value[0] == '#' &&
                int.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out int rgb)
                ? Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255) : fallback;
            return new(Read("Background", c.Background), Read("Surface", c.Surface), Read("Raised", c.Raised),
                Read("Text", c.Text), Read("Muted", c.Muted), Read("Accent", c.Accent), c.Sky);
        }
    }
    internal static ThemeLayer Layer(ThemeArea area) => Document.Layers.GetValueOrDefault(
        area is ThemeArea.Tabs or ThemeArea.Toolbar or ThemeArea.Bookmarks ? ThemeArea.Chrome : area) ?? new();

    internal static void Preview(ThemeDocument document)
    {
        _previousColors = Colors;
        var next = Normalize(document);
        if (new[] { ThemeArea.Home, ThemeArea.Chrome, ThemeArea.Menu }.Any(area =>
            Layer(area).File != (next.Layers.GetValueOrDefault(area)?.File ?? ""))) ThemeMedia.ReleaseAll();
        if (!Document.Icons.OrderBy(x => x.Key).SequenceEqual(next.Icons.OrderBy(x => x.Key)))
            ThemeIcons.ReleaseAll();
        Document = next;
    }
    internal static ThemeDocument Normalize(ThemeDocument doc)
    {
        var layers = doc.Layers ?? [];
        var chrome = layers.GetValueOrDefault(ThemeArea.Chrome) ?? layers.GetValueOrDefault(ThemeArea.Toolbar) ??
            layers.GetValueOrDefault(ThemeArea.Tabs) ?? new();
        ThemeLayer Clean(ThemeLayer? value)
        {
            var layer = value ?? new();
            string file = Path.GetFileName(layer.File ?? "");
            file = file switch { "kawaii-home.jpg" => "sakura-home.png", "kawaii-menu.jpg" => "sakura-menu.png",
                "kawaii-chrome.jpg" => "sakura-header.png", _ => file };
            return layer with { File = file, Tint = Math.Clamp(layer.Tint, 0, 255),
                Fit = layer.Fit is "contain" or "tile" ? layer.Fit : "cover",
                Zoom = float.IsFinite(layer.Zoom) ? Math.Clamp(layer.Zoom, 1, 4) : 1,
                PanX = float.IsFinite(layer.PanX) ? Math.Clamp(layer.PanX, -1, 1) : 0,
                PanY = float.IsFinite(layer.PanY) ? Math.Clamp(layer.PanY, -1, 1) : 0 };
        }
        string name = doc.Name ?? "Моя тема";
        if (string.IsNullOrWhiteSpace(name)) name = "Моя тема";
        string id = doc.Id ?? "";
        if (id.Length > 72 || id.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-')) id = "";
        var icons = (doc.Icons ?? []).Where(pair => ThemeIcons.KnownKeys.ContainsKey(pair.Key) &&
            !string.IsNullOrEmpty(pair.Value) && Path.GetFileName(pair.Value) == pair.Value &&
            (pair.Value.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
             pair.Value.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
             pair.Value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)))
             .Take(ThemeIcons.KnownKeys.Count).ToDictionary(x => x.Key, x => x.Value);
        return doc with {
            Id = id,
            Name = name[..Math.Min(name.Length, 80)],
            Base = Enum.IsDefined(doc.Base) ? doc.Base : BrowserTheme.Dark,
            Colors = doc.Colors?.Where(x => x.Value is { Length: 7 } && x.Value[0] == '#' &&
                int.TryParse(x.Value.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _))
                .Take(6).ToDictionary() ?? [],
            Layers = new() { [ThemeArea.Home] = Clean(layers.GetValueOrDefault(ThemeArea.Home)),
                [ThemeArea.Chrome] = Clean(chrome), [ThemeArea.Menu] = Clean(layers.GetValueOrDefault(ThemeArea.Menu)) },
            Icons = icons
        };
    }
    private static string LibraryPath(string dataPath) => Path.Combine(dataPath, "Themes");
    internal static IReadOnlyList<ThemeDocument> Library(string dataPath)
    {
        var result = new List<ThemeDocument> { Preset(BrowserTheme.Dark), Preset(BrowserTheme.Light) };
        string folder = LibraryPath(dataPath);
        if (!Directory.Exists(folder)) return result;
        foreach (string file in Directory.EnumerateFiles(folder, "*.json").Take(256))
        {
            try
            {
                if (new FileInfo(file).Length > 65536) continue;
                var doc = JsonSerializer.Deserialize<ThemeDocument>(File.ReadAllText(file), JsonOptions);
                if (doc is null) continue;
                doc = Normalize(doc);
                if (!IsBuiltin(doc.Id) && doc.Id.Length > 0) result.Add(doc);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return result;
    }
    internal static void Delete(string id, string dataPath)
    {
        if (IsBuiltin(id) || id.Length == 0 || id.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-')) return;
        string path = Path.Combine(LibraryPath(dataPath), id + ".json");
        if (File.Exists(path)) File.Delete(path);
        if (Document.Id == id) Save(Preset(BrowserTheme.Dark), dataPath);
    }
    internal static void Load(string dataPath)
    {
        DataPath = dataPath;
        ThemeMedia.ReleaseAll();
        ThemeIcons.ReleaseAll();
        Document = Preset(BrowserTheme.Dark);
        string path = Path.Combine(dataPath, "theme.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return;
            string json = File.ReadAllText(path);
            using var root = JsonDocument.Parse(json);
            if (root.RootElement.ValueKind == JsonValueKind.String)
            {
                if (Enum.TryParse<BrowserTheme>(root.RootElement.GetString(), true, out var theme) && Enum.IsDefined(theme))
                    Document = Preset(theme);
            }
            else if (JsonSerializer.Deserialize<ThemeDocument>(json, JsonOptions) is { } doc)
            {
                Document = Normalize(doc);
                if (Document.Name.Equals("Pink kawaii", StringComparison.OrdinalIgnoreCase))
                    Document = Preset(BrowserTheme.Dark);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }
    internal static void Select(BrowserTheme theme, string dataPath) => Save(Preset(theme), dataPath);
    internal static void Save(ThemeDocument document, string dataPath)
    {
        Directory.CreateDirectory(dataPath);
        var normalized = Normalize(document);
        if (IsBuiltin(normalized.Id) &&
            JsonSerializer.Serialize(normalized, JsonOptions) != JsonSerializer.Serialize(Preset(normalized.Base), JsonOptions))
            normalized = normalized with { Id = Guid.NewGuid().ToString("N") };
        if (normalized.Id.Length == 0) normalized = normalized with { Id = Guid.NewGuid().ToString("N") };
        string serialized = JsonSerializer.Serialize(normalized, JsonOptions);
        string path = Path.Combine(dataPath, "theme.json");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, serialized);
        File.Move(temporary, path, true);
        if (!IsBuiltin(normalized.Id))
        {
            string folder = LibraryPath(dataPath);
            Directory.CreateDirectory(folder);
            string entry = Path.Combine(folder, normalized.Id + ".json");
            File.WriteAllText(entry + ".tmp", serialized);
            File.Move(entry + ".tmp", entry, true);
        }
        DataPath = dataPath;
        Preview(normalized);
    }
    internal static Color Translate(Color color)
    {
        if (color.IsEmpty || color.A == 0) return color;
        foreach (var previous in (_previousColors is { } prior ? new[] { prior }.Concat(Themes.Values) : Themes.Values))
        {
            if (color.ToArgb() == previous.Background.ToArgb()) return Colors.Background;
            if (color.ToArgb() == previous.Surface.ToArgb()) return Colors.Surface;
            if (color.ToArgb() == previous.Raised.ToArgb()) return Colors.Raised;
            if (color.ToArgb() == previous.Text.ToArgb()) return Colors.Text;
            if (color.ToArgb() == previous.Muted.ToArgb()) return Colors.Muted;
            if (color.ToArgb() == previous.Accent.ToArgb()) return Colors.Accent;
        }
        return color;
    }
}
