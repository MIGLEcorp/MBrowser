namespace MBrowser;

/// <summary>CEF presentation workarounds. They never disable hardware video decoding.</summary>
internal enum VideoPresentationMode { Default, Stable, Compatibility }

internal static class CefVideoPolicy
{
    // These feature names are verified against Chromium 154 ui/gl/gl_switches.cc.
    // Avoid black/blank frames when DWM promotes and demotes video overlays.
    internal const string StableDisabledFeatures =
        "DirectCompositionSoftwareOverlays,DirectCompositionLetterboxVideoOptimization";
    internal const string StableEnabledFeatures = "DCompTripleBufferVideoSwapChain";
    // Chromium GPU driver workaround, verified against gpu/config/gpu_workaround_list.txt.
    internal const string DisableOverlaysSwitch = "disable_direct_composition_video_overlays";

    internal static (string EnableFeatures, string DisableFeatures, bool DisableDirectComposition)
        Options(VideoPresentationMode mode, bool hardwareAcceleration)
    {
        if (!hardwareAcceleration || mode == VideoPresentationMode.Default)
            return ("", "", false);
        if (mode == VideoPresentationMode.Compatibility)
            return ("", "", true);
        return (StableEnabledFeatures, StableDisabledFeatures, false);
    }

    internal static VideoPresentationMode Validate(VideoPresentationMode mode) =>
        Enum.IsDefined(mode) ? mode : VideoPresentationMode.Compatibility;
}

internal readonly record struct VideoFrameSample(
    double TimestampMs, double CurrentTimeSeconds, double TotalFrames, double DroppedFrames,
    int Width, int Height, bool Paused, int ReadyState, string CodecSupport);

internal readonly record struct VideoFrameReport(double SampleSeconds, double PresentedFps,
    double DroppedFps, double DroppedPercent, bool Playing, int Width, int Height)
{
    internal static VideoFrameReport Calculate(VideoFrameSample before, VideoFrameSample after)
    {
        double dt = Math.Max(0.001, (after.TimestampMs - before.TimestampMs) / 1000);
        double total = Math.Max(0, after.TotalFrames - before.TotalFrames);
        double dropped = Math.Clamp(after.DroppedFrames - before.DroppedFrames, 0, total);
        return new(dt, (total - dropped) / dt, dropped / dt,
            total <= 0 ? 0 : 100 * dropped / total,
            !after.Paused && after.ReadyState >= 2 &&
            after.CurrentTimeSeconds > before.CurrentTimeSeconds,
            after.Width, after.Height);
    }
}
