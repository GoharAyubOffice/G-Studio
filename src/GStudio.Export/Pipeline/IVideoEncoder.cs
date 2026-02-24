namespace GStudio.Export.Pipeline;

public interface IVideoEncoder
{
    Task EncodeAsync(
        string frameInputPattern,
        string outputMp4Path,
        int fps,
        CancellationToken cancellationToken = default);
}
