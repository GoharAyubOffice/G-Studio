namespace GStudio.Export.Pipeline;

public sealed record VideoEncodeRequest(
    string FrameInputPattern,
    string OutputMp4Path,
    int Fps,
    double? TargetDurationSeconds = null,
    string? MicrophoneAudioPath = null,
    string? SystemAudioPath = null);

public interface IVideoEncoder
{
    Task EncodeAsync(VideoEncodeRequest request, CancellationToken cancellationToken = default);
}
