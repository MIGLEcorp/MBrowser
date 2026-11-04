using System.Text.Json;

namespace MBrowser;

internal sealed partial class MainForm
{
    // Only completed local files are saved, never cookies, tokens or source URLs.
    private sealed record DownloadHistoryEntry(
        string Name, string Destination, long Size, DateTimeOffset CompletedAtUtc);

    private string DownloadHistoryFile => Path.Combine(DataPath, "downloads.json");

    private void LoadCefDownloadHistory()
    {
        try
        {
            if (!File.Exists(DownloadHistoryFile)) return;
            var entries = JsonSerializer.Deserialize<List<DownloadHistoryEntry>>(
                File.ReadAllText(DownloadHistoryFile)) ?? [];
            int id = -1;
            foreach (var item in entries.Take(120))
            {
                if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 255 ||
                    string.IsNullOrWhiteSpace(item.Destination) ||
                    !Path.IsPathFullyQualified(item.Destination) ||
                    !File.Exists(item.Destination))
                    continue;
                _cefDownloadList[(Guid.Empty, id)] =
                    new CefDownloadInfo(Guid.Empty, id--,
                        item.Name, item.Destination, "",
                        Math.Max(0, item.Size), Math.Max(0, item.Size),
                        0, false, "Готово", item.CompletedAtUtc);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            JsonException or ArgumentException)
        { Program.LogError(DataPath, error); }
    }

    private void SaveCefDownloadHistory()
    {
        try
        {
            var records = _cefDownloadList.Values
                .Where(x => x.Status == "Готово" && File.Exists(x.Destination))
                .OrderByDescending(x => x.CreatedUtc)
                .Take(120)
                .Select(x => new DownloadHistoryEntry(
                    x.Name, x.Destination, x.Total > 0 ? x.Total : x.Received, x.CreatedUtc))
                .ToArray();
            string tempfile = DownloadHistoryFile + ".tmp";
            File.WriteAllText(tempfile, JsonSerializer.Serialize(records));
            File.Move(tempfile, DownloadHistoryFile, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            ArgumentException)
        { Program.LogError(DataPath, error); }
    }

    private void ClearCompletedDownloadHistory()
    {
        foreach (var entry in _cefDownloadList
            .Where(kv => kv.Value.Status is "Готово" or "Отменено" or "Ошибка")
            .Select(kv => kv.Key).ToArray())
            _cefDownloadList.Remove(entry);
        SaveCefDownloadHistory();
        RefreshDownloadsPanel();
    }
}
