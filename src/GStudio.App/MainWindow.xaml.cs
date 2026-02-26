using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GStudio.Capture.Recorder;
using GStudio.Common.Configuration;
using GStudio.Common.Project;
using GStudio.Export.Pipeline;
using GStudio.Project.Store;
using GStudio.Render.Preview;

namespace GStudio.App;

public partial class MainWindow : Window
{
    private const int WmHotKey = 0x0312;
    private const int StopHotKeyId = 0x5A10;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkR = 0x52;

    private readonly SessionSettings _sessionSettings;
    private readonly ProjectSessionStore _sessionStore;
    private readonly EventLogStore _eventLogStore;
    private readonly RecordingCoordinator _recordingCoordinator;
    private readonly DeterministicPreviewPlanner _previewPlanner;
    private readonly ExportPackageWriter _exportPackageWriter;

    private static readonly EncoderModeOption[] EncoderModeOptions =
    [
        new(VideoEncoderMode.Adaptive, "Adaptive (native + ffmpeg fallback)"),
        new(VideoEncoderMode.NativeOnly, "Native Media Foundation only"),
        new(VideoEncoderMode.FfmpegOnly, "FFmpeg only")
    ];

    private ProjectSession? _activeSession;
    private CaptureStats? _lastStats;
    private PreviewRenderPlan? _lastPreview;
    private HwndSource? _windowSource;
    private bool _isStopHotKeyRegistered;
    private bool _isStoppingRecording;

