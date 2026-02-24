using GStudio.Common.Configuration;
using GStudio.Common.Geometry;

namespace GStudio.Cinematic.Engine;

public sealed class SpringCameraSolver
{
    public IReadOnlyList<CameraTransform> Solve(
        double durationSeconds,
        int fps,
        RectD viewport,
        IReadOnlyList<ZoomSegment> zoomSegments,
        MotionPreset preset)
    {
        var safeFps = Math.Max(1, fps);
        var safeDuration = Math.Max(durationSeconds, 0.1d);
        var frameCount = Math.Max(1, (int)Math.Ceiling(safeDuration * safeFps));
        var dt = 1.0d / safeFps;

        var settings = SpringPresetMap.Get(preset);

        var xState = new SpringState(viewport.Center.X);
        var yState = new SpringState(viewport.Center.Y);
        var scaleState = new SpringState(1.0d);

        var frames = new List<CameraTransform>(frameCount + 1);
        var sortedSegments = zoomSegments.OrderBy(static s => s.Start).ToArray();
        var activeLowerBound = 0;

        for (var frame = 0; frame <= frameCount; frame++)
        {
            var time = frame * dt;
            if (time > safeDuration)
            {
                time = safeDuration;
            }

            while (activeLowerBound < sortedSegments.Length && sortedSegments[activeLowerBound].End < time)
            {
                activeLowerBound++;
            }

            var active = FindLatestActive(sortedSegments, activeLowerBound, time);

            var targetCenter = active?.Center ?? viewport.Center;
            var targetScale = active?.Scale ?? 1.0d;

            Integrate(ref xState, targetCenter.X, settings, dt);
            Integrate(ref yState, targetCenter.Y, settings, dt);
            Integrate(ref scaleState, targetScale, settings, dt);

            frames.Add(new CameraTransform(
                Time: time,
                Center: new ScreenPoint(xState.Position, yState.Position),
                Scale: Math.Max(1.0d, scaleState.Position),
                Rotation: 0.0d));
        }

        return frames;
    }

    private static ZoomSegment? FindLatestActive(ZoomSegment[] segments, int lowerBound, double time)
    {
        if (segments.Length == 0 || lowerBound >= segments.Length)
        {
            return null;
        }

        ZoomSegment? selected = null;
        for (var index = lowerBound; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Start > time)
            {
                break;
            }

            if (segment.End >= time)
            {
                selected = segment;
            }
        }

        return selected;
    }

    private static void Integrate(ref SpringState state, double target, SpringSettings settings, double dt)
    {
        var acceleration = ((target - state.Position) * settings.Tension) - (state.Velocity * settings.Friction);
        acceleration /= settings.Mass;

        state.Velocity += acceleration * dt;
        state.Position += state.Velocity * dt;
    }

    private struct SpringState
    {
        public SpringState(double position)
        {
            Position = position;
            Velocity = 0.0d;
        }

        public double Position;

        public double Velocity;
    }
}
