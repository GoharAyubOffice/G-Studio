using System.Text.Json.Serialization;

namespace GStudio.Common.Events;

public sealed record WindowEvent(
    [property: JsonPropertyName("t")] double T,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("w")] int Width,
    [property: JsonPropertyName("h")] int Height,
    [property: JsonPropertyName("dpi")] double Dpi,
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("colorSpace")] string? ColorSpace = null);
