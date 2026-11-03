namespace MBrowser;

/// <summary>
/// CEF is expensive even with no rendered page. By default the native window
/// appears first; the engine initializes on the first real navigation (which
/// also happens automatically for an initially restored web page). Existing
/// tabs remain in the session, without restarting the main process.
/// </summary>
internal static class CefStartupPolicy
{
    internal static bool InitializeBeforeWindow(bool lazyStartup, bool forceDefer) =>
        !lazyStartup && !forceDefer;

    internal static bool NeedsEngineAtLaunch(AppSession session, IReadOnlyList<string> launchTargets, bool smoke)
    {
        if (smoke || launchTargets.Count > 0) return true;
        if (session.Tabs.Count == 0) return false;
        int selected = Math.Clamp(session.SelectedIndex, 0, session.Tabs.Count - 1);
        string? url = session.Tabs[selected].CurrentUrl;
        return !string.IsNullOrWhiteSpace(url) &&
            !url.Equals("about:blank", StringComparison.OrdinalIgnoreCase);
    }
}
