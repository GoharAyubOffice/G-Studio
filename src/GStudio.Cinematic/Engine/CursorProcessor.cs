using GStudio.Common.Configuration;
using GStudio.Common.Events;
using GStudio.Common.Geometry;
using GStudio.Common.Timeline;

namespace GStudio.Cinematic.Engine;

public sealed class CursorProcessor
{
    public IReadOnlyList<PointerEvent> Smooth(
        IReadOnlyList<PointerEvent> pointerEvents,
        CursorPolishSettings settings,
        IReadOnlyList<TimeRange>? bypassRanges = null)
    {
        if (pointerEvents.Count == 0)
        {
            return Array.Empty<PointerEvent>();
        }

        var ordered = pointerEvents.OrderBy(static e => e.T).ToArray();
        if (!settings.SmoothEnabled && !settings.RemoveShakes)
        {
            return ordered;
        }

        var activeBypassRanges = bypassRanges?.Select(static range => range.Normalize()).ToArray() ?? Array.Empty<TimeRange>();

        var filterX = new OneEuroFilter(settings.OneEuroMinCutoff, settings.OneEuroBeta, settings.OneEuroDerivativeCutoff);
        var filterY = new OneEuroFilter(settings.OneEuroMinCutoff, settings.OneEuroBeta, settings.OneEuroDerivativeCutoff);

        var output = new List<PointerEvent>(ordered.Length);
        ScreenPoint? previousInput = null;
        ScreenPoint? previousOutput = null;

        foreach (var pointerEvent in ordered)
        {
            if (!IsPositionEvent(pointerEvent))
            {
                output.Add(pointerEvent);
                continue;
            }

            var raw = new ScreenPoint(pointerEvent.X, pointerEvent.Y);
            var bypassSmooth = IsBypassed(pointerEvent.T, activeBypassRanges);

            var smooth = raw;
            if (settings.SmoothEnabled && !bypassSmooth)
            {
                smooth = new ScreenPoint(
                    filterX.Filter(raw.X, pointerEvent.T),
                    filterY.Filter(raw.Y, pointerEvent.T));
            }
            else
            {
                filterX.Reset(raw.X, pointerEvent.T);
                filterY.Reset(raw.Y, pointerEvent.T);
            }

            if (
                settings.RemoveShakes &&
                previousInput is { } priorInput &&
                previousOutput is { } priorOutput &&
                priorInput.DistanceTo(raw) <= settings.ShakeThresholdPixels)
            {
                smooth = priorOutput;
            }

            previousInput = raw;
            previousOutput = smooth;

            output.Add(pointerEvent with { X = smooth.X, Y = smooth.Y });
        }

        return output;
    }

    public IReadOnlyList<CursorSample> BuildSamples(
        IReadOnlyList<PointerEvent> pointerEvents,
        double durationSeconds,
        int fps,
        CursorPolishSettings settings)
    {
        if (pointerEvents.Count == 0)
        {
            return Array.Empty<CursorSample>();
        }

        var orderedMoves = pointerEvents
            .Where(IsPositionEvent)
            .OrderBy(static e => e.T)
            .ToArray();

        if (orderedMoves.Length == 0)
        {
            return Array.Empty<CursorSample>();
        }

        var safeFps = Math.Max(1, fps);
        var safeDuration = Math.Max(0.1d, durationSeconds);
        var dt = 1.0d / safeFps;
        var frameCount = (int)Math.Ceiling(safeDuration * safeFps);

        var samples = new List<CursorSample>(frameCount + 1);
        var previousMovement = orderedMoves[0].T;

        for (var frame = 0; frame <= frameCount; frame++)
        {
            var time = frame * dt;
            if (time > safeDuration)
            {
                time = safeDuration;
            }

            var position = InterpolatePosition(orderedMoves, time);

            if (frame > 0)
            {
                var prior = samples[frame - 1].Position;
                if (prior.DistanceTo(position) > 0.01d)
                {
                    previousMovement = time;
                }
            }

            var hidden = settings.HideIdle && (time - previousMovement) >= settings.IdleSeconds;

            samples.Add(new CursorSample(
                Time: time,
                Position: position,
                Hidden: hidden));
        }

        return samples;
    }

