using GStudio.Cinematic.Engine;
using GStudio.Common.Configuration;
using GStudio.Common.Events;
using GStudio.Common.Geometry;

namespace GStudio.Cinematic.Tests;

public sealed class CinematicEngineTests
{
    [Fact]
    public void AutoZoomGenerator_EmitsIndependentSegmentsForRapidClicks()
    {
        var generator = new AutoZoomGenerator();
        var settings = SessionSettings.CreateDefault().Camera with
        {
            PreRollSeconds = 0.2d,
            HoldSeconds = 0.5d,
            FocusAreaRatio = 0.4d
        };

        var events = new List<PointerEvent>
        {
            PointerEvent.Down(1.0d, 200.0d, 180.0d),
            PointerEvent.Down(1.35d, 240.0d, 200.0d)
        };

        var segments = generator.Generate(events, 1920, 1080, settings);

        Assert.Equal(2, segments.Count);
        Assert.Equal(0.8d, segments[0].Start, 3);
        Assert.Equal(1.5d, segments[0].End, 3);
        Assert.Equal(1.15d, segments[1].Start, 3);
        Assert.Equal(1.85d, segments[1].End, 3);
        Assert.Equal(1, segments[0].TriggerCount);
        Assert.Equal(1, segments[1].TriggerCount);
    }

    [Fact]
    public void SpringCameraSolver_ZoomSegmentPushesScaleAboveDefault()
    {
        var solver = new SpringCameraSolver();
        var segments = new[]
        {
            new ZoomSegment(
                Start: 1.0d,
                End: 2.0d,
                Center: new ScreenPoint(1100.0d, 620.0d),
                Scale: 2.4d,
                TriggerCount: 1)
        };

        var frames = solver.Solve(
            durationSeconds: 3.0d,
            fps: 60,
            viewport: new RectD(0.0d, 0.0d, 1920.0d, 1080.0d),
            zoomSegments: segments,
            preset: MotionPreset.Quick);

        Assert.True(frames.Count > 120);
        Assert.True(frames.Max(static frame => frame.Scale) > 1.5d);
        Assert.Contains(frames, static frame => frame.Center.X > 980.0d);
    }

    [Fact]
    public void SpringCameraSolver_PrefersMostRecentOverlappingZoomSegment()
    {
        var solver = new SpringCameraSolver();
        var segments = new[]
        {
            new ZoomSegment(
                Start: 1.0d,
                End: 2.0d,
                Center: new ScreenPoint(600.0d, 480.0d),
                Scale: 2.2d,
                TriggerCount: 1),
            new ZoomSegment(
                Start: 1.25d,
                End: 2.2d,
                Center: new ScreenPoint(1450.0d, 720.0d),
                Scale: 2.4d,
                TriggerCount: 1)
        };

        var frames = solver.Solve(
            durationSeconds: 3.0d,
            fps: 60,
            viewport: new RectD(0.0d, 0.0d, 1920.0d, 1080.0d),
            zoomSegments: segments,
            preset: MotionPreset.Quick);

        var postOverlapFrames = frames.Where(frame => frame.Time >= 1.45d && frame.Time <= 2.05d).ToArray();
        Assert.NotEmpty(postOverlapFrames);
        Assert.True(postOverlapFrames.Average(static frame => frame.Center.X) > 980.0d);
    }

    [Fact]
    public void CursorProcessor_RemoveShakesReducesMicroJitterTravel()
    {
        var processor = new CursorProcessor();
        var settings = SessionSettings.CreateDefault().Cursor with
        {
            SmoothEnabled = true,
            RemoveShakes = true,
            ShakeThresholdPixels = 2.5d
        };

        var events = new List<PointerEvent>
        {
            PointerEvent.Move(0.00d, 100.0d, 100.0d),
            PointerEvent.Move(0.02d, 101.0d, 99.4d),
            PointerEvent.Move(0.04d, 99.6d, 100.7d),
            PointerEvent.Move(0.06d, 100.8d, 99.8d),
            PointerEvent.Move(0.08d, 101.2d, 100.1d),
            PointerEvent.Move(0.10d, 135.0d, 120.0d)
        };

        var smoothed = processor.Smooth(events, settings);
        var rawTravel = TotalTravel(events);
        var smoothTravel = TotalTravel(smoothed);

        Assert.Equal(events.Count, smoothed.Count);
        Assert.True(smoothTravel < rawTravel);
        Assert.InRange(smoothed[^1].X, 120.0d, 135.0d);
    }

    private static double TotalTravel(IReadOnlyList<PointerEvent> events)
    {
        if (events.Count <= 1)
        {
            return 0.0d;
        }

        var total = 0.0d;
        for (var index = 1; index < events.Count; index++)
        {
            var a = events[index - 1];
            var b = events[index];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            total += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return total;
    }
}
