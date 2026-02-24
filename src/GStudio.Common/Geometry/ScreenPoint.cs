namespace GStudio.Common.Geometry;

public readonly record struct ScreenPoint(double X, double Y)
{
    public static ScreenPoint Lerp(ScreenPoint from, ScreenPoint to, double alpha)
    {
        var clamped = double.IsFinite(alpha) ? Math.Clamp(alpha, 0.0d, 1.0d) : 0.0d;
        return new ScreenPoint(
            from.X + ((to.X - from.X) * clamped),
            from.Y + ((to.Y - from.Y) * clamped));
    }

    public double DistanceTo(ScreenPoint other)
    {
        var dx = other.X - X;
        var dy = other.Y - Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
