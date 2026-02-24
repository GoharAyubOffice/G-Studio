using GStudio.Common.Configuration;
using GStudio.Project.Store;

namespace GStudio.Capture.Recorder;

public sealed record CaptureRunContext(
    ProjectSession Session,
    SessionSettings Settings,
    EventLogWriter EventLogWriter);
