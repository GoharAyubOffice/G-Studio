using GStudio.Project.Store;
using GStudio.Render.Preview;

namespace GStudio.Export.Pipeline;

public enum VideoEncoderMode
{
    Adaptive,
    NativeOnly,
    FfmpegOnly
}

public sealed record ExportRequest(
    ProjectSession Session,
    PreviewRenderPlan PreviewPlan,
    string OutputDirectory,
    string OutputName,
    bool EncodeVideo = true,
    VideoEncoderMode EncoderMode = VideoEncoderMode.Adaptive);

public sealed record ExportPackageResult(
    string PackageDirectory,
    string PlanFilePath,
    string EncodeScriptPath,
    string RenderedFramesDirectory,
    int RenderedFrameCount,
    string OutputMp4Path,
    bool VideoEncoded);
