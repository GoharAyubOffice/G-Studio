using System.Drawing;
using GStudio.Common.Configuration;
using GStudio.Capture.Recorder.FrameProviders;
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

        var gpuTier = DeviceCapabilityDetector.DetectGpuTier();
        backendName = "unknown";
        backendDetails = "Unknown capture backend.";

        string? duplicationReason = null;
        if (gpuTier >= GpuTier.Dedicated && D3D11DesktopDuplicationFrameProvider.TryCreate(out var d3dProvider, out duplicationReason))
        {
            backendName = "d3d11-duplication";
            backendDetails = $"Desktop duplication backend active. GPU tier: {gpuTier}";
            return d3dProvider!;
        }

        if (gpuTier >= GpuTier.Integrated && WgcFrameProvider.TryCreate(out var wgcProvider, out var wgcReason))
        {
            backendName = "wgc-primary";
            backendDetails = $"Windows Graphics Capture active. GPU tier: {gpuTier}. D3D11 failed: {duplicationReason ?? "unknown"}";
            return wgcProvider!;
        }

        System.Diagnostics.Debug.WriteLine($"[GStudio] D3D11 duplication FAILED: {duplicationReason}");
        Console.WriteLine($"[GStudio] D3D11 duplication FAILED: {duplicationReason}");

        if (D3D11DesktopDuplicationFrameProvider.TryCreate(out var d3dFallback, out var dupFallbackReason))
        {
            backendName = "d3d11-duplication";
            backendDetails = $"Desktop duplication (fallback). GPU tier: {gpuTier}";
            return d3dFallback!;
        }

        if (WgcFrameProvider.TryCreate(out var wgcFallback, out var wgcFallbackReason))
        {
            backendName = "wgc-primary";
            backendDetails = $"Windows Graphics Capture (fallback). GPU tier: {gpuTier}";
            return wgcFallback!;
        }

        backendName = "gdi-fallback";
        backendDetails = $"All fast backends failed. GPU tier: {gpuTier}. D3D11: {duplicationReason ?? "unknown"}. WGC: {wgcFallbackReason ?? "unknown"}";
        var primary = FormsScreen.PrimaryScreen;
        if (primary is not null)
        {
            return new GdiFrameProvider(primary.Bounds);
        }

        return new GdiFrameProvider(new Rectangle(0, 0, Math.Max(1, videoSettings.Width), Math.Max(1, videoSettings.Height)));
    }
}
