using System.Text.Json;
using CefSharp;

namespace MBrowser;

internal sealed partial class MainForm
{
    private const string VideoSnapshotScript = """
(() => {
  const videos = [...document.querySelectorAll('video')];
  const video = videos.sort((a, b) =>
      b.getBoundingClientRect().width * b.getBoundingClientRect().height -
      a.getBoundingClientRect().width * a.getBoundingClientRect().height)[0];
  if (!video) return '';
  const q = typeof video.getVideoPlaybackQuality === 'function'
      ? video.getVideoPlaybackQuality() : null;
  const can = (type) => video.canPlayType(type) || 'no';
  return JSON.stringify({
    TimestampMs: performance.now(),
    CurrentTimeSeconds: Number(video.currentTime) || 0,
    TotalFrames: q?.totalVideoFrames ?? video.webkitDecodedFrameCount ?? 0,
    DroppedFrames: q?.droppedVideoFrames ?? video.webkitDroppedFrameCount ?? 0,
    Width: video.videoWidth || 0,
    Height: video.videoHeight || 0,
    Paused: video.paused,
    ReadyState: video.readyState,
    CodecSupport: 'H264=' + can('video/mp4; codecs="avc1.42E01E"') +
      ', VP9=' + can('video/webm; codecs="vp9"') +
      ', AV1=' + can('video/mp4; codecs="av01.0.04M.08"')
  });
})()
""";

