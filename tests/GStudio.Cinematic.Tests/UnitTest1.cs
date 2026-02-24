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
    public void AutoZoomGenerator_ExtendsHoldWhilePointerStaysNearClickedRegion()
    {
        var generator = new AutoZoomGenerator();
        var settings = SessionSettings.CreateDefault().Camera;

        var events = new List<PointerEvent>
        {
            PointerEvent.Down(5.0d, 900.0d, 300.0d),
            PointerEvent.Move(5.2d, 930.0d, 320.0d),
            PointerEvent.Move(5.9d, 940.0d, 460.0d),
            PointerEvent.Move(6.4d, 960.0d, 500.0d),
            PointerEvent.Move(6.8d, 980.0d, 520.0d),
            PointerEvent.Move(7.05d, 1300.0d, 520.0d)
        };

        var segments = generator.Generate(events, 1920, 1080, settings);

        Assert.Single(segments);
        Assert.InRange(segments[0].End, 6.9d, 7.05d);
    }

    [Fact]
    public void AutoZoomGenerator_DoesNotExtendWhenPointerLeavesBeforePostHoldActivity()
    {
        var generator = new AutoZoomGenerator();
        var settings = SessionSettings.CreateDefault().Camera;

        var events = new List<PointerEvent>
        {
            PointerEvent.Down(1.0d, 300.0d, 300.0d),
            PointerEvent.Move(1.25d, 640.0d, 320.0d),
            PointerEvent.Move(1.65d, 980.0d, 360.0d)
        };

        var segments = generator.Generate(events, 1920, 1080, settings);

        Assert.Single(segments);
        Assert.InRange(segments[0].End, 1.59d, 1.61d);
    }

    [Fact]
    public void AutoZoomGenerator_KeepsZoomForRealSessionPatternUntilCursorLeavesArea()
    {
        var generator = new AutoZoomGenerator();
        var settings = SessionSettings.CreateDefault().Camera;

        var events = new List<PointerEvent>
        {
            PointerEvent.Down(13.459375d, 913.5d, 288.225d),
            PointerEvent.Move(14.1603604d, 885.0d, 450.9d),
            PointerEvent.Move(16.0017665d, 955.5d, 351.0d),
            PointerEvent.Move(18.6134001d, 953.25d, 396.225d),
            PointerEvent.Move(18.7539643d, 1008.0d, 346.275d),
            PointerEvent.Move(19.2041283d, 1338.0d, 174.825d)
        };

        var segments = generator.Generate(events, 1920, 1080, settings);

        Assert.Single(segments);
        Assert.InRange(segments[0].End, 18.88d, 18.98d);
    }

    [Fact]
    public void AutoZoomGenerator_AddsGentleHoverZoomWhenPointerDwellsWithoutClicks()
    {
        var generator = new AutoZoomGenerator();
        var settings = SessionSettings.CreateDefault().Camera;

        var events = new List<PointerEvent>
        {
            PointerEvent.Move(0.00d, 740.0d, 402.0d),
            PointerEvent.Move(0.24d, 742.0d, 404.0d),
            PointerEvent.Move(0.50d, 746.0d, 400.0d),
            PointerEvent.Move(0.76d, 744.0d, 406.0d),
            PointerEvent.Move(1.02d, 748.0d, 403.0d)
        };

        var segments = generator.Generate(events, 1920, 1080, settings);

        Assert.Single(segments);
        Assert.InRange(segments[0].Scale, 1.2d, 2.0d);
        Assert.InRange(segments[0].End, 1.20d, 1.35d);
    }

    [Fact]
    public void AutoZoomGenerator_TempersClickZoomScale()
    {
        var generator = new AutoZoomGenerator();
        var settings = SessionSettings.CreateDefault().Camera;

        var segments = generator.Generate(
            [PointerEvent.Down(1.0d, 900.0d, 420.0d)],
            1920,
            1080,
            settings);

        Assert.Single(segments);

        var legacyScale = 1.0d / settings.FocusAreaRatio;
        Assert.True(segments[0].Scale < legacyScale);
        Assert.InRange(segments[0].Scale, 2.2d, 2.5d);
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
