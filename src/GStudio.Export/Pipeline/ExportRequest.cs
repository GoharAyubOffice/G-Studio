using GStudio.Project.Store;
using GStudio.Render.Preview;

namespace GStudio.Export.Pipeline;

public sealed record ExportRequest(
    ProjectSession Session,
    PreviewRenderPlan PreviewPlan,
    string OutputDirectory,
    string OutputName);

public sealed record ExportPackageResult(
    string PackageDirectory,
    string PlanFilePath,
    string EncodeScriptPath);
