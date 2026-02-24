using System.Text.Json.Serialization;
using GStudio.Common.Configuration;

namespace GStudio.Common.Project;

public sealed record CaptureStats
{
    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; init; }

    [JsonPropertyName("frameCount")]
    public int FrameCount { get; init; }

    [JsonPropertyName("pointerEventCount")]
    public int PointerEventCount { get; init; }

    [JsonPropertyName("keyboardEventCount")]
    public int KeyboardEventCount { get; init; }

    [JsonPropertyName("windowEventCount")]
    public int WindowEventCount { get; init; }

    [JsonPropertyName("targetFps")]
    public int TargetFps { get; init; }

    [JsonPropertyName("effectiveFps")]
    public double EffectiveFps { get; init; }

    [JsonPropertyName("captureBackend")]
    public string CaptureBackend { get; init; } = "unknown";

    [JsonPropertyName("captureBackendDetails")]
    public string CaptureBackendDetails { get; init; } = "unknown";

    [JsonPropertyName("captureMissCount")]
    public int CaptureMissCount { get; init; }

    [JsonPropertyName("reusedFrameCount")]
    public int ReusedFrameCount { get; init; }

    [JsonPropertyName("frameTimelineCount")]
    public int FrameTimelineCount { get; init; }

    [JsonPropertyName("timelineDurationSeconds")]
    public double TimelineDurationSeconds { get; init; }

    [JsonPropertyName("timelineEffectiveFps")]
    public double TimelineEffectiveFps { get; init; }

    [JsonPropertyName("averageFrameIntervalMs")]
    public double AverageFrameIntervalMs { get; init; }

    [JsonPropertyName("frameIntervalJitterMs")]
    public double FrameIntervalJitterMs { get; init; }

    [JsonPropertyName("maxFrameIntervalMs")]
    public double MaxFrameIntervalMs { get; init; }

    [JsonPropertyName("durationDriftMs")]
    public double DurationDriftMs { get; init; }
}

public sealed record ProjectManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "1.0";

    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; init; }

    [JsonPropertyName("endedUtc")]
    public DateTimeOffset? EndedUtc { get; init; }

    [JsonPropertyName("settings")]
    public required SessionSettings Settings { get; init; }

    [JsonPropertyName("captureStats")]
    public CaptureStats CaptureStats { get; init; } = new();
}
