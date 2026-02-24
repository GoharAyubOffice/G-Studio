using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using GStudio.Capture.Audio;
using GStudio.Capture.Recorder.FrameProviders;
using GStudio.Common.Configuration;
using GStudio.Common.Events;
using GStudio.Project.Store;
using FormsCursor = System.Windows.Forms.Cursor;

namespace GStudio.Capture.Recorder;

public sealed class DesktopSnapshotCaptureBackend : ICaptureBackend
{
    private static readonly TimeSpan InputSamplingInterval = TimeSpan.FromMilliseconds(8);

    private const int VkLeftMouseButton = 0x01;
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkAlt = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private static readonly (int VirtualKey, string Label)[] ShortcutCandidates =
    [
        (0x50, "P"),
        (0x4B, "K"),
        (0x53, "S"),
        (0x41, "A"),
        (0x42, "B"),
        (0x20, "Space"),
        (0x0D, "Enter")
    ];

    public async Task<CaptureRunResult> RunAsync(CaptureRunContext context, CancellationToken cancellationToken = default)
    {
        using var frameProvider = DesktopFrameProviderFactory.Create(context.Settings.Video, out var backendName, out var backendDetails);
        var bounds = frameProvider.CaptureBounds;

        var outputWidth = ResolveOutputSize(context.Settings.Video.Width, bounds.Width);
        var outputHeight = ResolveOutputSize(context.Settings.Video.Height, bounds.Height);

        var pointerScaleX = outputWidth / (double)Math.Max(1, bounds.Width);
        var pointerScaleY = outputHeight / (double)Math.Max(1, bounds.Height);

        var fps = Math.Max(1, context.Settings.Video.Fps);
        var frameInterval = TimeSpan.FromSeconds(1.0d / fps);

        await using var audioCapture = new WasapiAudioCaptureCoordinator(context.Session.Paths, context.Settings.Audio);
        audioCapture.Start();

        if (context.Settings.Video.CaptureFrames)
        {
            Directory.CreateDirectory(context.Session.Paths.CaptureFramesDirectory);
        }

        await using var frameTimelineWriter = context.Settings.Video.CaptureFrames
            ? new NdjsonStreamWriter<FrameTimestampEvent>(context.Session.Paths.CaptureFrameTimelinePath)
            : null;

        await context.EventLogWriter.WriteWindowAsync(
            new WindowEvent(
                T: 0.0d,
                Type: "captureBounds",
                X: bounds.Left,
                Y: bounds.Top,
                Width: outputWidth,
                Height: outputHeight,
                Dpi: 96.0d,
                Title: backendName,
                ColorSpace: "sRGB"),
            cancellationToken).ConfigureAwait(false);

        await context.EventLogWriter.WriteWindowAsync(
            new WindowEvent(
                T: 0.0d,
                Type: "captureBackend",
                X: bounds.Left,
                Y: bounds.Top,
                Width: outputWidth,
                Height: outputHeight,
                Dpi: 96.0d,
                Title: backendName,
                ColorSpace: backendDetails),
            cancellationToken).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        var frameTask = RunFrameLoopAsync(
            frameProvider,
            outputWidth,
            outputHeight,
            context.Session.Paths.CaptureFramesDirectory,
            context.Settings.Video.CaptureFrames,
            frameInterval,
            () => stopwatch.Elapsed.TotalSeconds,
            frameTimelineWriter,
            cancellationToken);

        var inputTask = RunInputLoopAsync(
            context,
            bounds,
            outputWidth,
            outputHeight,
            pointerScaleX,
            pointerScaleY,
            () => stopwatch.Elapsed.TotalSeconds,
            cancellationToken);

        var frameResult = FrameLoopResult.Empty;

        try
        {
            await Task.WhenAll(frameTask, inputTask).ConfigureAwait(false);
            frameResult = frameTask.Result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            frameResult = frameTask.IsCompletedSuccessfully ? frameTask.Result : FrameLoopResult.Empty;
        }
        finally
        {
            await audioCapture.StopAsync().ConfigureAwait(false);
        }

        var durationSeconds = stopwatch.Elapsed.TotalSeconds;
        var effectiveFps = durationSeconds > 0.0d
            ? frameResult.FrameCount / durationSeconds
            : 0.0d;
        var durationDriftMs = (durationSeconds - frameResult.TimingMetrics.TimelineDurationSeconds) * 1000.0d;

        return new CaptureRunResult(
            FrameCount: frameResult.FrameCount,
            DurationSeconds: durationSeconds,
            TargetFps: fps,
            EffectiveFps: effectiveFps,
            BackendName: backendName,
            BackendDetails: backendDetails,
            CaptureMissCount: frameResult.CaptureMissCount,
            ReusedFrameCount: frameResult.ReusedFrameCount,
            FrameTimelineCount: frameResult.TimingMetrics.FrameTimelineCount,
            TimelineDurationSeconds: frameResult.TimingMetrics.TimelineDurationSeconds,
            TimelineEffectiveFps: frameResult.TimingMetrics.TimelineEffectiveFps,
            AverageFrameIntervalMs: frameResult.TimingMetrics.AverageFrameIntervalMs,
            FrameIntervalJitterMs: frameResult.TimingMetrics.FrameIntervalJitterMs,
            MaxFrameIntervalMs: frameResult.TimingMetrics.MaxFrameIntervalMs,
            DurationDriftMs: durationDriftMs);
    }

