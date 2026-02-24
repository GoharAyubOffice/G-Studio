namespace GStudio.Capture.Recorder;

public interface ICaptureBackend
{
    Task<CaptureRunResult> RunAsync(CaptureRunContext context, CancellationToken cancellationToken = default);
}
