using GStudio.Common.Project;

namespace GStudio.Project.Store;

public sealed record ProjectSession(
    string SessionId,
    ProjectPaths Paths,
    ProjectManifest Manifest);
