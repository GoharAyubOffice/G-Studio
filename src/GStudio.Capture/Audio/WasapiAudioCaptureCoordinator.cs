using GStudio.Common.Configuration;
using GStudio.Project.Store;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GStudio.Capture.Audio;

public sealed class WasapiAudioCaptureCoordinator : IAsyncDisposable
{
    private readonly AudioCaptureSettings _settings;
    private readonly ProjectPaths _paths;
    private readonly object _writerGate = new();

    private WasapiCapture? _microphoneCapture;
    private WasapiLoopbackCapture? _systemCapture;
    private WaveFileWriter? _microphoneWriter;
    private WaveFileWriter? _systemWriter;

    public WasapiAudioCaptureCoordinator(ProjectPaths paths, AudioCaptureSettings settings)
    {
        _paths = paths;
        _settings = settings;
    }

    public bool IsRecordingMicrophone { get; private set; }

    public bool IsRecordingSystemAudio { get; private set; }

    public IReadOnlyList<string> Warnings => _warnings;

    private readonly List<string> _warnings = [];

    public void Start()
    {
        if (!_settings.CaptureMicrophone && !_settings.CaptureSystemAudio)
        {
            return;
        }

        Directory.CreateDirectory(_paths.CaptureAudioDirectory);

        if (_settings.CaptureMicrophone)
        {
            TryStartMicrophoneCapture();
        }

        if (_settings.CaptureSystemAudio)
        {
            TryStartSystemCapture();
        }
    }

    public Task StopAsync()
    {
        StopCapture(_microphoneCapture, ref _microphoneCapture, ref _microphoneWriter);
        StopCapture(_systemCapture, ref _systemCapture, ref _systemWriter);

        IsRecordingMicrophone = false;
        IsRecordingSystemAudio = false;

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private void TryStartMicrophoneCapture()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            var capture = new WasapiCapture(device)
            {
                ShareMode = AudioClientShareMode.Shared
            };

            var writer = new WaveFileWriter(_paths.MicrophoneAudioPath, capture.WaveFormat);
            capture.DataAvailable += (_, args) => WriteAudio(_microphoneWriter, args.Buffer, args.BytesRecorded);
            capture.RecordingStopped += (_, args) =>
            {
                if (args.Exception is not null)
                {
                    _warnings.Add($"Microphone capture stopped unexpectedly: {args.Exception.Message}");
                }
            };

            _microphoneCapture = capture;
            _microphoneWriter = writer;

            capture.StartRecording();
            IsRecordingMicrophone = true;
        }
        catch (Exception ex)
        {
            _warnings.Add($"Microphone capture unavailable: {ex.Message}");
            StopCapture(_microphoneCapture, ref _microphoneCapture, ref _microphoneWriter);
            IsRecordingMicrophone = false;
        }
    }

    private void TryStartSystemCapture()
    {
        try
        {
            var capture = new WasapiLoopbackCapture();
            var writer = new WaveFileWriter(_paths.SystemAudioPath, capture.WaveFormat);

            capture.DataAvailable += (_, args) => WriteAudio(_systemWriter, args.Buffer, args.BytesRecorded);
            capture.RecordingStopped += (_, args) =>
            {
                if (args.Exception is not null)
                {
                    _warnings.Add($"System audio capture stopped unexpectedly: {args.Exception.Message}");
                }
            };

            _systemCapture = capture;
            _systemWriter = writer;

            capture.StartRecording();
            IsRecordingSystemAudio = true;
        }
        catch (Exception ex)
        {
            _warnings.Add($"System audio capture unavailable: {ex.Message}");
            StopCapture(_systemCapture, ref _systemCapture, ref _systemWriter);
            IsRecordingSystemAudio = false;
        }
    }

    private void WriteAudio(WaveFileWriter? writer, byte[] buffer, int bytesRecorded)
    {
        if (writer is null || bytesRecorded <= 0)
        {
            return;
        }

        lock (_writerGate)
        {
            writer.Write(buffer, 0, bytesRecorded);
        }
    }

    private static void StopCapture(
        WasapiCapture? capture,
        ref WasapiCapture? captureSlot,
        ref WaveFileWriter? writerSlot)
    {
        if (capture is null)
        {
            return;
        }

        try
        {
            capture.StopRecording();
        }
        catch
        {
        }

        capture.Dispose();
        writerSlot?.Dispose();

        writerSlot = null;
        captureSlot = null;
    }

    private static void StopCapture(
        WasapiLoopbackCapture? capture,
        ref WasapiLoopbackCapture? captureSlot,
        ref WaveFileWriter? writerSlot)
    {
        if (capture is null)
        {
            return;
        }

        try
        {
            capture.StopRecording();
        }
        catch
        {
        }

        capture.Dispose();
        writerSlot?.Dispose();

        writerSlot = null;
        captureSlot = null;
    }
}
