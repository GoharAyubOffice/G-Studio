using GStudio.Common.Configuration;
using GStudio.Common.Events;
using GStudio.Common.Geometry;

namespace GStudio.Cinematic.Engine;

public sealed class AutoZoomGenerator
{
    public IReadOnlyList<ZoomSegment> Generate(
        IReadOnlyList<PointerEvent> pointerEvents,
        int viewportWidth,
        int viewportHeight,
        CameraFollowSettings settings)
    {
        if (pointerEvents.Count == 0 || viewportWidth <= 1 || viewportHeight <= 1)
        {
            return Array.Empty<ZoomSegment>();
        }

        var viewport = new RectD(0.0d, 0.0d, viewportWidth, viewportHeight);
        var baseFocusWidth = Math.Max(8.0d, viewportWidth * settings.FocusAreaRatio);
        var baseFocusHeight = Math.Max(8.0d, viewportHeight * settings.FocusAreaRatio);
        var clampedScale = Math.Clamp(1.0d / settings.FocusAreaRatio, settings.MinScale, settings.MaxScale);

        var segments = new List<ZoomSegment>();

        foreach (var pointerEvent in pointerEvents.OrderBy(static e => e.T))
        {
            if (!IsPrimaryClick(pointerEvent))
            {
                continue;
            }

            var center = viewport.ClampCenter(
                new ScreenPoint(pointerEvent.X, pointerEvent.Y),
                baseFocusWidth,
                baseFocusHeight);

            var start = Math.Max(0.0d, pointerEvent.T - settings.PreRollSeconds);
            var end = pointerEvent.T + settings.HoldSeconds;

            segments.Add(new ZoomSegment(
                Start: start,
                End: end,
                Center: center,
                Scale: clampedScale,
                TriggerCount: 1));
        }

        var orderedSegments = segments
            .OrderBy(static segment => segment.Start)
            .ThenBy(static segment => segment.End);

        return SmoothNearbyGaps(orderedSegments, viewport, settings);
    }

    private static bool IsPrimaryClick(PointerEvent pointerEvent)
    {
        if (!string.Equals(pointerEvent.Type, PointerEventKinds.Down, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(pointerEvent.Btn))
        {
            return true;
        }

        return string.Equals(pointerEvent.Btn, "left", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ZoomSegment> SmoothNearbyGaps(
        IOrderedEnumerable<ZoomSegment> sortedSegments,
        RectD viewport,
        CameraFollowSettings settings)
    {
        var segments = sortedSegments.ToArray();
        if (segments.Length <= 1)
        {
            return segments;
        }

        var bridgeGapSeconds = Math.Max(0.12d, settings.PreRollSeconds + 0.05d);
        var bridgeDistancePixels = Math.Max(48.0d, Math.Min(viewport.Width, viewport.Height) * 0.25d);

        for (var index = 0; index < segments.Length - 1; index++)
        {
            var current = segments[index];
            var next = segments[index + 1];

            var gap = next.Start - current.End;
            if (gap <= 0.0d || gap > bridgeGapSeconds)
            {
                continue;
            }

            var distance = current.Center.DistanceTo(next.Center);
            if (distance > bridgeDistancePixels)
            {
                continue;
            }

            segments[index] = current with { End = next.Start };
        }

        return segments;
    }

}
