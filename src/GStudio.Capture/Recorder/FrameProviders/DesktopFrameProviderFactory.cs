using System.Drawing;
using GStudio.Common.Configuration;
using FormsScreen = System.Windows.Forms.Screen;

namespace GStudio.Capture.Recorder.FrameProviders;

internal static class DesktopFrameProviderFactory
{
    public static IDesktopFrameProvider Create(VideoCaptureSettings videoSettings, out string backendName)
    {
        if (videoSettings.Source == CaptureSourceKind.Region && videoSettings.Region is not null)
        {
            backendName = "gdi-region";
            return new GdiFrameProvider(new Rectangle(
                videoSettings.Region.X,
                videoSettings.Region.Y,
                Math.Max(1, videoSettings.Region.Width),
                Math.Max(1, videoSettings.Region.Height)));
        }

        if (D3D11DesktopDuplicationFrameProvider.TryCreate(out var d3dProvider, out _))
        {
            backendName = "d3d11-duplication";
            return d3dProvider!;
        }

        backendName = "gdi-fallback";
        var primary = FormsScreen.PrimaryScreen;
        if (primary is not null)
        {
            return new GdiFrameProvider(primary.Bounds);
        }

        return new GdiFrameProvider(new Rectangle(0, 0, Math.Max(1, videoSettings.Width), Math.Max(1, videoSettings.Height)));
    }
}
