using System.Drawing;

namespace GStudio.Capture.Recorder.FrameProviders;

internal interface IDesktopFrameProvider : IDisposable
{
    Rectangle CaptureBounds { get; }

    bool TryCaptureFrame(out Bitmap? bitmap);
}
