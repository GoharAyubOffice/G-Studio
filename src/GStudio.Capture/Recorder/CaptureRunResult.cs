namespace GStudio.Capture.Recorder;

public sealed record CaptureRunResult(
    int FrameCount,
    double DurationSeconds,
    int TargetFps,
    double EffectiveFps,
    string BackendName,
    string BackendDetails,
    int CaptureMissCount,
    int ReusedFrameCount,
    int FrameTimelineCount,
    double TimelineDurationSeconds,
    double TimelineEffectiveFps,
    double AverageFrameIntervalMs,
    double FrameIntervalJitterMs,
    double MaxFrameIntervalMs,
    double DurationDriftMs)
{
    public static CaptureRunResult Empty { get; } = new(
        0,
        0.0d,
        0,
        0.0d,
        "unknown",
        "unknown",
        0,
        0,
        0,
        0.0d,
        0.0d,
        0.0d,
        0.0d,
        0.0d,
        0.0d);
}
