using GStudio.Cinematic.Engine;

namespace GStudio.Render.Preview;

public sealed record PreviewFrame(
    int FrameIndex,
    double Time,
    CameraTransform Camera,
    CursorSample Cursor);

public sealed record PreviewRenderPlan(
    IReadOnlyList<PreviewFrame> Frames,
    IReadOnlyList<ZoomSegment> ZoomSegments,
    double DurationSeconds,
    int Fps);
