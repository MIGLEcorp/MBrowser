using CefSharp;
using CefSharp.Handler;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MBrowser;

/// <summary>
/// Downloads managed by CEF. The ordinary path is the user's Downloads folder
/// with collision-free names, while Save As remains an explicit user choice.
/// </summary>
internal sealed class CefDownloadsHandler(MainForm owner, Guid tabId) : DownloadHandler
{
    protected override bool OnBeforeDownload(IWebBrowser webBrowser, IBrowser browser,
        DownloadItem item, IBeforeDownloadCallback callback)
    {
        owner.CefDownloadDiagnosticForTest("BEFORE_DOWNLOAD id=" + item.Id);
        string suggested = Path.GetFileName(item.SuggestedFileName);
        if (string.IsNullOrWhiteSpace(suggested) ||
            suggested.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            suggested = "download";
        string url = item.Url;
        int downloadId = item.Id;
        if (!owner.DispatchCefUi(async () =>
        {
            try
            {
                if (callback.IsDisposed) return;
                string? destination = null;
                if (owner.AskWhereToSaveDownloads)
                {
                    using var picker = new SaveFileDialog
                    {
                        Title = "Сохранить загрузку · MBrowser",
                        FileName = suggested,
                        InitialDirectory = owner.DownloadTargetDirectory,
                        OverwritePrompt = true,
                        AddExtension = false
                    };
                    if (picker.ShowDialog(owner) == DialogResult.OK)
                        destination = picker.FileName;
                }
                else destination = owner.ChooseDownloadDestination(suggested);

                if (destination is not null && !callback.IsDisposed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    owner.CefDownloadStarted(tabId, downloadId, suggested, destination, url);
                    callback.Continue(destination, false);
                }
            }
            catch (Exception error) { owner.CefDownloadError(error); }
            finally { callback.Dispose(); }
            await Task.CompletedTask;
        })) callback.Dispose();
        return true;
    }

    protected override void OnDownloadUpdated(IWebBrowser webBrowser, IBrowser browser,
        DownloadItem item, IDownloadItemCallback callback)
    {
        int id = item.Id;
        bool complete = item.IsComplete, cancelled = item.IsCancelled, interrupted = item.IsInterrupted;
        bool paused = item.IsPaused;
        long received = item.ReceivedBytes, total = item.TotalBytes;
        double bytesPerSecond = Convert.ToDouble(item.CurrentSpeed);
        if (complete || cancelled || interrupted)
            owner.CefDownloadDiagnosticForTest("UPDATED id=" + id +
                " complete=" + complete + " cancel=" + cancelled +
                " interrupted=" + interrupted + " received=" + received);
        owner.TrackCefDownloadCallback(tabId, id, callback, complete || cancelled || interrupted);
        owner.DispatchCefUi(() =>
        {
            owner.CefDownloadProgress(tabId, id, received, total, bytesPerSecond,
                paused, complete, cancelled, interrupted);
            return Task.CompletedTask;
        });
    }
}

internal sealed partial class MainForm
{
    private sealed record CefDownloadInfo(Guid TabId, int Id, string Name, string Destination, string Url,
        long Received, long Total, double Speed, bool Paused, string Status, DateTimeOffset CreatedUtc);

    private readonly Dictionary<(Guid TabId, int Id), CefDownloadInfo> _cefDownloadList = [];
    private readonly ConcurrentDictionary<(Guid TabId, int Id), IDownloadItemCallback>
        _cefDownloadCallbacks = new();
    private readonly HashSet<string> _downloadReservedPaths = new(StringComparer.OrdinalIgnoreCase);

    internal int CefDownloadCountForTest => _cefDownloadList.Count;
    internal bool CefDownloadCompletedForTest() => _cefDownloadList.Values.Any(x =>
        x.Status == "Готово" && x.Received > 0 &&
        File.Exists(x.Destination));

