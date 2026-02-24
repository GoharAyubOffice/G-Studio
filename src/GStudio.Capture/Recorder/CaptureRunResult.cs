namespace GStudio.Capture.Recorder;

public sealed record CaptureRunResult(
    int FrameCount,
    double DurationSeconds)
{
    public static CaptureRunResult Empty { get; } = new(0, 0.0d);
}
