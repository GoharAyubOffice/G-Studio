using System.Globalization;

namespace GStudio.Export.Pipeline;

internal static class FfmpegCommandBuilder
{
    public static string BuildRuntimeArguments(VideoEncodeRequest request)
    {
        return BuildArguments(
            frameInputExpression: Quote(request.FrameInputPattern),
            outputExpression: Quote(request.OutputMp4Path),
            request);
    }

    public static string BuildScriptCommand(VideoEncodeRequest request)
    {
        return "ffmpeg " + BuildArguments(
            frameInputExpression: "\"%FRAME_PATTERN%\"",
            outputExpression: "\"%OUTPUT_FILE%\"",
            request);
    }

    private static string BuildArguments(
        string frameInputExpression,
        string outputExpression,
        VideoEncodeRequest request)
    {
        var safeFps = Math.Max(1, request.Fps);
        var targetDuration = request.TargetDurationSeconds.GetValueOrDefault(0.0d);
        var hasTargetDuration = targetDuration > 0.01d;
        var durationText = targetDuration.ToString("0.###", CultureInfo.InvariantCulture);
        var durationOption = hasTargetDuration ? $" -t {durationText}" : string.Empty;

        var hasMic = !string.IsNullOrWhiteSpace(request.MicrophoneAudioPath) && File.Exists(request.MicrophoneAudioPath);
        var hasSystem = !string.IsNullOrWhiteSpace(request.SystemAudioPath) && File.Exists(request.SystemAudioPath);

        if (hasMic && hasSystem)
        {
            var micFilter = BuildTrackFilter(request.MicrophoneAudioPath!, targetDuration, hasTargetDuration);
            var systemFilter = BuildTrackFilter(request.SystemAudioPath!, targetDuration, hasTargetDuration);

            var mixFilter =
                $"[1:a]{micFilter}[a1];[2:a]{systemFilter}[a2];[a1][a2]amix=inputs=2:duration=longest" +
                (hasTargetDuration ? $",atrim=0:{durationText}" : string.Empty) +
                "[aout]";

            return
                $"-y -framerate {safeFps} -i {frameInputExpression} -i {Quote(request.MicrophoneAudioPath!)} -i {Quote(request.SystemAudioPath!)} " +
                $"-filter_complex \"{mixFilter}\" -map 0:v:0 -map \"[aout]\" " +
                $"-c:v libx264 -pix_fmt yuv420p -c:a aac -movflags +faststart{durationOption} {outputExpression}";
        }

        if (hasMic || hasSystem)
        {
            var audioPath = hasMic ? request.MicrophoneAudioPath! : request.SystemAudioPath!;
            var audioFilter = BuildTrackFilter(audioPath, targetDuration, hasTargetDuration);

            return
                $"-y -framerate {safeFps} -i {frameInputExpression} -i {Quote(audioPath)} -map 0:v:0 -map 1:a:0 " +
                $"-c:v libx264 -pix_fmt yuv420p -filter:a \"{audioFilter}\" -c:a aac -movflags +faststart{durationOption} {outputExpression}";
        }

        return
            $"-y -framerate {safeFps} -i {frameInputExpression} -c:v libx264 -pix_fmt yuv420p -movflags +faststart{durationOption} {outputExpression}";
    }

    private static string BuildTrackFilter(string audioPath, double targetDuration, bool hasTargetDuration)
    {
        var filters = new List<string> { "aresample=async=1:first_pts=0" };

        if (hasTargetDuration && TryReadWaveDuration(audioPath, out var sourceDuration) && sourceDuration > 0.01d)
        {
            var tempoRatio = sourceDuration / targetDuration;
            if (Math.Abs(tempoRatio - 1.0d) >= 0.01d)
            {
                foreach (var segment in BuildAtempoChain(tempoRatio))
                {
                    filters.Add($"atempo={segment.ToString("0.######", CultureInfo.InvariantCulture)}");
                }
            }
        }

        if (hasTargetDuration)
        {
            var durationText = targetDuration.ToString("0.###", CultureInfo.InvariantCulture);
            filters.Add("apad");
            filters.Add($"atrim=0:{durationText}");
        }

        return string.Join(',', filters);
    }

    private static IReadOnlyList<double> BuildAtempoChain(double ratio)
    {
        var safeRatio = double.IsFinite(ratio) && ratio > 0.01d ? ratio : 1.0d;
        var values = new List<double>();

        while (safeRatio > 2.0d)
        {
            values.Add(2.0d);
            safeRatio /= 2.0d;
        }

        while (safeRatio < 0.5d)
        {
            values.Add(0.5d);
            safeRatio /= 0.5d;
        }

        values.Add(safeRatio);
        return values;
    }

    private static bool TryReadWaveDuration(string path, out double durationSeconds)
    {
        durationSeconds = 0.0d;

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            if (new string(reader.ReadChars(4)) != "RIFF")
            {
                return false;
            }

            _ = reader.ReadInt32();
            if (new string(reader.ReadChars(4)) != "WAVE")
            {
                return false;
            }

            var byteRate = 0;
            var dataSize = 0;

            while (reader.BaseStream.Position <= reader.BaseStream.Length - 8)
            {
                var chunkId = new string(reader.ReadChars(4));
                var chunkSize = reader.ReadInt32();

                if (chunkSize < 0)
                {
                    return false;
                }

                if (chunkId == "fmt ")
                {
                    _ = reader.ReadInt16();
                    _ = reader.ReadInt16();
                    _ = reader.ReadInt32();
                    byteRate = reader.ReadInt32();

                    if (chunkSize > 12)
                    {
                        reader.BaseStream.Seek(chunkSize - 12, SeekOrigin.Current);
                    }
                }
                else if (chunkId == "data")
                {
                    dataSize = chunkSize;
                    reader.BaseStream.Seek(chunkSize, SeekOrigin.Current);
                }
                else
                {
                    reader.BaseStream.Seek(chunkSize, SeekOrigin.Current);
                }

                if ((chunkSize & 1) == 1)
                {
                    reader.BaseStream.Seek(1, SeekOrigin.Current);
                }
            }

            if (byteRate <= 0 || dataSize <= 0)
            {
                return false;
            }

            durationSeconds = dataSize / (double)byteRate;
            return durationSeconds > 0.0d;
        }
        catch
        {
            return false;
        }
    }

    private static string Quote(string value) => $"\"{value}\"";
}
