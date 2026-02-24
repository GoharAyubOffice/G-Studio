using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GStudio.Capture.Audio;
using GStudio.Capture.Recorder.FrameProviders;
using GStudio.Common.Configuration;
using GStudio.Common.Events;
using FormsCursor = System.Windows.Forms.Cursor;

namespace GStudio.Capture.Recorder;

public sealed class DesktopSnapshotCaptureBackend : ICaptureBackend
{
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
        using var frameProvider = DesktopFrameProviderFactory.Create(context.Settings.Video, out var backendName);
        var bounds = frameProvider.CaptureBounds;

        var outputWidth = ResolveOutputSize(context.Settings.Video.Width, bounds.Width);
        var outputHeight = ResolveOutputSize(context.Settings.Video.Height, bounds.Height);

        var pointerScaleX = outputWidth / (double)Math.Max(1, bounds.Width);
        var pointerScaleY = outputHeight / (double)Math.Max(1, bounds.Height);

        var fps = Math.Max(1, context.Settings.Video.Fps);
        var frameInterval = TimeSpan.FromSeconds(1.0d / fps);
        var frameCount = 0;

        await using var audioCapture = new WasapiAudioCaptureCoordinator(context.Session.Paths, context.Settings.Audio);
        audioCapture.Start();

        if (context.Settings.Video.CaptureFrames)
        {
            Directory.CreateDirectory(context.Session.Paths.CaptureFramesDirectory);
        }

        string? previousFramePath = null;

        var pointerPosition = FormsCursor.Position;
        var lastPointerX = (pointerPosition.X - bounds.Left) * pointerScaleX;
        var lastPointerY = (pointerPosition.Y - bounds.Top) * pointerScaleY;
        var leftMouseDown = IsKeyDown(VkLeftMouseButton);

        var shortcutState = ShortcutCandidates.ToDictionary(static c => c.VirtualKey, static _ => false);

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

        var stopwatch = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(frameInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var t = stopwatch.Elapsed.TotalSeconds;

                if (context.Settings.Video.CaptureFrames)
                {
                    CaptureFrame(
                        frameProvider,
                        outputWidth,
                        outputHeight,
                        context.Session.Paths.CaptureFramesDirectory,
                        frameCount,
                        ref previousFramePath);
                }

                frameCount++;

                pointerPosition = FormsCursor.Position;
                var relativeX = (pointerPosition.X - bounds.Left) * pointerScaleX;
                var relativeY = (pointerPosition.Y - bounds.Top) * pointerScaleY;

                relativeX = Math.Clamp(relativeX, 0.0d, outputWidth - 1.0d);
                relativeY = Math.Clamp(relativeY, 0.0d, outputHeight - 1.0d);

                if (Math.Abs(relativeX - lastPointerX) > 0.1d || Math.Abs(relativeY - lastPointerY) > 0.1d)
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
        finally
        {
            await audioCapture.StopAsync().ConfigureAwait(false);
        }

        return new CaptureRunResult(
            FrameCount: frameCount,
            DurationSeconds: stopwatch.Elapsed.TotalSeconds);
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

    private static void CaptureFrame(
        IDesktopFrameProvider frameProvider,
        int outputWidth,
        int outputHeight,
        string frameDirectory,
        int frameIndex,
        ref string? previousFramePath)
    {
        var framePath = Path.Combine(frameDirectory, $"frame_{frameIndex:D06}.png");

        Bitmap? capturedBitmap = null;
        var hasCapturedFrame = false;

        try
        {
            hasCapturedFrame = frameProvider.TryCaptureFrame(out capturedBitmap);
        }
        catch
        {
            capturedBitmap?.Dispose();
            capturedBitmap = null;
            hasCapturedFrame = false;
        }

        if (!hasCapturedFrame || capturedBitmap is null)
        {
            if (!string.IsNullOrWhiteSpace(previousFramePath) && File.Exists(previousFramePath))
            {
                File.Copy(previousFramePath, framePath, overwrite: true);
                previousFramePath = framePath;
                return;
            }

            capturedBitmap = new Bitmap(frameProvider.CaptureBounds.Width, frameProvider.CaptureBounds.Height, PixelFormat.Format32bppArgb);
            using var fallbackGraphics = Graphics.FromImage(capturedBitmap);
            fallbackGraphics.Clear(Color.Black);
        }

        using var sourceBitmap = capturedBitmap;
        if (outputWidth == sourceBitmap.Width && outputHeight == sourceBitmap.Height)
        {
            sourceBitmap.Save(framePath, ImageFormat.Png);
            previousFramePath = framePath;
            return;
        }

        using var outputBitmap = new Bitmap(outputWidth, outputHeight, PixelFormat.Format32bppArgb);
        using var outputGraphics = Graphics.FromImage(outputBitmap);
        outputGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        outputGraphics.SmoothingMode = SmoothingMode.HighQuality;
        outputGraphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        outputGraphics.CompositingQuality = CompositingQuality.HighQuality;

        outputGraphics.DrawImage(
            sourceBitmap,
            new Rectangle(0, 0, outputWidth, outputHeight),
            new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height),
            GraphicsUnit.Pixel);

        outputBitmap.Save(framePath, ImageFormat.Png);
        previousFramePath = framePath;
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

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
