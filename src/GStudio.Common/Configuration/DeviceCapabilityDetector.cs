using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace GStudio.Common.Configuration;

public static class DeviceCapabilityDetector
{
    private static GpuTier? _cachedTier;

    public static GpuTier DetectGpuTier()
    {
        if (_cachedTier.HasValue)
        {
            return _cachedTier.Value;
        }

        try
        {
            var featureLevels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 };
            var result = D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out ID3D11Device device,
                out ID3D11DeviceContext _);

            if (result.Failure || device is null)
            {
                _cachedTier = GpuTier.Software;
                return _cachedTier.Value;
            }

            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            var description = adapter.Description;

            var vendorId = description.VendorId;
            var gpuName = description.Description.ToString().ToLowerInvariant();

            if (vendorId == 0x10DE || gpuName.Contains("nvidia") || gpuName.Contains("geforce") || gpuName.Contains("rtx") || gpuName.Contains("gtx"))
            {
                _cachedTier = GpuTier.Dedicated;
                return _cachedTier.Value;
            }

            if (vendorId == 0x1002 || gpuName.Contains("amd") || gpuName.Contains("radeon") || gpuName.Contains("rx "))
            {
                _cachedTier = GpuTier.Dedicated;
                return _cachedTier.Value;
            }

            if (vendorId == 0x8086 || gpuName.Contains("intel") || gpuName.Contains("uhd") || gpuName.Contains("iris"))
            {
                _cachedTier = GpuTier.Integrated;
                return _cachedTier.Value;
            }

            _cachedTier = GpuTier.Unknown;
            return _cachedTier.Value;
        }
        catch
        {
            _cachedTier = GpuTier.Unknown;
            return _cachedTier.Value;
        }
    }

    public static string GetGpuName()
    {
        try
        {
            var featureLevels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 };
            var result = D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out ID3D11Device device,
                out ID3D11DeviceContext _);

            if (result.Failure || device is null)
            {
                return "Unknown";
            }

            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            return adapter.Description.Description.ToString();
        }
        catch
        {
            return "Unknown";
        }
    }
}
