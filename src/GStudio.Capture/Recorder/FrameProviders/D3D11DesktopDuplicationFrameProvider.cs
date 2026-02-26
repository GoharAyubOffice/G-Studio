using System.Drawing;
using System.Drawing.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using FormsScreen = System.Windows.Forms.Screen;

namespace GStudio.Capture.Recorder.FrameProviders;

internal sealed class D3D11DesktopDuplicationFrameProvider : IDesktopFrameProvider
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutputDuplication _duplication;
    private readonly int _width;
    private readonly int _height;

    private ID3D11Texture2D? _stagingTexture;

    private D3D11DesktopDuplicationFrameProvider(
        ID3D11Device device,
        ID3D11DeviceContext context,
        IDXGIOutputDuplication duplication,
        Rectangle captureBounds)
    {
        _device = device;
        _context = context;
        _duplication = duplication;
        CaptureBounds = captureBounds;
        _width = captureBounds.Width;
        _height = captureBounds.Height;
    }

    public Rectangle CaptureBounds { get; }

    public static bool TryCreate(out D3D11DesktopDuplicationFrameProvider? provider, out string? reason)
    {
        provider = null;
        reason = null;

        ID3D11Device? device = null;
        ID3D11DeviceContext? context = null;

        try
        {
            var featureLevels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 };
            var createResult = D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out device,
                out context);

            if (createResult.Failure)
            {
                reason = $"D3D11 device creation failed ({createResult.Code}).";
                return false;
            }

            using var dxgiDevice = device!.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            adapter.EnumOutputs(0, out var output).CheckError();
            using (output)
            {
                using var output1 = output.QueryInterface<IDXGIOutput1>();

                IDXGIOutputDuplication? duplication;
                try
                {
                    duplication = output1.DuplicateOutput(device);
                }
                catch (Exception dupEx)
                {
                    reason = $"DXGI DuplicateOutput failed: {dupEx.Message}. Another app may be capturing or access is denied.";
                    return false;
                }

                if (duplication is null)
                {
                    reason = "DXGI DuplicateOutput returned null.";
                    return false;
                }

                var bounds = FormsScreen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);

                provider = new D3D11DesktopDuplicationFrameProvider(
                    device,
                    context,
                    duplication,
                    new Rectangle(
                        bounds.Left,
                        bounds.Top,
                        bounds.Width,
                        bounds.Height));

                device = null;
                context = null;

                return true;
            }
        }
        catch (Exception ex)
        {
            reason = $"Exception during D3D11 duplication init: {ex.Message}";
            return false;
        }
        finally
        {
            context?.Dispose();
            device?.Dispose();
        }
    }

    public bool TryCaptureFrame(out Bitmap? bitmap)
    {
        bitmap = null;

        IDXGIResource? resource = null;
        var releaseFrame = false;

        try
        {
            var result = _duplication.AcquireNextFrame(16, out _, out resource);
            if (result == Vortice.DXGI.ResultCode.WaitTimeout)
            {
                return false;
            }

            if (result.Failure || resource is null)
            {
                throw new InvalidOperationException($"AcquireNextFrame failed ({result.Code}).");
            }

            releaseFrame = true;

            using var texture = resource.QueryInterface<ID3D11Texture2D>();
            EnsureStagingTexture(texture.Description);

            _context.CopyResource(_stagingTexture!, texture);
            var mapped = _context.Map(_stagingTexture!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                bitmap = CopyMappedTextureToBitmap(mapped, _width, _height);
            }
            finally
            {
                _context.Unmap(_stagingTexture!, 0);
            }

            return true;
        }
        finally
        {
            resource?.Dispose();
            if (releaseFrame)
            {
                _duplication.ReleaseFrame();
            }
        }
    }

    public void Dispose()
    {
        _stagingTexture?.Dispose();
        _duplication.Dispose();
        _context.Dispose();
        _device.Dispose();
    }

    private void EnsureStagingTexture(Texture2DDescription sourceDescription)
    {
        if (_stagingTexture is not null)
        {
            return;
        }

        var stagingDescription = sourceDescription;
        stagingDescription.BindFlags = BindFlags.None;
        stagingDescription.CPUAccessFlags = CpuAccessFlags.Read;
        stagingDescription.Usage = ResourceUsage.Staging;
        stagingDescription.MiscFlags = ResourceOptionFlags.None;

        _stagingTexture = _device.CreateTexture2D(stagingDescription);
    }

    private static unsafe Bitmap CopyMappedTextureToBitmap(MappedSubresource mapped, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, width, height);
        var bitmapData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            var sourceBase = (byte*)mapped.DataPointer;
            var destinationBase = (byte*)bitmapData.Scan0;
            var rowCopyBytes = Math.Min(mapped.RowPitch, bitmapData.Stride);

            for (var y = 0; y < height; y++)
            {
                var sourceRow = sourceBase + (y * mapped.RowPitch);
                var destinationRow = destinationBase + (y * bitmapData.Stride);
                Buffer.MemoryCopy(sourceRow, destinationRow, bitmapData.Stride, rowCopyBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        return bitmap;
    }
}