    private static async Task<FrameLoopResult> RunFrameLoopAsync(
        IDesktopFrameProvider frameProvider,
        int outputWidth,
        int outputHeight,
        string frameDirectory,
        bool captureFrames,
        TimeSpan frameInterval,
        Func<double> timestampProvider,
        NdjsonStreamWriter<FrameTimestampEvent>? frameTimelineWriter,
        CancellationToken cancellationToken)
    {
        var frameCount = 0;
        using var timer = new PeriodicTimer(frameInterval);

        if (!captureFrames)
        {
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    frameCount++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }

            return new FrameLoopResult(
                FrameCount: frameCount,
                CaptureMissCount: 0,
                ReusedFrameCount: 0,
                TimingMetrics: FrameTimingMetrics.Empty);
        }

        var queue = Channel.CreateBounded<PendingFrame>(new BoundedChannelOptions(12)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var writerTask = RunFrameWriterLoopAsync(
            queue.Reader,
            outputWidth,
            outputHeight,
            frameDirectory,
            frameTimelineWriter);

        var captureMissCount = 0;
        FrameWriterResult writerResult;

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var time = timestampProvider();

                var capturedBitmap = TryCaptureBitmap(frameProvider);
                if (capturedBitmap is null)
                {
                    captureMissCount++;
                }

                await queue.Writer
                    .WriteAsync(new PendingFrame(frameCount, time, capturedBitmap), cancellationToken)
                    .ConfigureAwait(false);

                frameCount++;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            queue.Writer.TryComplete();
            writerResult = await writerTask.ConfigureAwait(false);
        }

