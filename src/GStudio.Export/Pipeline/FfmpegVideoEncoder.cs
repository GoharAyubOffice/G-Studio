using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace GStudio.Export.Pipeline;

public sealed class FfmpegVideoEncoder : IVideoEncoder
{
    public async Task EncodeAsync(
        string frameInputPattern,
        string outputMp4Path,
        int fps,
        CancellationToken cancellationToken = default)
    {
        var safeFps = Math.Max(1, fps);
        Directory.CreateDirectory(Path.GetDirectoryName(outputMp4Path) ?? ".");

        var args =
            $"-y -framerate {safeFps} -i \"{frameInputPattern}\" -c:v libx264 -pix_fmt yuv420p -movflags +faststart \"{outputMp4Path}\"";

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

            if (process.ExitCode != 0 || !File.Exists(outputMp4Path))
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