    internal async Task<string> CefMediaCapabilitiesForTestAsync()
    {
        if (!_cefRuntimes.TryGetValue(ActiveTab.Id, out var runtime) ||
            !CefIsAlive(runtime) || !runtime.View.CanExecuteJavascriptInMainFrame)
            return "no-main-frame";
        const string script = "(() => {const v=document.createElement('video');" +
            "const mse=globalThis.MediaSource;" +
            "const types=['video/mp4; codecs=\"avc1.42E01E\"','audio/mp4; codecs=\"mp4a.40.2\"'," +
            "'video/webm; codecs=\"vp9\"'];" +
            "const codecs=types.map(type=>({type,canPlay:v.canPlayType(type)||'no'," +
            "mse:!!mse?.isTypeSupported?.(type)}));" +
            "const resources=performance.getEntriesByType('resource');" +
            "return JSON.stringify({host:location.hostname,codecs,videoCount:document.querySelectorAll('video').length," +
            "iframeCount:document.querySelectorAll('iframe').length," +
            "hlsRequests:resources.filter(x=>/\\.m3u8(?:\\?|$)/i.test(x.name)).length," +
            "mediaErrors:[...document.querySelectorAll('video')].map(x=>x.error?.code||0)});})()";
        try
        {
            var result = await runtime.View.EvaluateScriptAsync(script);
            return result.Success && result.Result is string json ? json : "evaluation-failed";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        { Program.LogError(DataPath, ex); return "evaluation-error"; }
    }

    private void ShowVideoSettings()
    {
        ShowMenuPage("Видео · GPU", rows =>
        {
            rows("Режим вывода (после перезапуска MBrowser)", () => { });
            rows((_preferences.VideoPresentation == VideoPresentationMode.Compatibility ? "✓ " : "") +
                "Рекомендуется — GPU без DirectComposition", () =>
                SetVideoMode(VideoPresentationMode.Compatibility));
            rows((_preferences.VideoPresentation == VideoPresentationMode.Stable ? "✓ " : "") +
                "Эксперимент — тройной буфер и без оверлеев", () =>
                SetVideoMode(VideoPresentationMode.Stable));
            rows((_preferences.VideoPresentation == VideoPresentationMode.Default ? "✓ " : "") +
                "Стандартный Chromium — без исправлений", () =>
                SetVideoMode(VideoPresentationMode.Default));
            rows("GPU-ускорение: " + (_preferences.HardwareAcceleration ? "ВКЛ" : "ВЫКЛ"), ShowResourceSettings);
            rows("Диагностика воспроизведения (3 сек.)", () => RunUi(ShowVideoDiagnosticsAsync));
            rows("Замерять на вкладке с запущенным видео", () => { });
            rows("Проверить поддержку H.264 / AAC / VP9", () =>
                RunUi(ShowMediaCodecDiagnosticsAsync));
            rows("Если видео недоступно — открыть страницу в системном браузере",
                OpenCurrentPageInSystemBrowser);
            rows("Изменение режима требует перезапуска браузера", () => { });
        });
    }

    private async Task ShowMediaCodecDiagnosticsAsync()
    {
        string result = await CefMediaCapabilitiesForTestAsync();
        ShowMenuPage("Поддержка видеокодеков", rows =>
        {
            if (result.StartsWith("{", StringComparison.Ordinal))
            {
                using var json = JsonDocument.Parse(result);
                if (json.RootElement.TryGetProperty("codecs", out var codecs))
                    foreach (var codec in codecs.EnumerateArray())
                    {
                        string type = codec.GetProperty("type").GetString() ?? "unknown";
                        string status = codec.GetProperty("canPlay").GetString() ?? "no";
                        string mse = codec.GetProperty("mse").GetBoolean() ? "да" : "нет";
                        rows(type + ": " + status + " · MSE: " + mse, () => { });
                    }
            }
            else rows("Диагностика недоступна: " + result, () => { });
            rows("Открыть страницу в системном браузере", OpenCurrentPageInSystemBrowser);
        });
    }

    private void OpenCurrentPageInSystemBrowser()
    {
        if (!AddressParser.TryWebUrl(ActiveTab.CurrentUrl ?? "", out var url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or
            InvalidOperationException)
        { ShowError(error); }
    }

    private void SetVideoMode(VideoPresentationMode mode)
    {
        _preferences.VideoPresentation = mode;
        try { _preferences.Save(DataPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { ShowError(ex); return; }
        ShowVideoSettings();
    }

    private async Task<VideoFrameSample?> SnapshotVideoAsync(CefTabRuntime runtime)
    {
        if (!CefIsAlive(runtime) || !runtime.View.CanExecuteJavascriptInMainFrame) return null;
        try
        {
            var result = await runtime.View.EvaluateScriptAsync(VideoSnapshotScript);
            if (!result.Success || result.Result is not string json || json.Length > 2048 ||
                string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<VideoFrameSample>(json);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or JsonException)
        { Program.LogError(DataPath, ex); return null; }
    }

    internal async Task<string> CefVideoFrameReportForTestAsync()
    {
        if (!_cefRuntimes.TryGetValue(ActiveTab.Id, out var runtime) || !CefIsAlive(runtime))
            return "no-renderer";
        var first = await SnapshotVideoAsync(runtime);
        if (first is null) return "no-video";
        await Task.Delay(2500);
        var last = await SnapshotVideoAsync(runtime);
        if (last is null || !CefIsAlive(runtime)) return "no-video-after-sample";
        var report = VideoFrameReport.Calculate(first.Value, last.Value);
        return JsonSerializer.Serialize(new { report.SampleSeconds, report.PresentedFps,
            report.DroppedFps, report.DroppedPercent, report.Playing,
            report.Width, report.Height, last.Value.CodecSupport });
    }

    private async Task ShowVideoDiagnosticsAsync()
    {
        Guid tabId = ActiveTab.Id;
        if (!_cefRuntimes.TryGetValue(tabId, out var runtime) || !CefIsAlive(runtime))
            return;
        var first = await SnapshotVideoAsync(runtime);
        if (first is null)
        {
            ShowMenuPage("Диагностика видео", rows =>
            {
                rows("В этой вкладке нет доступного HTML5-видео.", () => { });
                rows("Видео может находиться внутри отдельного iframe.", () => { });
                rows("Открыть страницу в системном браузере", OpenCurrentPageInSystemBrowser);
                rows("Открыть настройки видеовывода", ShowVideoSettings);
            });
            return;
        }
        await Task.Delay(2500);
        if (!_cefRuntimes.TryGetValue(tabId, out var current) || current != runtime ||
            ActiveTab.Id != tabId || !CefIsAlive(runtime)) return;
        var last = await SnapshotVideoAsync(runtime);
        if (last is null) return;
        var report = VideoFrameReport.Calculate(first.Value, last.Value);
        ShowMenuPage("Диагностика видео", rows =>
        {
            rows("Видео: " + report.Width + "×" + report.Height +
                (report.Playing ? " · воспроизводится" : " · пауза/буферизация"), () => { });
            rows("Показанных кадров/сек: " + report.PresentedFps.ToString("F1"), () => { });
            rows("Потеряно: " + report.DroppedFps.ToString("F1") + " кадр./сек (" +
                report.DroppedPercent.ToString("F1") + "%)", () => { });
            rows("Поддержка кодеков: " + last.Value.CodecSupport, () => { });
            rows("Режим вывода: " + _preferences.VideoPresentation, ShowVideoSettings);
            rows("GPU: " + (_preferences.HardwareAcceleration ? "включён в настройках" : "выключен"),
                ShowResourceSettings);
            rows("Счётчики HTML5 не подтверждают GPU-декодер.", () => { });
            rows("Повторить замер", () => RunUi(ShowVideoDiagnosticsAsync));
            rows("Настройки видеовывода", ShowVideoSettings);
        });
    }
}
