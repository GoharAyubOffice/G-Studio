using GStudio.Common.Configuration;
using GStudio.Common.Events;
using GStudio.Common.Geometry;
using GStudio.Common.Timeline;

namespace GStudio.Cinematic.Engine;

public sealed class CinematicPlanner
{
    private readonly AutoZoomGenerator _autoZoomGenerator;
    private readonly CursorProcessor _cursorProcessor;
    private readonly SpringCameraSolver _springCameraSolver;

    public CinematicPlanner(
        AutoZoomGenerator? autoZoomGenerator = null,
        CursorProcessor? cursorProcessor = null,
        SpringCameraSolver? springCameraSolver = null)
    {
        _autoZoomGenerator = autoZoomGenerator ?? new AutoZoomGenerator();
        _cursorProcessor = cursorProcessor ?? new CursorProcessor();
        _springCameraSolver = springCameraSolver ?? new SpringCameraSolver();
    }

    public CinematicPlan Build(
        IReadOnlyList<PointerEvent> pointerEvents,
        SessionSettings settings,
        double? durationSeconds = null,
        IReadOnlyList<TimeRange>? smoothBypassRanges = null)
    {
        var sortedEvents = pointerEvents.OrderBy(static e => e.T).ToArray();
        var duration = ResolveDuration(sortedEvents, durationSeconds);

        var smoothedEvents = _cursorProcessor.Smooth(sortedEvents, settings.Cursor, smoothBypassRanges);
        var zoomSegments = _autoZoomGenerator.Generate(
            sortedEvents,
            settings.Video.Width,
            settings.Video.Height,
            settings.Camera);

        var viewport = new RectD(0.0d, 0.0d, settings.Video.Width, settings.Video.Height);
        var cameraFrames = _springCameraSolver.Solve(
            duration,
            settings.Video.Fps,
            viewport,
            zoomSegments,
            settings.Camera.Preset);

        var cursorFrames = _cursorProcessor.BuildSamples(smoothedEvents, duration, settings.Video.Fps, settings.Cursor);

        return new CinematicPlan(
            ZoomSegments: zoomSegments,
            CameraFrames: cameraFrames,
            CursorFrames: cursorFrames,
            DurationSeconds: duration,
            Fps: settings.Video.Fps);
    }

    private static double ResolveDuration(IReadOnlyList<PointerEvent> pointerEvents, double? explicitDuration)
    {
        if (explicitDuration is { } durationFromInput && durationFromInput > 0.0d)
        {
            return durationFromInput;
        }

        if (pointerEvents.Count == 0)
        {
            return 0.1d;
        }

        return Math.Max(0.1d, pointerEvents[^1].T + 0.25d);
    }
}
