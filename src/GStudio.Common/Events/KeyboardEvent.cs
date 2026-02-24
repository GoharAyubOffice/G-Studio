using System.Text.Json.Serialization;

namespace GStudio.Common.Events;

public sealed record KeyboardEvent(
    [property: JsonPropertyName("t")] double T,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("mods")] IReadOnlyList<string>? Mods,
    [property: JsonPropertyName("key")] string Key)
{
    public static KeyboardEvent Shortcut(double t, IReadOnlyList<string> mods, string key) =>
        new(t, "shortcut", mods, key);

    public static KeyboardEvent KeyPress(double t, string key) =>
        new(t, "key", null, key);
}
