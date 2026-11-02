using System.Drawing.Imaging;

namespace MBrowser;

/// <summary>Theme-scoped artwork for every built-in chrome glyph; PNG keeps alpha, GIF keeps frames.</summary>
internal static class ThemeIcons
{
    internal static readonly IReadOnlyDictionary<string, string> KnownKeys = new Dictionary<string, string>
    {
        ["Back"] = "Назад", ["Forward"] = "Вперёд", ["Reload"] = "Обновить",
        ["Stop"] = "Остановить загрузку", ["Home"] = "Главная", ["Bookmark"] = "Закладки",
        ["Go"] = "Перейти / поиск", ["Menu"] = "Меню",
        ["TabNew"] = "Новая вкладка", ["TabClose"] = "Закрыть вкладку",
        ["TabPrevious"] = "Предыдущие вкладки", ["TabNext"] = "Следующие вкладки",
        ["Minimize"] = "Свернуть окно", ["Maximize"] = "Развернуть окно",
        ["Restore"] = "Восстановить окно", ["Close"] = "Закрыть окно",
        ["BookmarkAdd"] = "Добавить закладку", ["BookmarkMore"] = "Другие закладки"
    };
    private sealed class Media(string path) : IDisposable
    {
        internal readonly MemoryStream Stream = new(File.ReadAllBytes(path));
        internal Image Image = null!;
        internal int Frames;
        internal Media Initialize()
        {
            Image = Image.FromStream(Stream);
            Frames = Image.RawFormat.Guid == ImageFormat.Gif.Guid ? Image.GetFrameCount(FrameDimension.Time) : 1;
            return this;
        }
        public void Dispose() { Image?.Dispose(); Stream.Dispose(); }
    }
    private static readonly Dictionary<string, Media?> Cache = [];
    private static readonly List<WeakReference<Control>> AnimatedOwners = [];
    private static readonly System.Windows.Forms.Timer Timer = CreateTimer();
    private static System.Windows.Forms.Timer CreateTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 85 };
        timer.Tick += (_, _) =>
        {
            for (int i = AnimatedOwners.Count - 1; i >= 0; i--)
            {
                if (!AnimatedOwners[i].TryGetTarget(out var control) || control.IsDisposed)
                    AnimatedOwners.RemoveAt(i);
                else if (control.Visible && control.FindForm()?.WindowState != FormWindowState.Minimized)
                    control.Invalidate();
            }
            if (AnimatedOwners.Count == 0) timer.Stop();
        };
        return timer;
    }
    internal static void ReleaseAll()
    {
        Timer.Stop();
        AnimatedOwners.Clear();
        foreach (var media in Cache.Values) media?.Dispose();
        Cache.Clear();
    }
    internal static bool TryDraw(Graphics graphics, Rectangle bounds, string key, Control owner)
    {
        if (!ThemeSettings.Document.Icons.TryGetValue(key, out string? filename)) return false;
        string path = ThemeMedia.Resolve(filename);
        if (string.IsNullOrEmpty(path)) return false;
        if (!Cache.TryGetValue(path, out var media))
        {
            try
            {
                media = File.Exists(path) && new FileInfo(path).Length <= 16 * 1024 * 1024
                    ? new Media(path).Initialize() : null;
                if (media is not null && (media.Image.Width > 8192 || media.Image.Height > 8192 ||
                    (long)media.Image.Width * media.Image.Height * media.Frames > 80_000_000))
                {
                    media.Dispose();
                    media = null;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
            { media = null; }
            Cache[path] = media;
        }
        if (media is null) return false;
        try
        {
            if (media.Frames > 1)
            {
                media.Image.SelectActiveFrame(FrameDimension.Time, (int)((Environment.TickCount64 / 85) % media.Frames));
                if (!AnimatedOwners.Any(w => w.TryGetTarget(out var c) && ReferenceEquals(c, owner)))
                    AnimatedOwners.Add(new WeakReference<Control>(owner));
                if (!Timer.Enabled) Timer.Start();
            }
            var gState = graphics.Save();
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(media.Image, bounds);
            graphics.Restore(gState);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        { return false; }
    }
}
