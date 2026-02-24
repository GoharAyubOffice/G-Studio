using System.Drawing;
using System.Drawing.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using static Vortice.Direct3D11.D3D11;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace GStudio.Capture.Recorder.FrameProviders;

internal sealed class WgcFrameProvider : IDesktopFrameProvider
{
    private readonly ID3D11Device _d3dDevice;
    private readonly ID3D11DeviceContext _d3dContext;
    private readonly IDirect3DDevice _direct3DDevice;
    private readonly GraphicsCaptureItem _captureItem;
    private readonly Direct3D11CaptureFramePool _framePool;
    private readonly GraphicsCaptureSession _captureSession;

    private readonly int _width;
    private readonly int _height;

    private ID3D11Texture2D? _stagingTexture;

    private WgcFrameProvider(
        ID3D11Device d3dDevice,
        ID3D11DeviceContext d3dContext,
        IDirect3DDevice direct3DDevice,
        GraphicsCaptureItem captureItem,
        Direct3D11CaptureFramePool framePool,
        GraphicsCaptureSession captureSession,
        Rectangle captureBounds)
    {
        _d3dDevice = d3dDevice;
        _d3dContext = d3dContext;
        _direct3DDevice = direct3DDevice;
        _captureItem = captureItem;
        _framePool = framePool;
        _captureSession = captureSession;
        CaptureBounds = captureBounds;
        _width = captureBounds.Width;
        _height = captureBounds.Height;
    }

    public Rectangle CaptureBounds { get; }

    public static bool TryCreate(out WgcFrameProvider? provider, out string? reason)
    {
        provider = null;
        reason = null;

        ID3D11Device? d3dDevice = null;
        ID3D11DeviceContext? d3dContext = null;
        IDirect3DDevice? direct3DDevice = null;
        GraphicsCaptureItem? captureItem = null;
        Direct3D11CaptureFramePool? framePool = null;
        GraphicsCaptureSession? captureSession = null;

        try
        {
            if (!WgcInterop.TryCreatePrimaryMonitorItem(out captureItem, out var captureBounds, out reason) || captureItem is null)
            {
                return false;
            }

            var featureLevels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1 };
            var createResult = D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out d3dDevice,
                out d3dContext);

            if (createResult.Failure || d3dDevice is null || d3dContext is null)
            {
                reason = $"Failed to create D3D11 device for WGC ({createResult.Code}).";
                return false;
            }

            direct3DDevice = WgcInterop.CreateDirect3DDevice(d3dDevice);

            framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                direct3DDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                3,
                captureItem.Size);

            captureSession = framePool.CreateCaptureSession(captureItem);
            captureSession.IsCursorCaptureEnabled = false;
            captureSession.StartCapture();

            provider = new WgcFrameProvider(
                d3dDevice,
                d3dContext,
                direct3DDevice,
                captureItem,
                framePool,
                captureSession,
                captureBounds);

            d3dDevice = null;
            d3dContext = null;
            direct3DDevice = null;
            captureItem = null;
            framePool = null;
            captureSession = null;

            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
        finally
        {
            captureSession?.Dispose();
            framePool?.Dispose();
            (direct3DDevice as IDisposable)?.Dispose();
            d3dContext?.Dispose();
            d3dDevice?.Dispose();
        }
    }

    public bool TryCaptureFrame(out Bitmap? bitmap)
    {
        bitmap = null;

        try
        {
            using var frame = _framePool.TryGetNextFrame();
            if (frame is null)
            {
                return false;
            }

            using var sourceTexture = WgcInterop.CreateTextureFromSurface(frame.Surface);
            EnsureStagingTexture(sourceTexture.Description);

            _d3dContext.CopyResource(_stagingTexture!, sourceTexture);
            var mapped = _d3dContext.Map(_stagingTexture!, 0, MapMode.Read, MapFlags.None);

            try
            {
                bitmap = CopyMappedTextureToBitmap(mapped, _width, _height);
            }
            finally
            {
                _d3dContext.Unmap(_stagingTexture!, 0);
            }

            return true;
        }
        catch
        {
            bitmap?.Dispose();
            bitmap = null;
            return false;
        }
    }

    public void Dispose()
    {
        _stagingTexture?.Dispose();
        _captureSession.Dispose();
        _framePool.Dispose();
        (_direct3DDevice as IDisposable)?.Dispose();
        _d3dContext.Dispose();
        _d3dDevice.Dispose();
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

        _stagingTexture = _d3dDevice.CreateTexture2D(stagingDescription);
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