    private static bool IsPositionEvent(PointerEvent pointerEvent)
    {
        return string.Equals(pointerEvent.Type, PointerEventKinds.Move, StringComparison.OrdinalIgnoreCase)
            || string.Equals(pointerEvent.Type, PointerEventKinds.Down, StringComparison.OrdinalIgnoreCase)
            || string.Equals(pointerEvent.Type, PointerEventKinds.Up, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBypassed(double time, IReadOnlyList<TimeRange> bypassRanges)
    {
        if (bypassRanges.Count == 0)
        {
            return false;
        }

        foreach (var range in bypassRanges)
        {
            if (range.Contains(time))
            {
                return true;
            }
        }

        return false;
    }

    private static ScreenPoint InterpolatePosition(IReadOnlyList<PointerEvent> moves, double time)
    {
        if (time <= moves[0].T)
        {
            return new ScreenPoint(moves[0].X, moves[0].Y);
        }

        for (var index = 1; index < moves.Count; index++)
        {
            var right = moves[index];
            if (time > right.T)
            {
                continue;
            }

            var left = moves[index - 1];
            var duration = Math.Max(0.0001d, right.T - left.T);
            var alpha = (time - left.T) / duration;

            return ScreenPoint.Lerp(
                new ScreenPoint(left.X, left.Y),
                new ScreenPoint(right.X, right.Y),
                alpha);
        }

        var last = moves[^1];
        return new ScreenPoint(last.X, last.Y);
    }

    private sealed class OneEuroFilter
    {
        private readonly double _minCutoff;
        private readonly double _beta;
        private readonly double _derivativeCutoff;

        private LowPassFilter _valueFilter = new();
        private LowPassFilter _derivativeFilter = new();
        private bool _isInitialized;
        private double _lastTime;
        private double _lastRaw;

        public OneEuroFilter(double minCutoff, double beta, double derivativeCutoff)
        {
            _minCutoff = Math.Max(0.0001d, minCutoff);
            _beta = Math.Max(0.0d, beta);
            _derivativeCutoff = Math.Max(0.0001d, derivativeCutoff);
        }

        public double Filter(double value, double time)
        {
            if (!_isInitialized)
            {
                Reset(value, time);
                return value;
            }

            var dt = Math.Max(0.0001d, time - _lastTime);
            _lastTime = time;

            var derivative = (value - _lastRaw) / dt;
            _lastRaw = value;

            var derivativeAlpha = Alpha(dt, _derivativeCutoff);
            var filteredDerivative = _derivativeFilter.Filter(derivative, derivativeAlpha);

            var cutoff = _minCutoff + (_beta * Math.Abs(filteredDerivative));
            var valueAlpha = Alpha(dt, cutoff);

            return _valueFilter.Filter(value, valueAlpha);
        }

        public void Reset(double value, double time)
        {
            _valueFilter = new LowPassFilter(value);
            _derivativeFilter = new LowPassFilter(0.0d);
            _lastRaw = value;
            _lastTime = time;
            _isInitialized = true;
        }

        private static double Alpha(double dt, double cutoff)
        {
            var tau = 1.0d / (2.0d * Math.PI * cutoff);
            return 1.0d / (1.0d + (tau / dt));
        }
    }

    private struct LowPassFilter
    {
        private bool _initialized;
        private double _value;

        public LowPassFilter(double initialValue)
        {
            _initialized = true;
            _value = initialValue;
        }

        public double Filter(double value, double alpha)
        {
            if (!_initialized)
            {
                _value = value;
                _initialized = true;
                return value;
            }

            _value = (alpha * value) + ((1.0d - alpha) * _value);
            return _value;
        }
    }
}
