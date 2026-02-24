using System.Text.Json;
using System.Text.Json.Serialization;

namespace GStudio.Project.Store;

internal static class JsonSerialization
{
    public static readonly JsonSerializerOptions LineOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public static readonly JsonSerializerOptions DocumentOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
        WriteIndented = true
    };
}
