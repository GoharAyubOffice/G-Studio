using System.Text;
using System.Text.Json;
using GStudio.Render.Preview;

namespace GStudio.Export.Pipeline;

public sealed class ExportPackageWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<ExportPackageResult> WriteAsync(ExportRequest request, CancellationToken cancellationToken = default)
    {
        var safeName = SanitizeName(request.OutputName);
        var packageDirectory = Path.Combine(request.OutputDirectory, safeName);
        Directory.CreateDirectory(packageDirectory);

        var planFilePath = Path.Combine(packageDirectory, "render_plan.json");
        var encodeScriptPath = Path.Combine(packageDirectory, "encode_with_ffmpeg.cmd");

        await WritePlanFileAsync(planFilePath, request.PreviewPlan, cancellationToken).ConfigureAwait(false);
        await WriteEncodeScriptAsync(encodeScriptPath, request, cancellationToken).ConfigureAwait(false);

        return new ExportPackageResult(
            PackageDirectory: packageDirectory,
            PlanFilePath: planFilePath,
            EncodeScriptPath: encodeScriptPath);
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
        ExportRequest request,
        CancellationToken cancellationToken)
    {
        var frameInputPattern = Path.Combine(request.Session.Paths.CaptureFramesDirectory, "frame_%06d.png");
        var outputMp4Path = Path.Combine(request.OutputDirectory, SanitizeName(request.OutputName) + ".mp4");
        var fps = Math.Max(1, request.PreviewPlan.Fps);

        var script = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine("setlocal")
            .AppendLine($"set FRAME_PATTERN=\"{frameInputPattern}\"")
            .AppendLine($"set OUTPUT_FILE=\"{outputMp4Path}\"")
            .AppendLine($"ffmpeg -y -framerate {fps} -i %FRAME_PATTERN% -c:v libx264 -pix_fmt yuv420p -movflags +faststart %OUTPUT_FILE%")
            .AppendLine("if errorlevel 1 (")
            .AppendLine("  echo FFmpeg encode failed.")
            .AppendLine("  exit /b 1")
            .AppendLine(")")
            .AppendLine("echo Export complete: %OUTPUT_FILE%")
            .AppendLine("endlocal")
            .ToString();

        await File.WriteAllTextAsync(scriptPath, script, cancellationToken).ConfigureAwait(false);
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
