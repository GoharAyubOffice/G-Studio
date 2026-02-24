namespace GStudio.Common.Geometry;

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public ScreenPoint Center => new(X + (Width / 2.0d), Y + (Height / 2.0d));

    public static RectD Around(ScreenPoint center, double width, double height)
    {
        var safeWidth = Math.Max(width, 1.0d);
        var safeHeight = Math.Max(height, 1.0d);
        return new RectD(center.X - (safeWidth / 2.0d), center.Y - (safeHeight / 2.0d), safeWidth, safeHeight);
    }

    public ScreenPoint ClampPoint(ScreenPoint point)
    {
        return new ScreenPoint(
            Math.Clamp(point.X, X, Right),
            Math.Clamp(point.Y, Y, Bottom));
    }

    public ScreenPoint ClampCenter(ScreenPoint center, double focusWidth, double focusHeight)
    {
        var halfWidth = Math.Max(0.5d, focusWidth / 2.0d);
        var halfHeight = Math.Max(0.5d, focusHeight / 2.0d);
        return new ScreenPoint(
            Math.Clamp(center.X, X + halfWidth, Right - halfWidth),
            Math.Clamp(center.Y, Y + halfHeight, Bottom - halfHeight));
    }
}
