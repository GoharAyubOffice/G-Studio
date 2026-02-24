using System.Text.Json.Serialization;

namespace GStudio.Common.Events;

public sealed record FrameTimestampEvent(
    [property: JsonPropertyName("t")] double Time,
    [property: JsonPropertyName("i")] int Index);
