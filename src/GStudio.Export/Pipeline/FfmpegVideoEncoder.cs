using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace GStudio.Export.Pipeline;

public sealed class FfmpegVideoEncoder : IVideoEncoder
{
    public async Task EncodeAsync(VideoEncodeRequest request, CancellationToken cancellationToken = default)
    {
        var safeFps = Math.Max(1, request.Fps);
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputMp4Path) ?? ".");

        var args = BuildArguments(
            request.FrameInputPattern,
            request.OutputMp4Path,
            safeFps,
            request.TargetDurationSeconds,
            request.MicrophoneAudioPath,
            request.SystemAudioPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var stdOut = await stdOutTask.ConfigureAwait(false);
            var stdErr = await stdErrTask.ConfigureAwait(false);

            if (process.ExitCode != 0 || !File.Exists(request.OutputMp4Path))
            {
                var details = BuildFailureDetails(process.ExitCode, stdOut, stdErr);
                throw new InvalidOperationException($"FFmpeg encode failed. {details}");
            }
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                "FFmpeg is not available on PATH. Install FFmpeg or run the generated encode script manually.",
                ex);
        }
    }

    private static string BuildArguments(
        string frameInputPattern,
        string outputMp4Path,
        int fps,
        double? targetDurationSeconds,
        string? microphoneAudioPath,
        string? systemAudioPath)
    {
        var durationSeconds = targetDurationSeconds.GetValueOrDefault(0.0d);
        var hasDuration = durationSeconds > 0.01d;
        var durationText = durationSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        var hasMic = !string.IsNullOrWhiteSpace(microphoneAudioPath) && File.Exists(microphoneAudioPath);
        var hasSystem = !string.IsNullOrWhiteSpace(systemAudioPath) && File.Exists(systemAudioPath);

        if (hasMic && hasSystem)
        {
            var durationOption = hasDuration ? $" -t {durationText}" : string.Empty;
            return
                $"-y -framerate {fps} -i \"{frameInputPattern}\" -i \"{microphoneAudioPath}\" -i \"{systemAudioPath}\" " +
                $"-filter_complex \"[1:a]aresample=async=1:first_pts=0,apad,atrim=0:{durationText}[a1];[2:a]aresample=async=1:first_pts=0,apad,atrim=0:{durationText}[a2];[a1][a2]amix=inputs=2:duration=longest,atrim=0:{durationText}[aout]\" -map 0:v:0 -map \"[aout]\" " +
                $"-c:v libx264 -pix_fmt yuv420p -c:a aac -movflags +faststart{durationOption} \"{outputMp4Path}\"";
        }

        if (hasMic || hasSystem)
        {
            var audioPath = hasMic ? microphoneAudioPath! : systemAudioPath!;
            var durationOption = hasDuration ? $" -t {durationText}" : string.Empty;
            return
                $"-y -framerate {fps} -i \"{frameInputPattern}\" -i \"{audioPath}\" -map 0:v:0 -map 1:a:0 " +
                $"-c:v libx264 -pix_fmt yuv420p -filter:a \"aresample=async=1:first_pts=0,apad,atrim=0:{durationText}\" -c:a aac -movflags +faststart{durationOption} \"{outputMp4Path}\"";
        }

        var videoDurationOption = hasDuration ? $" -t {durationText}" : string.Empty;
        return
            $"-y -framerate {fps} -i \"{frameInputPattern}\" -c:v libx264 -pix_fmt yuv420p -movflags +faststart{videoDurationOption} \"{outputMp4Path}\"";
    }

    private static string BuildFailureDetails(int exitCode, string stdOut, string stdErr)
    {
        var summary = new StringBuilder()
            .Append("ExitCode=")
            .Append(exitCode)
            .Append(".");

        if (!string.IsNullOrWhiteSpace(stdErr))
        {
            var errSnippet = stdErr.Length > 600 ? stdErr[^600..] : stdErr;
            summary.Append(" stderr=").Append(errSnippet.Replace('\r', ' ').Replace('\n', ' ').Trim());
        }
        else if (!string.IsNullOrWhiteSpace(stdOut))
        {
            var outSnippet = stdOut.Length > 600 ? stdOut[^600..] : stdOut;
            summary.Append(" stdout=").Append(outSnippet.Replace('\r', ' ').Replace('\n', ' ').Trim());
        }

        return summary.ToString();
    }
}
