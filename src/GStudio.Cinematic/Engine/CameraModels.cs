using GStudio.Common.Geometry;

namespace GStudio.Cinematic.Engine;

public sealed record ZoomSegment(
    double Start,
    double End,
    ScreenPoint Center,
    double Scale,
    int TriggerCount)
{
    public double Duration => Math.Max(0.0d, End - Start);
}

public sealed record CameraTransform(
    double Time,
    ScreenPoint Center,
    double Scale,
    double Rotation = 0.0d);

public sealed record CursorSample(
    double Time,
    ScreenPoint Position,
    bool Hidden);

public sealed record SpringSettings(
    double Tension,
    double Friction,
    double Mass);

public sealed record CinematicPlan(
    IReadOnlyList<ZoomSegment> ZoomSegments,
    IReadOnlyList<CameraTransform> CameraFrames,
    IReadOnlyList<CursorSample> CursorFrames,
    double DurationSeconds,
    int Fps);
