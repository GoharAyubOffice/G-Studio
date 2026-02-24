using GStudio.Cinematic.Engine;
using GStudio.Common.Configuration;
using GStudio.Common.Events;
using GStudio.Common.Geometry;

namespace GStudio.Render.Preview;

public sealed class DeterministicPreviewPlanner
{
    private readonly CinematicPlanner _cinematicPlanner;

    public DeterministicPreviewPlanner(CinematicPlanner? cinematicPlanner = null)
    {
        _cinematicPlanner = cinematicPlanner ?? new CinematicPlanner();
    }

    public PreviewRenderPlan Build(
        IReadOnlyList<PointerEvent> pointerEvents,
        SessionSettings settings,
        double? durationSeconds = null)
    {
        var cinematicPlan = _cinematicPlanner.Build(pointerEvents, settings, durationSeconds);
        var frames = new List<PreviewFrame>(cinematicPlan.CameraFrames.Count);

        var fallbackCursor = cinematicPlan.CursorFrames.Count > 0
            ? cinematicPlan.CursorFrames[^1]
            : new CursorSample(
                Time: 0.0d,
                Position: new ScreenPoint(settings.Video.Width / 2.0d, settings.Video.Height / 2.0d),
                Hidden: false);

        for (var frameIndex = 0; frameIndex < cinematicPlan.CameraFrames.Count; frameIndex++)
        {
            var camera = cinematicPlan.CameraFrames[frameIndex];
            var cursor = frameIndex < cinematicPlan.CursorFrames.Count
                ? cinematicPlan.CursorFrames[frameIndex]
                : fallbackCursor with { Time = camera.Time };

            frames.Add(new PreviewFrame(
                FrameIndex: frameIndex,
                Time: camera.Time,
                Camera: camera,
                Cursor: cursor));
        }

        return new PreviewRenderPlan(
            Frames: frames,
            ZoomSegments: cinematicPlan.ZoomSegments,
            DurationSeconds: cinematicPlan.DurationSeconds,
            Fps: cinematicPlan.Fps);
    }
}
