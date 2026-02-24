using System.IO;
using System.Windows;
using GStudio.Capture.Recorder;
using GStudio.Common.Configuration;
using GStudio.Common.Project;
using GStudio.Export.Pipeline;
using GStudio.Project.Store;
using GStudio.Render.Preview;

namespace GStudio.App;

public partial class MainWindow : Window
{
    private readonly SessionSettings _sessionSettings;
    private readonly ProjectSessionStore _sessionStore;
    private readonly EventLogStore _eventLogStore;
    private readonly RecordingCoordinator _recordingCoordinator;
    private readonly DeterministicPreviewPlanner _previewPlanner;
    private readonly ExportPackageWriter _exportPackageWriter;

    private ProjectSession? _activeSession;
    private CaptureStats? _lastStats;
    private PreviewRenderPlan? _lastPreview;

    public MainWindow()
    {
        InitializeComponent();

        var defaultSettings = SessionSettings.CreateDefault();
        _sessionSettings = defaultSettings with
        {
            Video = defaultSettings.Video with
            {
                Width = 1920,
                Height = 1080,
                Fps = 30,
                CaptureFrames = true
            },
            Audio = defaultSettings.Audio with
            {
                CaptureMicrophone = false,
                CaptureSystemAudio = false
            }
        };

        var projectsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GStudio",
            "Projects");

        _sessionStore = new ProjectSessionStore(projectsRoot);
        _eventLogStore = new EventLogStore();
        _recordingCoordinator = new RecordingCoordinator(
            _sessionStore,
            _eventLogStore,
            new DesktopSnapshotCaptureBackend());

        _previewPlanner = new DeterministicPreviewPlanner();
        _exportPackageWriter = new ExportPackageWriter();

        UpdateUiState();
    }

    private async void StartRecording_Click(object sender, RoutedEventArgs e)
    {
        if (_recordingCoordinator.IsRecording)
        {
            return;
        }

        try
        {
            SetStatus("Starting capture session...");
            _lastPreview = null;
            FrameList.ItemsSource = null;
            ZoomSegmentList.ItemsSource = null;

            _activeSession = await _recordingCoordinator.StartAsync(_sessionSettings);

            SessionPathText.Text = _activeSession.Paths.RootDirectory;
            CaptureStatsText.Text = "Recording in progress";
            SetStatus("Recording desktop frames and interaction events.");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to start recording: {ex.Message}", isError: true);
        }
        finally
        {
            UpdateUiState();
        }
    }

    private async void StopRecording_Click(object sender, RoutedEventArgs e)
    {
        if (!_recordingCoordinator.IsRecording)
        {
            return;
        }

        try
        {
            SetStatus("Stopping capture session...");
            _lastStats = await _recordingCoordinator.StopAsync();

            _activeSession = _recordingCoordinator.ActiveSession;
            CaptureStatsText.Text =
                $"Duration {_lastStats.DurationSeconds:0.00}s | Frames {_lastStats.FrameCount} | Pointer {_lastStats.PointerEventCount} | Keyboard {_lastStats.KeyboardEventCount}";

            SetStatus("Recording stopped. Build cinematic preview next.");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to stop recording: {ex.Message}", isError: true);
        }
        finally
        {
            UpdateUiState();
        }
    }

    private async void BuildPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSession is null)
        {
            SetStatus("No session available. Record something first.", isError: true);
            return;
        }

        try
        {
            SetStatus("Building cinematic camera-follow plan...");

            var pointerEvents = await _eventLogStore.ReadPointerEventsAsync(_activeSession.Paths);
            if (pointerEvents.Count == 0)
            {
                SetStatus("No pointer events captured. Move/click the mouse while recording.", isError: true);
                return;
            }

            _lastPreview = _previewPlanner.Build(
                pointerEvents,
                _sessionSettings,
                durationSeconds: _lastStats?.DurationSeconds);

            ZoomSegmentList.ItemsSource = _lastPreview.ZoomSegments
                .Select((segment, index) =>
                    FormattableString.Invariant(
                        $"#{index + 1:00} [{segment.Start:0.000}s-{segment.End:0.000}s] center=({segment.Center.X:0.0},{segment.Center.Y:0.0}) scale={segment.Scale:0.00} triggers={segment.TriggerCount}"))
                .ToList();

            FrameList.ItemsSource = _lastPreview.Frames
                .Take(200)
                .Select(frame =>
                    FormattableString.Invariant(
                        $"{frame.FrameIndex,4} | t={frame.Time,7:0.000}s | cam=({frame.Camera.Center.X,7:0.0},{frame.Camera.Center.Y,7:0.0}) x{frame.Camera.Scale,4:0.00} | cursor=({frame.Cursor.Position.X,7:0.0},{frame.Cursor.Position.Y,7:0.0}) hidden={(frame.Cursor.Hidden ? 1 : 0)}"))
                .ToList();

            SetStatus($"Preview generated: {_lastPreview.ZoomSegments.Count} zoom segments, {_lastPreview.Frames.Count} frames.");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to build preview: {ex.Message}", isError: true);
        }
        finally
        {
            UpdateUiState();
        }
    }

    private async void CreateExportPackage_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSession is null || _lastPreview is null)
        {
            SetStatus("Build preview before creating export package.", isError: true);
            return;
        }

        try
        {
            SetStatus("Writing export package...");

            var outputRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GStudio",
                "Exports");

            var exportRequest = new ExportRequest(
                Session: _activeSession,
                PreviewPlan: _lastPreview,
                OutputDirectory: outputRoot,
                OutputName: $"session_{_activeSession.SessionId}");

            var exportResult = await _exportPackageWriter.WriteAsync(exportRequest);
            SetStatus(
                $"Export package ready: {exportResult.RenderedFrameCount} rendered frames at {exportResult.RenderedFramesDirectory}. Run {exportResult.EncodeScriptPath} to build MP4.");
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to create export package: {ex.Message}", isError: true);
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? System.Windows.Media.Brushes.IndianRed
            : System.Windows.Media.Brushes.Teal;
    }

    private void UpdateUiState()
    {
        var isRecording = _recordingCoordinator.IsRecording;

        StartButton.IsEnabled = !isRecording;
        StopButton.IsEnabled = isRecording;
        PreviewButton.IsEnabled = !isRecording && _activeSession is not null;
        ExportButton.IsEnabled = !isRecording && _activeSession is not null && _lastPreview is not null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _recordingCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnClosed(e);
    }
}
