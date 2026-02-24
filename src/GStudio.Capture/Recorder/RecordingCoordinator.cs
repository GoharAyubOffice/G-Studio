using GStudio.Common.Configuration;
using GStudio.Common.Project;
using GStudio.Project.Store;

namespace GStudio.Capture.Recorder;

public sealed class RecordingCoordinator : IAsyncDisposable
{
    private readonly ProjectSessionStore _sessionStore;
    private readonly EventLogStore _eventLogStore;
    private readonly ICaptureBackend _captureBackend;

    private CancellationTokenSource? _captureCts;
    private Task<CaptureRunResult>? _captureTask;
    private EventLogWriter? _eventLogWriter;

    public RecordingCoordinator(
        ProjectSessionStore sessionStore,
        EventLogStore eventLogStore,
        ICaptureBackend captureBackend)
    {
        _sessionStore = sessionStore;
        _eventLogStore = eventLogStore;
        _captureBackend = captureBackend;
    }

    public bool IsRecording => _captureTask is not null;

    public ProjectSession? ActiveSession { get; private set; }

    public async Task<ProjectSession> StartAsync(SessionSettings settings, CancellationToken cancellationToken = default)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("Recording is already running.");
        }

        var session = await _sessionStore.CreateSessionAsync(settings, cancellationToken).ConfigureAwait(false);
        var writer = _eventLogStore.OpenWriter(session.Paths);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        ActiveSession = session;
        _eventLogWriter = writer;
        _captureCts = linkedCts;

        var context = new CaptureRunContext(session, settings, writer);
        _captureTask = _captureBackend.RunAsync(context, linkedCts.Token);

        return session;
    }

    public async Task<CaptureStats> StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRecording || ActiveSession is null || _eventLogWriter is null || _captureCts is null || _captureTask is null)
        {
            throw new InvalidOperationException("Recording is not running.");
        }

        _captureCts.Cancel();

        CaptureRunResult runResult;
        try
        {
            runResult = await _captureTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            runResult = CaptureRunResult.Empty;
        }

        await _eventLogWriter.DisposeAsync().ConfigureAwait(false);

        var stats = new CaptureStats
        {
            DurationSeconds = runResult.DurationSeconds,
            FrameCount = runResult.FrameCount,
            PointerEventCount = _eventLogWriter.PointerCount,
            KeyboardEventCount = _eventLogWriter.KeyboardCount,
            WindowEventCount = _eventLogWriter.WindowCount
        };

        await _sessionStore.CompleteSessionAsync(ActiveSession, stats, cancellationToken).ConfigureAwait(false);

        _captureCts.Dispose();
        _captureCts = null;
        _captureTask = null;
        _eventLogWriter = null;

        return stats;
    }

    public async ValueTask DisposeAsync()
    {
        if (!IsRecording)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
    }
}