    public MainWindow()
    {
        InitializeComponent();

        var defaultSettings = SessionSettings.CreateAdaptive();
        _sessionSettings = defaultSettings with
        {
            Video = defaultSettings.Video with
            {
                Width = 1920,
                Height = 1080,
                CaptureFrames = true
            },
            Audio = defaultSettings.Audio with
            {
                CaptureMicrophone = true,
                CaptureSystemAudio = true
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

        InitializeEncoderModeOptions();

        SourceInitialized += MainWindow_SourceInitialized;

        UpdateUiState();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = (HwndSource)PresentationSource.FromVisual(this)!;
        _windowSource.AddHook(WindowMessageHook);
        RegisterStopHotKey(_windowSource.Handle);
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
            CaptureStatsText.Text = "Recording in progress (Ctrl+Shift+R to stop)";

            if (_isStopHotKeyRegistered)
            {
                SetStatus("Recording started. Press Ctrl+Shift+R to stop; app window is hidden while recording.");
                HideForRecording();
            }
            else
            {
                SetStatus("Recording started. Stop hotkey unavailable, app remains visible and may appear in capture.");
            }
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
        await StopRecordingCoreAsync(restoreWindowAfterStop: false);
    }

    private async Task StopRecordingCoreAsync(bool restoreWindowAfterStop)
    {
        if (!_recordingCoordinator.IsRecording || _isStoppingRecording)
        {
            return;
        }

        _isStoppingRecording = true;

        try
        {
            SetStatus("Stopping capture session...");
            _lastStats = await _recordingCoordinator.StopAsync();

            if (restoreWindowAfterStop)
            {
                RestoreAfterRecording();
            }

            _activeSession = _recordingCoordinator.ActiveSession;
            CaptureStatsText.Text =
                $"Duration {_lastStats.DurationSeconds:0.00}s | Frames {_lastStats.FrameCount} | FPS {_lastStats.EffectiveFps:0.0}/{_lastStats.TargetFps} | TlFPS {_lastStats.TimelineEffectiveFps:0.0} | Jitter {_lastStats.FrameIntervalJitterMs:0.0}ms | Drift {_lastStats.DurationDriftMs:+0.0;-0.0;0.0}ms | Backend {_lastStats.CaptureBackend} | Reused {_lastStats.ReusedFrameCount} | Missed {_lastStats.CaptureMissCount} | Pointer {_lastStats.PointerEventCount} | Keyboard {_lastStats.KeyboardEventCount}";

            SetStatus(BuildStopStatusMessage(_lastStats));
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to stop recording: {ex.Message}", isError: true);
        }
        finally
        {
            _isStoppingRecording = false;
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
            SetStatus("Writing export package and encoding MP4...");

            var outputRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GStudio",
                "Exports");

            var exportRequest = new ExportRequest(
                Session: _activeSession,
                PreviewPlan: _lastPreview,
                OutputDirectory: outputRoot,
                OutputName: $"session_{_activeSession.SessionId}",
                EncodeVideo: true,
                EncoderMode: SelectedEncoderMode());

            var exportResult = await _exportPackageWriter.WriteAsync(exportRequest);
            SetStatus(exportResult.VideoEncoded
                ? $"Export complete: {exportResult.OutputMp4Path}"
                : $"Export package ready: {exportResult.RenderedFrameCount} rendered frames at {exportResult.RenderedFramesDirectory}. Run {exportResult.EncodeScriptPath} to build MP4.");
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

    private void InitializeEncoderModeOptions()
    {
        EncoderModeCombo.ItemsSource = EncoderModeOptions;
        EncoderModeCombo.SelectedIndex = 0;
    }

    private VideoEncoderMode SelectedEncoderMode()
    {
        return EncoderModeCombo.SelectedItem is EncoderModeOption option
            ? option.Mode
            : VideoEncoderMode.Adaptive;
    }

    private static string BuildStopStatusMessage(CaptureStats stats)
    {
        var degradedFps = stats.TargetFps > 0 && stats.EffectiveFps < (stats.TargetFps * 0.8d);
        var usingFallbackBackend = stats.CaptureBackend.Contains("fallback", StringComparison.OrdinalIgnoreCase);
        var driftedTimeline = Math.Abs(stats.DurationDriftMs) > 280.0d;

        if (degradedFps && usingFallbackBackend)
        {
            return $"Recording stopped with degraded capture ({stats.EffectiveFps:0.0}/{stats.TargetFps} fps on {stats.CaptureBackend}). Build preview next.";
        }

        if (degradedFps)
        {
            return $"Recording stopped with low effective fps ({stats.EffectiveFps:0.0}/{stats.TargetFps}). Build preview next.";
        }

        if (usingFallbackBackend)
        {
            return $"Recording stopped on fallback backend ({stats.CaptureBackend}). Build preview next.";
        }

        if (driftedTimeline)
        {
            return $"Recording stopped with timing drift ({stats.DurationDriftMs:+0.0;-0.0;0.0} ms). Build preview next.";
        }

        return "Recording stopped. Build cinematic preview next.";
    }

    private void UpdateUiState()
    {
        var isRecording = _recordingCoordinator.IsRecording;

        StartButton.IsEnabled = !isRecording;
        StopButton.IsEnabled = isRecording;
        PreviewButton.IsEnabled = !isRecording && _activeSession is not null;
        ExportButton.IsEnabled = !isRecording && _activeSession is not null && _lastPreview is not null;
    }

    private void HideForRecording()
    {
        WindowState = WindowState.Minimized;
        Hide();
    }

    private void RestoreAfterRecording()
    {
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotKey && wParam.ToInt32() == StopHotKeyId)
        {
            handled = true;
            _ = StopRecordingCoreAsync(restoreWindowAfterStop: true);
        }

        return IntPtr.Zero;
    }

    private void RegisterStopHotKey(IntPtr hwnd)
    {
        _isStopHotKeyRegistered = RegisterHotKey(hwnd, StopHotKeyId, ModControl | ModShift | ModNoRepeat, VkR);
    }

    private void UnregisterStopHotKey(IntPtr hwnd)
    {
        if (!_isStopHotKeyRegistered)
        {
            return;
        }

        UnregisterHotKey(hwnd, StopHotKeyId);
        _isStopHotKeyRegistered = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_windowSource is not null)
        {
            UnregisterStopHotKey(_windowSource.Handle);
            _windowSource.RemoveHook(WindowMessageHook);
        }

        _recordingCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnClosed(e);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private sealed record EncoderModeOption(VideoEncoderMode Mode, string Label)
    {
        public override string ToString() => Label;
    }
}
