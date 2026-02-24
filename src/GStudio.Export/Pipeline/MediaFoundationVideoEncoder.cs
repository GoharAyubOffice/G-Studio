using System.Drawing;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace GStudio.Export.Pipeline;

public sealed class MediaFoundationVideoEncoder : IVideoEncoder
{
    private readonly int _maxFrames;

    public MediaFoundationVideoEncoder(int maxFrames = 450)
    {
        _maxFrames = Math.Max(1, maxFrames);
    }

    public async Task EncodeAsync(VideoEncodeRequest request, CancellationToken cancellationToken = default)
    {
        var framePaths = ResolveFramePaths(request.FrameInputPattern);
        if (framePaths.Length == 0)
        {
            throw new InvalidOperationException("No rendered frames found for Media Foundation export.");
        }

        if (framePaths.Length > _maxFrames)
        {
            throw new InvalidOperationException(
                $"Media Foundation encoder currently supports up to {_maxFrames} frames per export attempt.");
        }

        var frameDuration = TimeSpan.FromSeconds(1.0d / Math.Max(1, request.Fps));
        var composition = new MediaComposition();

        foreach (var framePath in framePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var frameFile = await StorageFile.GetFileFromPathAsync(framePath);
            var clip = await MediaClip.CreateFromImageFileAsync(frameFile, frameDuration);
            composition.Clips.Add(clip);
        }

        await AddBackgroundTrackAsync(composition, request.MicrophoneAudioPath, cancellationToken).ConfigureAwait(false);
        await AddBackgroundTrackAsync(composition, request.SystemAudioPath, cancellationToken).ConfigureAwait(false);

        var outputDirectory = Path.GetDirectoryName(request.OutputMp4Path);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException("Invalid output directory for Media Foundation export.");
        }

        Directory.CreateDirectory(outputDirectory);

        var folder = await StorageFolder.GetFolderFromPathAsync(outputDirectory);
        var outputFile = await folder.CreateFileAsync(Path.GetFileName(request.OutputMp4Path), CreationCollisionOption.ReplaceExisting);

        var profile = CreateEncodingProfile(framePaths[0]);
        var reason = await composition.RenderToFileAsync(outputFile, MediaTrimmingPreference.Precise, profile);

        if (reason != TranscodeFailureReason.None || !File.Exists(request.OutputMp4Path))
        {
            throw new InvalidOperationException($"Media Foundation export failed ({reason}).");
        }
    }

    private static MediaEncodingProfile CreateEncodingProfile(string firstFramePath)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);

        using var firstFrame = new Bitmap(firstFramePath);
        profile.Video.Width = (uint)Math.Max(1, firstFrame.Width);
        profile.Video.Height = (uint)Math.Max(1, firstFrame.Height);

        if (profile.Audio is not null)
        {
            profile.Audio.ChannelCount = 2;
            profile.Audio.SampleRate = 48_000;
            profile.Audio.Bitrate = 192_000;
        }

        return profile;
    }

    private static async Task AddBackgroundTrackAsync(
        MediaComposition composition,
        string? audioPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var audioFile = await StorageFile.GetFileFromPathAsync(audioPath);
        var track = await BackgroundAudioTrack.CreateFromFileAsync(audioFile);
        composition.BackgroundAudioTracks.Add(track);
    }

    private static string[] ResolveFramePaths(string frameInputPattern)
    {
        var frameDirectory = Path.GetDirectoryName(frameInputPattern);
        if (string.IsNullOrWhiteSpace(frameDirectory) || !Directory.Exists(frameDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory
            .GetFiles(frameDirectory, "frame_*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
