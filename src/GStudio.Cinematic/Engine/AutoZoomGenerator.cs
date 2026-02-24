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

        var unmergedSegments = new List<ZoomSegment>();

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

            unmergedSegments.Add(new ZoomSegment(
                Start: start,
                End: end,
                Center: center,
                Scale: clampedScale,
                TriggerCount: 1));
        }

        return MergeOverlaps(unmergedSegments);
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

    private static IReadOnlyList<ZoomSegment> MergeOverlaps(List<ZoomSegment> segments)
    {
        if (segments.Count == 0)
        {
            return Array.Empty<ZoomSegment>();
        }

        segments.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<ZoomSegment>(segments.Count);

        var current = segments[0];
        for (var index = 1; index < segments.Count; index++)
        {
            var next = segments[index];

            if (next.Start <= current.End)
            {
                current = new ZoomSegment(
                    Start: current.Start,
                    End: Math.Max(current.End, next.End),
                    Center: BlendCenter(current, next),
                    Scale: Math.Max(current.Scale, next.Scale),
                    TriggerCount: current.TriggerCount + next.TriggerCount);

                continue;
            }

            merged.Add(current);
            current = next;
        }

        merged.Add(current);
        return merged;
    }

    private static ScreenPoint BlendCenter(ZoomSegment left, ZoomSegment right)
    {
        var totalWeight = left.TriggerCount + right.TriggerCount;
        if (totalWeight <= 0)
        {
            return right.Center;
        }

        var leftWeight = left.TriggerCount / (double)totalWeight;
        var rightWeight = right.TriggerCount / (double)totalWeight;

        return new ScreenPoint(
            (left.Center.X * leftWeight) + (right.Center.X * rightWeight),
            (left.Center.Y * leftWeight) + (right.Center.Y * rightWeight));
    }
}
