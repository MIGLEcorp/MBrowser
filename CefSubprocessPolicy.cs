namespace MBrowser;

/// <summary>
/// CefSharp's small subprocess launcher skips loading MBrowser's full .NET
/// WinForms host in every Chromium renderer, utility and GPU child. The tradeoff
/// is that Task Manager shows CefSharp.BrowserSubprocess.exe in separate groups.
/// </summary>
internal enum CefSubprocessMode { Automatic, Grouped, Compact }

internal static class CefSubprocessPolicy
{
    internal static CefSubprocessMode Validate(CefSubprocessMode mode) =>
        Enum.IsDefined(mode) ? mode : CefSubprocessMode.Automatic;

    internal static bool UseCompact(CefSubprocessMode mode, MemoryMode memoryMode) => mode switch
    {
        CefSubprocessMode.Compact => true,
        CefSubprocessMode.Grouped => false,
        _ => memoryMode is MemoryMode.LowMemory or MemoryMode.Extreme
    };

    internal static string Executable(CefSubprocessMode mode, MemoryMode memoryMode) =>
        UseCompact(mode, memoryMode) ? "CefSharp.BrowserSubprocess.exe" : "MBrowser.exe";
}
