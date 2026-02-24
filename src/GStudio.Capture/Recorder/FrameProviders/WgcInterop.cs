using System.Drawing;
using System.Runtime.InteropServices;
using WinRT;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using FormsScreen = System.Windows.Forms.Screen;

namespace GStudio.Capture.Recorder.FrameProviders;

internal static class WgcInterop
{
    private const uint MonitorDefaultToPrimary = 0x00000001;

    public static IDirect3DDevice CreateDirect3DDevice(ID3D11Device d3dDevice)
    {
        using var dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var devicePointer);
        if (hr < 0 || devicePointer == 0)
        {
            throw new InvalidOperationException($"CreateDirect3D11DeviceFromDXGIDevice failed ({hr}).");
        }

        var direct3DDevice = MarshalInterface<IDirect3DDevice>.FromAbi(devicePointer);
        Marshal.Release(devicePointer);
        return direct3DDevice;
    }

    public static bool TryCreatePrimaryMonitorItem(
        out GraphicsCaptureItem? captureItem,
        out Rectangle captureBounds,
        out string? reason)
    {
        captureItem = null;
        captureBounds = Rectangle.Empty;
        reason = null;

        if (!GraphicsCaptureSession.IsSupported())
        {
            reason = "Windows Graphics Capture is not supported on this system.";
            return false;
        }

        try
        {
            var monitor = MonitorFromPoint(new NativePoint { X = 0, Y = 0 }, MonitorDefaultToPrimary);
            if (monitor == 0)
            {
                reason = "Failed to resolve primary monitor handle.";
                return false;
            }

            var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
            var interop = (IGraphicsCaptureItemInterop)factory;
            var iid = typeof(GraphicsCaptureItem).GUID;
            var hr = interop.CreateForMonitor(monitor, in iid, out var itemPointer);
            if (hr < 0 || itemPointer == 0)
            {
                reason = $"CreateForMonitor failed ({hr}).";
                return false;
            }

            captureItem = MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
            Marshal.Release(itemPointer);

            var primaryBounds = FormsScreen.PrimaryScreen?.Bounds
                ?? new Rectangle(0, 0, Math.Max(1, captureItem.Size.Width), Math.Max(1, captureItem.Size.Height));

            captureBounds = new Rectangle(
                primaryBounds.Left,
                primaryBounds.Top,
                Math.Max(1, captureItem.Size.Width),
                Math.Max(1, captureItem.Size.Height));

            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    public static ID3D11Texture2D CreateTextureFromSurface(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var texturePointer = access.GetInterface(typeof(ID3D11Texture2D).GUID);
        if (texturePointer == 0)
        {
            throw new InvalidOperationException("Failed to get ID3D11Texture2D from capture surface.");
        }

        return new ID3D11Texture2D(texturePointer);
    }

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public int X { get; init; }

        public int Y { get; init; }
    }
}

[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    nint GetInterface(in Guid iid);
}

[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    int CreateForWindow(nint windowHandle, in Guid iid, out nint result);

    int CreateForMonitor(nint monitorHandle, in Guid iid, out nint result);
}
