namespace GStudio.Export.Pipeline;

public sealed record VideoEncodeRequest(
    string FrameInputPattern,
    string OutputMp4Path,
    int Fps,
    int? FrameCount = null,
    double? TargetDurationSeconds = null,
    string? MicrophoneAudioPath = null,
    string? SystemAudioPath = null,
    bool PreferMediaFoundation = true,
    bool AllowFfmpegFallback = true);

public interface IVideoEncoder
{
    Task EncodeAsync(VideoEncodeRequest request, CancellationToken cancellationToken = default);
}
