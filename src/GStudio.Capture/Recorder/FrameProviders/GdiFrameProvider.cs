using System.Drawing;
using System.Drawing.Imaging;

namespace GStudio.Capture.Recorder.FrameProviders;

internal sealed class GdiFrameProvider : IDesktopFrameProvider
{
    public GdiFrameProvider(Rectangle bounds)
    {
        CaptureBounds = bounds;
    }

    public Rectangle CaptureBounds { get; }

    public bool TryCaptureFrame(out Bitmap? bitmap)
    {
        bitmap = new Bitmap(CaptureBounds.Width, CaptureBounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(CaptureBounds.Left, CaptureBounds.Top, 0, 0, CaptureBounds.Size, CopyPixelOperation.SourceCopy);
        return true;
    }

    public void Dispose()
    {
    }
}
