using System.Text;
using System.Text.Json;
using GStudio.Render.Composition;
using GStudio.Render.Preview;

namespace GStudio.Export.Pipeline;

public sealed class ExportPackageWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly CinematicFrameRenderer _frameRenderer;
    private readonly IVideoEncoder _videoEncoder;

    public ExportPackageWriter(
        CinematicFrameRenderer? frameRenderer = null,
        IVideoEncoder? videoEncoder = null)
    {
        _frameRenderer = frameRenderer ?? new CinematicFrameRenderer();
        _videoEncoder = videoEncoder ?? new AdaptiveVideoEncoder();
    }

    public async Task<ExportPackageResult> WriteAsync(ExportRequest request, CancellationToken cancellationToken = default)
    {
        var safeName = SanitizeName(request.OutputName);
        var packageDirectory = Path.Combine(request.OutputDirectory, safeName);
        Directory.CreateDirectory(packageDirectory);

        var planFilePath = Path.Combine(packageDirectory, "render_plan.json");
        var encodeScriptPath = Path.Combine(packageDirectory, "encode_with_ffmpeg.cmd");
        var renderedFramesDirectory = Path.Combine(packageDirectory, "rendered_frames");
        var outputMp4Path = Path.Combine(request.OutputDirectory, safeName + ".mp4");

        await WritePlanFileAsync(planFilePath, request.PreviewPlan, cancellationToken).ConfigureAwait(false);

        var renderedFrameSet = await _frameRenderer.RenderAsync(
            plan: request.PreviewPlan,
            sourceFramesDirectory: request.Session.Paths.CaptureFramesDirectory,
            outputFramesDirectory: renderedFramesDirectory,
            designWidth: request.Session.Manifest.Settings.Video.Width,
            designHeight: request.Session.Manifest.Settings.Video.Height,
            motionBlurStrength: request.Session.Manifest.Settings.MotionBlur.ScreenBlurStrength,
            frameTimelinePath: request.Session.Paths.CaptureFrameTimelinePath,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var frameInputPattern = Path.Combine(renderedFrameSet.FramesDirectory, "frame_%06d.png");
        var fps = Math.Max(1, request.PreviewPlan.Fps);
        var targetDurationSeconds = renderedFrameSet.FrameCount / (double)fps;
        var microphoneAudioPath = ResolveOptionalFilePath(request.Session.Paths.MicrophoneAudioPath);
        var systemAudioPath = ResolveOptionalFilePath(request.Session.Paths.SystemAudioPath);

        var encodeRequest = new VideoEncodeRequest(
            FrameInputPattern: frameInputPattern,
            OutputMp4Path: outputMp4Path,
            Fps: fps,
            FrameCount: renderedFrameSet.FrameCount,
            TargetDurationSeconds: targetDurationSeconds,
            MicrophoneAudioPath: microphoneAudioPath,
            SystemAudioPath: systemAudioPath);

        await WriteEncodeScriptAsync(
            scriptPath: encodeScriptPath,
            frameInputDirectory: renderedFrameSet.FramesDirectory,
            outputMp4Path: outputMp4Path,
            fps: fps,
            targetDurationSeconds: targetDurationSeconds,
            microphoneAudioPath: microphoneAudioPath,
            systemAudioPath: systemAudioPath,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var videoEncoded = false;
        if (request.EncodeVideo)
        {
            await _videoEncoder.EncodeAsync(encodeRequest, cancellationToken).ConfigureAwait(false);

            videoEncoded = true;
        }

        return new ExportPackageResult(
            PackageDirectory: packageDirectory,
            PlanFilePath: planFilePath,
            EncodeScriptPath: encodeScriptPath,
            RenderedFramesDirectory: renderedFrameSet.FramesDirectory,
            RenderedFrameCount: renderedFrameSet.FrameCount,
            OutputMp4Path: outputMp4Path,
            VideoEncoded: videoEncoded);
    }

    private static async Task WritePlanFileAsync(string planFilePath, PreviewRenderPlan plan, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            planFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            useAsync: true);

        await JsonSerializer.SerializeAsync(stream, plan, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteEncodeScriptAsync(
        string scriptPath,
        string frameInputDirectory,
        string outputMp4Path,
        int fps,
        double targetDurationSeconds,
        string? microphoneAudioPath,
        string? systemAudioPath,
        CancellationToken cancellationToken)
    {
        var frameInputPattern = Path.Combine(frameInputDirectory, "frame_%06d.png");
        var ffmpegCommand = FfmpegCommandBuilder.BuildScriptCommand(new VideoEncodeRequest(
            FrameInputPattern: frameInputPattern,
            OutputMp4Path: outputMp4Path,
            Fps: fps,
            FrameCount: null,
            TargetDurationSeconds: targetDurationSeconds,
            MicrophoneAudioPath: microphoneAudioPath,
            SystemAudioPath: systemAudioPath));

        var script = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine("setlocal")
            .AppendLine($"set \"FRAME_PATTERN={frameInputPattern}\"")
            .AppendLine($"set \"OUTPUT_FILE={outputMp4Path}\"")
            .AppendLine(ffmpegCommand)
            .AppendLine("if errorlevel 1 (")
            .AppendLine("  echo FFmpeg encode failed.")
            .AppendLine("  exit /b 1")
            .AppendLine(")")
            .AppendLine("echo Export complete: %OUTPUT_FILE%")
            .AppendLine("endlocal")
            .ToString();

        await File.WriteAllTextAsync(scriptPath, script, cancellationToken).ConfigureAwait(false);
    }

    private static string? ResolveOptionalFilePath(string candidatePath)
    {
        return File.Exists(candidatePath) ? candidatePath : null;
    }

    private static string SanitizeName(string candidate)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitizedChars = candidate
            .Select(c => invalid.Contains(c) ? '_' : c)
            .ToArray();

        var sanitized = new string(sanitizedChars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "export" : sanitized;
    }
}
