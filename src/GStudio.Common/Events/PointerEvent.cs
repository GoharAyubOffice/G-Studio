using System.Text.Json.Serialization;

namespace GStudio.Common.Events;

public static class PointerEventKinds
{
    public const string Move = "move";
    public const string Down = "down";
    public const string Up = "up";
    public const string Wheel = "wheel";
    public const string CursorShape = "cursorShape";
}

public sealed record PointerEvent(
    [property: JsonPropertyName("t")] double T,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("btn")] string? Btn = null,
    [property: JsonPropertyName("dx")] double? Dx = null,
    [property: JsonPropertyName("dy")] double? Dy = null,
    [property: JsonPropertyName("shape")] string? Shape = null)
{
    public static PointerEvent Move(double t, double x, double y) =>
        new(t, PointerEventKinds.Move, x, y);

    public static PointerEvent Down(double t, double x, double y, string button = "left") =>
        new(t, PointerEventKinds.Down, x, y, Btn: button);

    public static PointerEvent Up(double t, double x, double y, string button = "left") =>
        new(t, PointerEventKinds.Up, x, y, Btn: button);

    public static PointerEvent Wheel(double t, double x, double y, double dx, double dy) =>
        new(t, PointerEventKinds.Wheel, x, y, Dx: dx, Dy: dy);
}
