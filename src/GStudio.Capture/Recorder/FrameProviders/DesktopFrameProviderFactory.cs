using System.Drawing;
using GStudio.Common.Configuration;
using FormsScreen = System.Windows.Forms.Screen;

namespace GStudio.Capture.Recorder.FrameProviders;

internal static class DesktopFrameProviderFactory
{
    public static IDesktopFrameProvider Create(
        VideoCaptureSettings videoSettings,
        out string backendName,
        out string backendDetails)
    {
        if (videoSettings.Source == CaptureSourceKind.Region && videoSettings.Region is not null)
        {
            backendName = "gdi-region";
            backendDetails = "Region mode captures via GDI.";
            return new GdiFrameProvider(new Rectangle(
                videoSettings.Region.X,
                videoSettings.Region.Y,
                Math.Max(1, videoSettings.Region.Width),
                Math.Max(1, videoSettings.Region.Height)));
        }

        if (WgcFrameProvider.TryCreate(out var wgcProvider, out var wgcReason))
        {
            backendName = "wgc-primary";
            backendDetails = "Windows Graphics Capture active.";
            return wgcProvider!;
        }

        if (D3D11DesktopDuplicationFrameProvider.TryCreate(out var d3dProvider, out var duplicationReason))
        {
            backendName = "d3d11-duplication";
            backendDetails = "Desktop duplication backend active.";
            return d3dProvider!;
        }

        backendName = "gdi-fallback";
        backendDetails = $"WGC unavailable: {wgcReason ?? "unknown"}; duplication unavailable: {duplicationReason ?? "unknown"}";
        var primary = FormsScreen.PrimaryScreen;
        if (primary is not null)
        {
            return new GdiFrameProvider(primary.Bounds);
        }

        return new GdiFrameProvider(new Rectangle(0, 0, Math.Max(1, videoSettings.Width), Math.Max(1, videoSettings.Height)));
    }
}
