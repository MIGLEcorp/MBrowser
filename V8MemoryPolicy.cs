namespace MBrowser;

/// <summary>
/// V8's --optimize-for-size is an official memory-biased mode. It trades
/// execution throughput for smaller heaps and may be inappropriate for games.
/// No hard heap limits: real-world sites must not crash with OOM.
/// </summary>
internal enum V8MemoryMode { Standard, Compact, Automatic }

internal static class V8MemoryPolicy
{
    internal static V8MemoryMode Validate(V8MemoryMode mode) =>
        Enum.IsDefined(mode) ? mode : V8MemoryMode.Standard;

    internal static bool ShouldOptimize(V8MemoryMode mode, MemoryMode memoryMode) =>
        mode == V8MemoryMode.Compact ||
        mode == V8MemoryMode.Automatic && memoryMode == MemoryMode.Extreme;

    internal static string Flags(V8MemoryMode mode, MemoryMode memoryMode) =>
        ShouldOptimize(mode, memoryMode) ? "--optimize-for-size" : "";
}
