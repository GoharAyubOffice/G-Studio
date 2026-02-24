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
        var clampedScale = Math.Clamp((1.0d / settings.FocusAreaRatio) * 0.82d, settings.MinScale, settings.MaxScale);

        var orderedPointerEvents = pointerEvents.OrderBy(static e => e.T).ToArray();
        var clickSegments = new List<ZoomSegment>();

        foreach (var pointerEvent in orderedPointerEvents)
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

            clickSegments.Add(new ZoomSegment(
                Start: start,
                End: end,
                Center: center,
                Scale: clampedScale,
                TriggerCount: 1));
        }

        var orderedSegments = clickSegments
            .OrderBy(static segment => segment.Start)
            .ThenBy(static segment => segment.End)
            .ToArray();

        var extendedSegments = ExtendHoldByLocalPointerActivity(
            orderedSegments,
            orderedPointerEvents,
            viewport,
            settings);

        var hoverSegments = GenerateHoverSegments(
            orderedPointerEvents,
            viewport,
            settings,
            clickScale: clampedScale,
            focusWidth: baseFocusWidth,
            focusHeight: baseFocusHeight);

        if (hoverSegments.Count == 0)
        {
            return SmoothNearbyGaps(
                extendedSegments.OrderBy(static segment => segment.Start).ThenBy(static segment => segment.End),
                viewport,
                settings);
        }

        var combinedSegments = extendedSegments
            .Concat(hoverSegments)
            .OrderBy(static segment => segment.Start)
            .ThenBy(static segment => segment.End);

        return SmoothNearbyGaps(
            combinedSegments,
            viewport,
            settings);
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

    private static IReadOnlyList<ZoomSegment> GenerateHoverSegments(
        PointerEvent[] sortedPointerEvents,
        RectD viewport,
        CameraFollowSettings settings,
        double clickScale,
        double focusWidth,
        double focusHeight)
    {
        if (sortedPointerEvents.Length < 2)
        {
            return Array.Empty<ZoomSegment>();
        }

        var dwellMinDurationSeconds = Math.Max(0.55d, settings.HoldSeconds * 0.9d);
        var dwellMaxWindowSeconds = Math.Max(1.9d, dwellMinDurationSeconds * 2.3d);
        var dwellMaxTravelPixels = Math.Max(26.0d, Math.Min(viewport.Width, viewport.Height) * 0.028d);
        var hoverTailSeconds = Math.Max(0.22d, settings.PreRollSeconds + 0.10d);
        var clickSuppressionRadiusPixels = Math.Max(84.0d, Math.Min(viewport.Width, viewport.Height) * 0.18d);

        var hoverScaleFloor = Math.Max(settings.MinScale + 0.08d, 1.08d);
        var hoverScaleCeiling = Math.Max(hoverScaleFloor, clickScale - 0.22d);
        var hoverScale = Math.Clamp(clickScale * 0.72d, hoverScaleFloor, hoverScaleCeiling);

        var clickEvents = sortedPointerEvents.Where(IsPrimaryClick).ToArray();
        var hoverSegments = new List<ZoomSegment>();

        var startIndex = 0;
        while (startIndex < sortedPointerEvents.Length - 1)
        {
            var startEvent = sortedPointerEvents[startIndex];
            if (!IsPointerPositionEvent(startEvent))
            {
                startIndex++;
                continue;
            }

            var minX = startEvent.X;
            var maxX = startEvent.X;
            var minY = startEvent.Y;
            var maxY = startEvent.Y;
            var lastDwellIndex = -1;

            for (var scanIndex = startIndex + 1; scanIndex < sortedPointerEvents.Length; scanIndex++)
            {
                var pointerEvent = sortedPointerEvents[scanIndex];
                if (!IsPointerPositionEvent(pointerEvent))
                {
                    continue;
                }

                var elapsed = pointerEvent.T - startEvent.T;
                if (elapsed > dwellMaxWindowSeconds)
                {
                    break;
                }

                minX = Math.Min(minX, pointerEvent.X);
                maxX = Math.Max(maxX, pointerEvent.X);
                minY = Math.Min(minY, pointerEvent.Y);
                maxY = Math.Max(maxY, pointerEvent.Y);

                var travelExtent = Math.Max(maxX - minX, maxY - minY);
                if (travelExtent > dwellMaxTravelPixels)
                {
                    break;
                }

                if (elapsed >= dwellMinDurationSeconds)
                {
                    lastDwellIndex = scanIndex;
                }
            }

            if (lastDwellIndex <= startIndex)
            {
                startIndex++;
                continue;
            }

            var dwellEndEvent = sortedPointerEvents[lastDwellIndex];
            var center = viewport.ClampCenter(
                new ScreenPoint((minX + maxX) / 2.0d, (minY + maxY) / 2.0d),
                focusWidth,
                focusHeight);

            var segmentStart = Math.Max(0.0d, startEvent.T - Math.Min(0.08d, settings.PreRollSeconds * 0.5d));
            var segmentEnd = dwellEndEvent.T + hoverTailSeconds;

            if (!HasNearbyClick(clickEvents, center, segmentStart, segmentEnd, clickSuppressionRadiusPixels))
            {
                hoverSegments.Add(new ZoomSegment(
                    Start: segmentStart,
                    End: segmentEnd,
                    Center: center,
                    Scale: hoverScale,
                    TriggerCount: 1));
            }

            startIndex = Math.Max(startIndex + 1, lastDwellIndex);
        }

        return hoverSegments;
    }

    private static bool HasNearbyClick(
        PointerEvent[] clickEvents,
        ScreenPoint center,
        double start,
        double end,
        double radius)
    {
        if (clickEvents.Length == 0)
        {
            return false;
        }

        var lowerTime = Math.Max(0.0d, start - 0.15d);
        var upperTime = end + 0.24d;

        foreach (var clickEvent in clickEvents)
        {
            if (clickEvent.T < lowerTime || clickEvent.T > upperTime)
            {
                continue;
            }

            if (center.DistanceTo(new ScreenPoint(clickEvent.X, clickEvent.Y)) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointerPositionEvent(PointerEvent pointerEvent)
    {
        if (!double.IsFinite(pointerEvent.X) || !double.IsFinite(pointerEvent.Y) || !double.IsFinite(pointerEvent.T))
        {
            return false;
        }

        return pointerEvent.Type is PointerEventKinds.Move or PointerEventKinds.Down or PointerEventKinds.Up;
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

    private static IReadOnlyList<ZoomSegment> ExtendHoldByLocalPointerActivity(
        ZoomSegment[] sortedSegments,
        PointerEvent[] sortedPointerEvents,
        RectD viewport,
        CameraFollowSettings settings)
    {
        if (sortedSegments.Length == 0 || sortedPointerEvents.Length == 0)
        {
            return sortedSegments;
        }

        var stayRadiusPixels = Math.Max(64.0d, Math.Min(viewport.Width, viewport.Height) * 0.24d);
        var exitRadiusPixels = stayRadiusPixels * 1.28d;
        var extensionTailSeconds = Math.Max(0.14d, settings.PreRollSeconds + 0.04d);
        var maxExtensionSeconds = Math.Max(1.8d, settings.HoldSeconds * 10.0d);

        var searchStartIndex = 0;

        for (var segmentIndex = 0; segmentIndex < sortedSegments.Length; segmentIndex++)
        {
            var segment = sortedSegments[segmentIndex];
            var clickTime = Math.Clamp(segment.Start + settings.PreRollSeconds, segment.Start, segment.End);
            var nextSegmentStart = segmentIndex + 1 < sortedSegments.Length
                ? sortedSegments[segmentIndex + 1].Start
                : double.PositiveInfinity;
            var scanUntil = Math.Min(nextSegmentStart, clickTime + maxExtensionSeconds);

            while (searchStartIndex < sortedPointerEvents.Length && sortedPointerEvents[searchStartIndex].T < clickTime)
            {
                searchStartIndex++;
            }

            var hasInsideAfterHold = false;
            var lastInsideTime = clickTime;
            var extendedEnd = segment.End;

            for (var eventIndex = searchStartIndex; eventIndex < sortedPointerEvents.Length; eventIndex++)
            {
                var pointerEvent = sortedPointerEvents[eventIndex];
                if (pointerEvent.T > scanUntil)
                {
                    break;
                }

                var distance = segment.Center.DistanceTo(new ScreenPoint(pointerEvent.X, pointerEvent.Y));
                if (distance <= stayRadiusPixels)
                {
                    lastInsideTime = pointerEvent.T;
                    if (pointerEvent.T >= segment.End)
                    {
                        hasInsideAfterHold = true;
                    }

                    continue;
                }

                if (pointerEvent.T >= segment.End && hasInsideAfterHold && distance >= exitRadiusPixels)
                {
                    extendedEnd = Math.Max(extendedEnd, Math.Min(scanUntil, lastInsideTime + extensionTailSeconds));
                    break;
                }
            }

            if (hasInsideAfterHold)
            {
                extendedEnd = Math.Max(extendedEnd, Math.Min(scanUntil, lastInsideTime + extensionTailSeconds));
            }

            sortedSegments[segmentIndex] = segment with
            {
                End = Math.Max(segment.End, Math.Min(nextSegmentStart, extendedEnd))
            };
        }

        return sortedSegments;
    }

}