        return new FrameLoopResult(
            FrameCount: frameCount,
            CaptureMissCount: captureMissCount,
            ReusedFrameCount: writerResult.ReusedFrameCount,
            TimingMetrics: writerResult.TimingMetrics);
    }

    private static async Task RunInputLoopAsync(
        CaptureRunContext context,
        Rectangle bounds,
        int outputWidth,
        int outputHeight,
        double pointerScaleX,
        double pointerScaleY,
        Func<double> timestampProvider,
        CancellationToken cancellationToken)
    {
        var pointerPosition = GetCursorScreenPoint();
        var lastPointerX = (pointerPosition.X - bounds.Left) * pointerScaleX;
        var lastPointerY = (pointerPosition.Y - bounds.Top) * pointerScaleY;
        var leftMouseDown = IsKeyDown(VkLeftMouseButton);

        var shortcutState = ShortcutCandidates.ToDictionary(static c => c.VirtualKey, static _ => false);
        using var timer = new PeriodicTimer(InputSamplingInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var t = timestampProvider();

                pointerPosition = GetCursorScreenPoint();
                var relativeX = (pointerPosition.X - bounds.Left) * pointerScaleX;
                var relativeY = (pointerPosition.Y - bounds.Top) * pointerScaleY;

                relativeX = Math.Clamp(relativeX, 0.0d, outputWidth - 1.0d);
                relativeY = Math.Clamp(relativeY, 0.0d, outputHeight - 1.0d);

                if (Math.Abs(relativeX - lastPointerX) > 0.5d || Math.Abs(relativeY - lastPointerY) > 0.5d)
                {
                    await context.EventLogWriter.WritePointerAsync(
                        PointerEvent.Move(t, relativeX, relativeY),
                        cancellationToken).ConfigureAwait(false);

                    lastPointerX = relativeX;
                    lastPointerY = relativeY;
                }

                var currentLeftMouseDown = IsKeyDown(VkLeftMouseButton);
                if (currentLeftMouseDown != leftMouseDown)
                {
                    var clickEvent = currentLeftMouseDown
                        ? PointerEvent.Down(t, relativeX, relativeY)
                        : PointerEvent.Up(t, relativeX, relativeY);

                    await context.EventLogWriter.WritePointerAsync(clickEvent, cancellationToken).ConfigureAwait(false);
                    leftMouseDown = currentLeftMouseDown;
                }

                await CaptureKeyboardAsync(context, t, shortcutState, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task CaptureKeyboardAsync(
        CaptureRunContext context,
        double timestamp,
        Dictionary<int, bool> shortcutState,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in ShortcutCandidates)
        {
            var isDown = IsKeyDown(candidate.VirtualKey);
            var wasDown = shortcutState[candidate.VirtualKey];
            shortcutState[candidate.VirtualKey] = isDown;

            if (!isDown || wasDown)
            {
                continue;
            }

            var modifiers = ActiveModifiers();
            if (modifiers.Count > 0)
            {
                await context.EventLogWriter.WriteKeyboardAsync(
                    KeyboardEvent.Shortcut(timestamp, modifiers, candidate.Label),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await context.EventLogWriter.WriteKeyboardAsync(
                    KeyboardEvent.KeyPress(timestamp, candidate.Label),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<FrameWriterResult> RunFrameWriterLoopAsync(
        ChannelReader<PendingFrame> reader,
        int outputWidth,
        int outputHeight,
        string frameDirectory,
        NdjsonStreamWriter<FrameTimestampEvent>? frameTimelineWriter)
    {
        string? previousFramePath = null;
        ulong? previousFrameSignature = null;
        var reusedFrameCount = 0;
        var timingAccumulator = new FrameTimingAccumulator();

        await foreach (var pending in reader.ReadAllAsync().ConfigureAwait(false))
        {
            var frameWriteResult = WriteFrame(
                pending.Bitmap,
                outputWidth,
                outputHeight,
                frameDirectory,
                pending.Index,
                ref previousFramePath,
                ref previousFrameSignature);

            if (frameWriteResult is FrameWriteResult.ReusedPreviousFrame)
            {
                reusedFrameCount++;
            }

            timingAccumulator.AddSample(pending.Time);

            if (frameTimelineWriter is not null)
            {
                await frameTimelineWriter
                    .WriteAsync(new FrameTimestampEvent(pending.Time, pending.Index), CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        return new FrameWriterResult(reusedFrameCount, timingAccumulator.Snapshot());
    }

    private static Bitmap? TryCaptureBitmap(IDesktopFrameProvider frameProvider)
    {
        Bitmap? capturedBitmap = null;

        try
        {
            var hasCapturedFrame = frameProvider.TryCaptureFrame(out capturedBitmap);
            if (hasCapturedFrame && capturedBitmap is not null)
            {
                return capturedBitmap;
            }

            capturedBitmap?.Dispose();
            return null;
        }
        catch
        {
            capturedBitmap?.Dispose();
            return null;
        }
    }

    private static FrameWriteResult WriteFrame(
        Bitmap? capturedBitmap,
        int outputWidth,
        int outputHeight,
        string frameDirectory,
        int frameIndex,
        ref string? previousFramePath,
        ref ulong? previousFrameSignature)
    {
        var framePath = Path.Combine(frameDirectory, $"frame_{frameIndex:D06}.png");

        if (capturedBitmap is null)
        {
            if (!string.IsNullOrWhiteSpace(previousFramePath) && File.Exists(previousFramePath))
            {
                File.Copy(previousFramePath, framePath, overwrite: true);
                previousFramePath = framePath;
                return FrameWriteResult.ReusedPreviousFrame;
            }

            using var blackFrame = new Bitmap(outputWidth, outputHeight, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(blackFrame))
            {
                graphics.Clear(Color.Black);
            }

            blackFrame.Save(framePath, ImageFormat.Png);
            previousFramePath = framePath;
            previousFrameSignature = 0UL;
            return FrameWriteResult.BlackFrame;
        }

        using (capturedBitmap)
        {
            var frameSignature = ComputeFrameSignature(capturedBitmap);
            if (
                previousFrameSignature.HasValue &&
                previousFrameSignature.Value == frameSignature &&
                !string.IsNullOrWhiteSpace(previousFramePath) &&
                File.Exists(previousFramePath))
            {
                File.Copy(previousFramePath, framePath, overwrite: true);
                previousFramePath = framePath;
                return FrameWriteResult.ReusedPreviousFrame;
            }

            if (outputWidth == capturedBitmap.Width && outputHeight == capturedBitmap.Height)
            {
                capturedBitmap.Save(framePath, ImageFormat.Png);
                previousFramePath = framePath;
                previousFrameSignature = frameSignature;
                return FrameWriteResult.CapturedFrame;
            }

            using var outputBitmap = new Bitmap(outputWidth, outputHeight, PixelFormat.Format32bppArgb);
            using var outputGraphics = Graphics.FromImage(outputBitmap);
            outputGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            outputGraphics.SmoothingMode = SmoothingMode.HighQuality;
            outputGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            outputGraphics.CompositingQuality = CompositingQuality.HighQuality;

            outputGraphics.DrawImage(
                capturedBitmap,
                new Rectangle(0, 0, outputWidth, outputHeight),
                new Rectangle(0, 0, capturedBitmap.Width, capturedBitmap.Height),
                GraphicsUnit.Pixel);

            outputBitmap.Save(framePath, ImageFormat.Png);
            previousFramePath = framePath;
            previousFrameSignature = frameSignature;
            return FrameWriteResult.CapturedFrame;
        }
    }

    private static unsafe ulong ComputeFrameSignature(Bitmap bitmap)
    {
        const ulong offsetBasis = 1469598103934665603UL;
        const ulong prime = 1099511628211UL;

        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var stepX = Math.Max(1, bitmap.Width / 36);
        var stepY = Math.Max(1, bitmap.Height / 20);

        BitmapData? data = null;
        try
        {
            data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var hash = offsetBasis;
            var basePtr = (byte*)data.Scan0;

            for (var y = 0; y < bitmap.Height; y += stepY)
            {
                var row = basePtr + (y * data.Stride);
                for (var x = 0; x < bitmap.Width; x += stepX)
                {
                    var pixelPtr = row + (x * 4);
                    var pixel = *(uint*)pixelPtr;
                    hash ^= pixel;
                    hash *= prime;
                }
            }

            hash ^= (ulong)bitmap.Width;
            hash *= prime;
            hash ^= (ulong)bitmap.Height;
            hash *= prime;

            return hash;
        }
        catch
        {
            return offsetBasis ^ (ulong)bitmap.Width ^ ((ulong)bitmap.Height << 24);
        }
        finally
        {
            if (data is not null)
            {
                bitmap.UnlockBits(data);
            }
        }
    }

    private sealed record PendingFrame(int Index, double Time, Bitmap? Bitmap);
    private sealed record FrameWriterResult(int ReusedFrameCount, FrameTimingMetrics TimingMetrics);
    private sealed record FrameLoopResult(int FrameCount, int CaptureMissCount, int ReusedFrameCount, FrameTimingMetrics TimingMetrics)
    {
        public static FrameLoopResult Empty { get; } = new(0, 0, 0, FrameTimingMetrics.Empty);
    }

    private sealed record FrameTimingMetrics(
        int FrameTimelineCount,
        double TimelineDurationSeconds,
        double TimelineEffectiveFps,
        double AverageFrameIntervalMs,
        double FrameIntervalJitterMs,
        double MaxFrameIntervalMs)
    {
        public static FrameTimingMetrics Empty { get; } = new(0, 0.0d, 0.0d, 0.0d, 0.0d, 0.0d);
    }

    private sealed class FrameTimingAccumulator
    {
        private bool _hasSample;
        private double _lastTimestamp;
        private int _sampleCount;
        private int _intervalCount;
        private double _intervalSum;
        private double _intervalSumSquares;
        private double _maxInterval;

        public void AddSample(double timestamp)
        {
            if (!double.IsFinite(timestamp) || timestamp < 0.0d)
            {
                return;
            }

            if (_hasSample)
            {
                var delta = timestamp - _lastTimestamp;
                if (delta < 0.0d)
                {
                    delta = 0.0d;
                }

                _intervalCount++;
                _intervalSum += delta;
                _intervalSumSquares += delta * delta;
                _maxInterval = Math.Max(_maxInterval, delta);
            }

            _lastTimestamp = timestamp;
            _sampleCount++;
            _hasSample = true;
        }

        public FrameTimingMetrics Snapshot()
        {
            if (!_hasSample || _sampleCount <= 0)
            {
                return FrameTimingMetrics.Empty;
            }

            var timelineDurationSeconds = _lastTimestamp;
            var timelineEffectiveFps = timelineDurationSeconds > 0.0d
                ? _sampleCount / timelineDurationSeconds
                : 0.0d;

            if (_intervalCount <= 0)
            {
                return new FrameTimingMetrics(
                    FrameTimelineCount: _sampleCount,
                    TimelineDurationSeconds: timelineDurationSeconds,
                    TimelineEffectiveFps: timelineEffectiveFps,
                    AverageFrameIntervalMs: 0.0d,
                    FrameIntervalJitterMs: 0.0d,
                    MaxFrameIntervalMs: 0.0d);
            }

            var meanIntervalSeconds = _intervalSum / _intervalCount;
            var meanSquareInterval = _intervalSumSquares / _intervalCount;
            var varianceSeconds = Math.Max(0.0d, meanSquareInterval - (meanIntervalSeconds * meanIntervalSeconds));

            return new FrameTimingMetrics(
                FrameTimelineCount: _sampleCount,
                TimelineDurationSeconds: timelineDurationSeconds,
                TimelineEffectiveFps: timelineEffectiveFps,
                AverageFrameIntervalMs: meanIntervalSeconds * 1000.0d,
                FrameIntervalJitterMs: Math.Sqrt(varianceSeconds) * 1000.0d,
                MaxFrameIntervalMs: _maxInterval * 1000.0d);
        }
    }

    private enum FrameWriteResult
    {
        CapturedFrame,
        ReusedPreviousFrame,
        BlackFrame
    }

    private static int ResolveOutputSize(int configuredSize, int fallbackSize)
    {
        return configuredSize > 0 ? configuredSize : Math.Max(1, fallbackSize);
    }

    private static List<string> ActiveModifiers()
    {
        var modifiers = new List<string>(4);
        if (IsKeyDown(VkControl))
        {
            modifiers.Add("CTRL");
        }

        if (IsKeyDown(VkShift))
        {
            modifiers.Add("SHIFT");
        }

        if (IsKeyDown(VkAlt))
        {
            modifiers.Add("ALT");
        }

        if (IsKeyDown(VkLWin) || IsKeyDown(VkRWin))
        {
            modifiers.Add("WIN");
        }

        return modifiers;
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static Point GetCursorScreenPoint()
    {
        if (GetCursorPos(out var point))
        {
            return new Point(point.X, point.Y);
        }

        return FormsCursor.Position;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public int X { get; init; }

        public int Y { get; init; }
    }
}
