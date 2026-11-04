using System.Globalization;

namespace MBrowser;

/// <summary>Formatting and collision-safe download destinations for the native CEF manager.</summary>
internal static class DownloadUiPolicy
{
    internal static string FormatSize(long bytes)
    {
        if (bytes < 0) return "—";
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " Б";
        double n = bytes;
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        int i = 0;
        while (n >= 1024 && i < units.Length - 1) { n /= 1024; i++; }
        return n.ToString(n < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " " + units[i];
    }

    internal static string FormatSpeed(double bytesPerSecond) =>
        bytesPerSecond > 0 && double.IsFinite(bytesPerSecond)
            ? FormatSize((long)Math.Min(long.MaxValue, bytesPerSecond)) + "/с"
            : "—";

    internal static string FormatTimeRemaining(long received, long total, double speed)
    {
        if (total <= 0 || received >= total || !double.IsFinite(speed) || speed <= 0)
            return "—";
        double seconds = (total - received) / speed;
        if (seconds > 7 * 24 * 3600) return "больше недели";
        int value = (int)Math.Ceiling(seconds);
        if (value < 60) return value + " сек";
        if (value < 3600) return (value / 60) + " мин " + (value % 60) + " сек";
        return (value / 3600) + " ч " + ((value % 3600) / 60) + " мин";
    }

    internal static int Percentage(long received, long total) =>
        total <= 0 ? 0 : (int)Math.Clamp(received * 100d / total, 0, 100);

    internal static string UniqueDownloadPath(string directory, string suggestedName,
        IEnumerable<string> reserved)
    {
        string name = Path.GetFileName(suggestedName);
        if (string.IsNullOrWhiteSpace(name) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            name = "download";
        name = name.Trim().TrimEnd('.');
        if (name.Length == 0) name = "download";
        if (name.Length > 190) name = name[..190];
        string baseName = Path.GetFileNameWithoutExtension(name);
        string ext = Path.GetExtension(name);
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT1" }
            .Contains(baseName, StringComparer.OrdinalIgnoreCase))
            baseName = "download-" + baseName;
        var used = reserved.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < 10000; index++)
        {
            string candidate = Path.Combine(directory, baseName +
                (index == 0 ? "" : " (" + index + ")") + ext);
            if (!File.Exists(candidate) && !Directory.Exists(candidate) &&
                !used.Contains(candidate))
                return candidate;
        }
        return Path.Combine(directory, Guid.NewGuid().ToString("N") + ext);
    }
}
