namespace MBrowser;

/// <summary>
/// CefSharp may deliver FrameLoadEnd just as navigation invalidates its frame.
/// CefFrameWrapper throws a plain System.Exception for an invalid frame, so
/// catching ObjectDisposedException alone is not sufficient. No exception from
/// event-driven page-script injection should escape to CEF's callback thread.
/// </summary>
internal static class CefFrameLoadGuard
{
    internal static bool TryRun(Func<bool> isCurrentFrame, Action inject,
        Action<Exception> reportUnexpected)
    {
        try
        {
            if (!isCurrentFrame()) return false;
            inject();
            return true;
        }
        catch (Exception error) when (IsStaleFrame(error))
        {
            // An ordinary navigation race, not an application failure.
            return false;
        }
        catch (Exception error)
        {
            // Unexpected script-injection failure must not terminate MBrowser.
            try { reportUnexpected(error); }
            catch (Exception) { /* Never throw from a native CEF callback. */ }
            return false;
        }
    }

    internal static bool IsStaleFrame(Exception error) =>
        error is ObjectDisposedException ||
        error.Message.Contains("underlying frame is no longer valid",
            StringComparison.OrdinalIgnoreCase);
}
