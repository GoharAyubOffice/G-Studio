namespace GStudio.Export.Pipeline;

public sealed class AdaptiveVideoEncoder : IVideoEncoder
{
    private readonly IVideoEncoder _mediaFoundationEncoder;
    private readonly IVideoEncoder _ffmpegEncoder;

    public AdaptiveVideoEncoder(
        IVideoEncoder? mediaFoundationEncoder = null,
        IVideoEncoder? ffmpegEncoder = null)
    {
        _mediaFoundationEncoder = mediaFoundationEncoder ?? new MediaFoundationVideoEncoder();
        _ffmpegEncoder = ffmpegEncoder ?? new FfmpegVideoEncoder();
    }

    public async Task EncodeAsync(VideoEncodeRequest request, CancellationToken cancellationToken = default)
    {
        if (request.PreferMediaFoundation)
        {
            try
            {
                await _mediaFoundationEncoder.EncodeAsync(request, cancellationToken).ConfigureAwait(false);
                if (File.Exists(request.OutputMp4Path))
                {
                    return;
                }
            }
            catch when (request.AllowFfmpegFallback)
            {
            }
        }

        await _ffmpegEncoder.EncodeAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