    internal void CefDownloadDiagnosticForTest(string message)
    {
        if (!_smoke) return;
        try { File.AppendAllText(Path.Combine(DataPath, "cef-download-events.log"), message + Environment.NewLine); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal void CefClickDownloadSmokeLinkForTest()
    {
        if (!_smoke || !_cefRuntimes.TryGetValue(ActiveTab.Id, out var runtime) ||
            !CefIsAlive(runtime) || !runtime.View.IsBrowserInitialized) return;
        try
        {
            var host = runtime.View.GetBrowser().GetHost();
            var click = new CefSharp.MouseEvent(48, 16, CefSharp.CefEventFlags.None);
            host.SendMouseClickEvent(click, CefSharp.MouseButtonType.Left, false, 1);
            host.SendMouseClickEvent(click, CefSharp.MouseButtonType.Left, true, 1);
            CefDownloadDiagnosticForTest("SENT_USER_CLICK");
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
        { CefDownloadDiagnosticForTest("CLICK_ERROR=" + error.GetType().Name); }
    }
    internal static string DownloadsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    internal string DownloadTargetDirectory => _smoke
        ? Path.Combine(DataPath, "Downloads") : DownloadsDirectory;
    internal bool AskWhereToSaveDownloads => _preferences.AskWhereToSaveDownloads;

    internal bool DispatchCefUi(Func<Task> action)
    {
        try
        {
            if (_closing || !IsHandleCreated || IsDisposed) return false;
            BeginInvoke((Action)(() => RunUi(action)));
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }

    internal string ChooseDownloadDestination(string suggested)
    {
        Directory.CreateDirectory(DownloadTargetDirectory);
        string target = DownloadUiPolicy.UniqueDownloadPath(
            DownloadTargetDirectory, suggested, _downloadReservedPaths);
        _downloadReservedPaths.Add(target);
        return target;
    }

    internal void TrackCefDownloadCallback(Guid tabId, int id,
        IDownloadItemCallback callback, bool terminal)
    {
        var key = (tabId, id);
        if (terminal) _cefDownloadCallbacks.TryRemove(key, out _);
        else if (!callback.IsDisposed) _cefDownloadCallbacks[key] = callback;
    }

    internal void CefDownloadStarted(Guid tabId, int id, string name, string destination, string url)
    {
        _cefDownloadList[(tabId, id)] = new(tabId, id, name, destination, url,
            0, 0, 0, false, "Загрузка", DateTimeOffset.UtcNow);
        _status.Text = "CEF: загружается " + name;
        UpdateDownloadButton();
        if (_downloadsPanel is not null) RefreshDownloadsPanel();
    }

    internal void CefDownloadProgress(Guid tabId, int id, long received, long total,
        double speed, bool paused, bool complete, bool cancelled, bool interrupted)
    {
        if (!_cefDownloadList.TryGetValue((tabId, id), out var info)) return;
        string state = complete ? "Готово" : cancelled ? "Отменено" :
            interrupted ? "Ошибка" : paused ? "Пауза" : "Загрузка";
        bool terminal = complete || cancelled || interrupted;
        _cefDownloadList[(tabId, id)] = info with
        {
            Received = Math.Max(0, received), Total = Math.Max(0, total),
            Speed = speed, Paused = paused, Status = state
        };
        if (terminal)
        {
            _downloadReservedPaths.Remove(info.Destination);
            _cefDownloadCallbacks.TryRemove((tabId, id), out _);
        }
        if (complete && !string.IsNullOrWhiteSpace(info.Destination))
        {
            _status.Text = "Сохранено: " + info.Name;
            if (info.Status != "Готово") SaveCefDownloadHistory();
        }
        UpdateDownloadButton();
        if (terminal && _downloadsPanel is not null) RefreshDownloadsPanel();
    }

    internal void CefDownloadError(Exception error)
    {
        Program.LogError(DataPath, error);
        if (!_closing) _status.Text = "Ошибка загрузки: " + error.Message;
    }

    private void DownloadAction(CefDownloadInfo info, string action)
    {
        if (!_cefDownloadCallbacks.TryGetValue((info.TabId, info.Id), out var callback) ||
            callback.IsDisposed) return;
        try
        {
            switch (action)
            {
                case "pause": callback.Pause(); break;
                case "resume": callback.Resume(); break;
                case "cancel": callback.Cancel(); break;
            }
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
        { Program.LogError(DataPath, error); }
    }

    private static void OpenDownloadedFile(CefDownloadInfo info)
    {
        if (!File.Exists(info.Destination)) return;
        Process.Start(new ProcessStartInfo(info.Destination) { UseShellExecute = true });
    }

    private static void ShowDownloadedFileInFolder(CefDownloadInfo info)
    {
        if (!File.Exists(info.Destination)) return;
        Process.Start(new ProcessStartInfo("explorer.exe",
            "/select,\"" + info.Destination.Replace("\"", "") + "\"") { UseShellExecute = true });
    }
}
