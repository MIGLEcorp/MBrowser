namespace MBrowser;

/// <summary>
/// Normal navigation uses Chromium native scroll restoration. Coordinates saved
/// for a discarded tab must only be replayed for that exact document's first load.
/// </summary>
internal static class CefScrollPolicy
{
    internal static bool ShouldRestore(bool pending, string? requestedUrl, string? loadedUrl,
        double x, double y) =>
        pending && (x > 0 || y > 0) && !string.IsNullOrWhiteSpace(requestedUrl) &&
        string.Equals(requestedUrl, loadedUrl, StringComparison.OrdinalIgnoreCase);

    internal static string RestoreScript(double x, double y)
    {
        string sx = Math.Clamp(x, 0, 100_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string sy = Math.Clamp(y, 0, 100_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return "(() => { const x=" + sx + ",y=" + sy + ";" +
            // Do not set history.scrollRestoration=manual: that breaks Chromium's
            // native Back/Forward scroll behavior after the restored page.
            "requestAnimationFrame(() => scrollTo(x,y));" +
            // Dynamic pages may extend their document shortly after first paint.
            "setTimeout(() => { if (Math.abs(scrollY-y)>2 && document.scrollingElement?.scrollHeight>y)" +
            " scrollTo(x,y); },200);" +
            "})()";
    }
}
