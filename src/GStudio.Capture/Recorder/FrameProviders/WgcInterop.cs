using System.Drawing;
using System.Runtime.InteropServices;
using WinRT;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;

namespace GStudio.Capture.Recorder.FrameProviders;

internal static class WgcInterop
{
    private const uint MonitorDefaultToPrimary = 0x00000001;
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

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

            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            var itemPointer = interop.CreateForMonitor(monitor, in GraphicsCaptureItemGuid);
            if (itemPointer == 0)
            {
                reason = "CreateForMonitor returned null item pointer.";
                return false;
            }

            captureItem = GraphicsCaptureItem.FromAbi(itemPointer);
            Marshal.Release(itemPointer);

            var monitorBounds = TryGetMonitorBounds(monitor, out var resolvedBounds)
                ? resolvedBounds
                : new Rectangle(0, 0, Math.Max(1, captureItem.Size.Width), Math.Max(1, captureItem.Size.Height));

            captureBounds = new Rectangle(
                monitorBounds.Left,
                monitorBounds.Top,
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

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(nint monitorHandle, ref NativeMonitorInfo monitorInfo);

    private static bool TryGetMonitorBounds(nint monitorHandle, out Rectangle bounds)
    {
        var monitorInfo = new NativeMonitorInfo
        {
            Size = Marshal.SizeOf<NativeMonitorInfo>()
        };

        if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            bounds = Rectangle.Empty;
            return false;
        }

        bounds = new Rectangle(
            monitorInfo.MonitorRect.Left,
            monitorInfo.MonitorRect.Top,
            Math.Max(1, monitorInfo.MonitorRect.Right - monitorInfo.MonitorRect.Left),
            Math.Max(1, monitorInfo.MonitorRect.Bottom - monitorInfo.MonitorRect.Top));

        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public int X { get; init; }

        public int Y { get; init; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NativeMonitorInfo
    {
        public int Size;

        public NativeRect MonitorRect;

        public NativeRect WorkRect;

        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }
}

[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
internal interface IDirect3DDxgiInterfaceAccess
{
    nint GetInterface(in Guid iid);
}

[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[ComVisible(true)]
internal interface IGraphicsCaptureItemInterop
{
    nint CreateForWindow(nint windowHandle, in Guid iid);

    nint CreateForMonitor(nint monitorHandle, in Guid iid);
}
