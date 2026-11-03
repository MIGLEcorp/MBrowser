using CefSharp;
using CefSharp.DevTools.Memory;

namespace MBrowser;

/// <summary>
/// Chromium's real moderate memory pressure notification. Never overwrite page
/// scripts, remove DOM nodes or call exposed V8 GC. Can be requested manually,
/// and while window has been inactive for a long time if user opted in.
/// </summary>
internal static class MemoryMaintenancePolicy
{
    internal static bool ShouldRun(bool enabled, bool browserForeground, bool loading,
        TimeSpan inactiveFor, TimeSpan sinceLastMaintenance) =>
        enabled && !browserForeground && !loading &&
        inactiveFor >= TimeSpan.FromMinutes(3) &&
        sinceLastMaintenance >= TimeSpan.FromMinutes(8);
}

internal sealed partial class MainForm
{
    private DateTimeOffset _lastMemoryMaintenance = DateTimeOffset.MinValue;
    private bool _memoryMaintenanceRunning;

    internal Task<bool> TryMemoryPressureForSmokeAsync() => TryReduceActivePageMemoryAsync();

    private async Task<bool> TryReduceActivePageMemoryAsync()
    {
        if (_memoryMaintenanceRunning || _closing ||
            !_cefRuntimes.TryGetValue(ActiveTab.Id, out var runtime) ||
            !CefIsAlive(runtime) || runtime.Loading ||
            !runtime.View.IsBrowserInitialized ||
            !runtime.View.CanExecuteJavascriptInMainFrame ||
            CefHasProtectedActivity(runtime)) return false;

        _memoryMaintenanceRunning = true;
        try
        {
            // Avoid frames with media, ongoing calls or edited form state.
            var safe = await runtime.View.EvaluateScriptAsync(TabHibernationManager.SafeToHibernateScript);
            if (!CefIsAlive(runtime) || safe.Success != true || safe.Result is not true) return false;
            using var devTools = runtime.View.GetDevToolsClient();
            var reply = await devTools.Memory.SimulatePressureNotificationAsync(PressureLevel.Moderate);
            _lastMemoryMaintenance = DateTimeOffset.UtcNow;
            return reply.Success;
        }
        catch (Exception ex)
        {
            Program.LogError(DataPath, ex);
            return false;
        }
        finally { _memoryMaintenanceRunning = false; }
    }

    private async Task MaintainIdleMemoryAsync(DateTimeOffset now)
    {
        if (!MemoryMaintenancePolicy.ShouldRun(_preferences.CollectJavaScriptGarbageWhenIdle,
            IsBrowserForeground, IsPageLoading, now - _inactiveSinceUtc,
            now - _lastMemoryMaintenance))
            return;
        // Also protect against downloads, ongoing media and edited forms.
        await TryReduceActivePageMemoryAsync();
        // Avoid a tight retry loop on pages where maintenance is not safe.
        _lastMemoryMaintenance = DateTimeOffset.UtcNow;
    }

    private async Task ShowManualMemoryReclaimAsync()
    {
        var before = ReadMemory();
        bool success = await TryReduceActivePageMemoryAsync();
        if (success) await Task.Delay(1500);
        var after = ReadMemory();
        ShowMenuPage("Очистка памяти CEF", rows =>
        {
            rows(success ? "Запрос Moderate отправлен Chromium" :
                "Не выполнено: вкладка занята или недоступна", ShowResourceDiagnostics);
            rows("Private Bytes до: " + Mb(before.PrivateBytes), ShowResourceDiagnostics);
            rows("Private Bytes после: " + Mb(after.PrivateBytes), ShowResourceDiagnostics);
            rows("Разница: " + Mb(before.PrivateBytes - after.PrivateBytes), ShowResourceDiagnostics);
            rows("Может временно увеличить CPU; безопасен не для всех сайтов", ShowResourceDiagnostics);
            rows("Назад к диагностике", ShowResourceDiagnostics);
        });
    }
}
