using System.Runtime.InteropServices;
using BambiHeavy.Types;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using Resource = SharpDX.DXGI.Resource;
using ResultCode = SharpDX.DXGI.ResultCode;

namespace BambiHeavy.Capture;

public class WindowsScreenCapture : IScreenCapture
{
    private Adapter1? _adapter;
    private Device? _device;
    private OutputDuplication? _duplicatedOutput;
    private Factory1? _factory;
    private int _height;
    private Output1? _output;

    private byte[] _pixels = Array.Empty<byte>();
    private Texture2D? _stagingTexture;
    private int _width;

    public WindowsScreenCapture()
    {
        InitializeDXGI();
    }

    public Rectangle GetPrimaryScreenBounds()
    {
        return new Rectangle(0, 0, _width, _height);
    }

    public ScreenCaptureResult Capture(Rectangle bounds, int timeoutMs = 50)
    {
        Resource? screenResource = null;
        OutputDuplicateFrameInformation frameInfo;

        try
        {
            // Acquire the next frame
            var result = _duplicatedOutput!.TryAcquireNextFrame(timeoutMs, out frameInfo, out screenResource);
            if (!result.Success)
            {
                if (result.Code == ResultCode.WaitTimeout.Result.Code)
                    return new ScreenCaptureResult(_width, _height, _width * 4, _pixels);

                if (result.Code == ResultCode.AccessLost.Result.Code)
                {
                    if (!TryReinitDXGI())
                        return new ScreenCaptureResult(_width, _height, _width * 4, _pixels);
                    return new ScreenCaptureResult(_width, _height, _width * 4, _pixels);
                }

                result.CheckError();
            }
        }
        catch (SharpDXException ex) when (ex.ResultCode.Code == ResultCode.WaitTimeout.Result.Code)
        {
            // Timeout usually means no screen changes (static screen).
            // We just return the last cached _pixels array since nothing moved.
            return new ScreenCaptureResult(_width, _height, _width * 4, _pixels);
        }
        catch (SharpDXException ex) when (ex.ResultCode.Code == ResultCode.AccessLost.Result.Code)
        {
            // Display topology changed (e.g. resolution change or UAC prompt)
            if (!TryReinitDXGI())
                return new ScreenCaptureResult(_width, _height, _width * 4, _pixels);
            return new ScreenCaptureResult(_width, _height, _width * 4, _pixels);
        }

        using (var screenTexture2D = screenResource.QueryInterface<Texture2D>())
        {
            _device!.ImmediateContext.CopyResource(screenTexture2D, _stagingTexture);
        }

        screenResource.Dispose();
        _duplicatedOutput.ReleaseFrame();

        var mapSource = _device!.ImmediateContext.MapSubresource(_stagingTexture, 0, MapMode.Read, MapFlags.None);

        var stride = mapSource.RowPitch;
        var bytes = stride * _height;

        if (_pixels.Length != bytes) _pixels = new byte[bytes];

        Marshal.Copy(mapSource.DataPointer, _pixels, 0, bytes);

        _device.ImmediateContext.UnmapSubresource(_stagingTexture, 0);

        return new ScreenCaptureResult(_width, _height, stride, _pixels);
    }

    public void Dispose()
    {
        DisposeDXGI();
    }

    private void InitializeDXGI()
    {
        _factory = new Factory1();
        _adapter = _factory.GetAdapter1(0);
        _device = new Device(_adapter);
        _output = _adapter.GetOutput(0).QueryInterface<Output1>();

        _width = _output.Description.DesktopBounds.Right - _output.Description.DesktopBounds.Left;
        _height = _output.Description.DesktopBounds.Bottom - _output.Description.DesktopBounds.Top;

        _duplicatedOutput = _output.DuplicateOutput(_device);

        var texDesc = new Texture2DDescription
        {
            CpuAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            Format = Format.B8G8R8A8_UNorm,
            Width = _width,
            Height = _height,
            OptionFlags = ResourceOptionFlags.None,
            MipLevels = 1,
            ArraySize = 1,
            SampleDescription = { Count = 1, Quality = 0 },
            Usage = ResourceUsage.Staging
        };
        _stagingTexture = new Texture2D(_device, texDesc);
    }

    private bool TryReinitDXGI()
    {
        try
        {
            DisposeDXGI();
            InitializeDXGI();
            return true;
        }
        catch (SharpDXException ex)
        {
            // UAC prompt still on screen, desktop ownership unavailable, etc.
            // Caller will retry on the next frame.
            Console.WriteLine($"[WARN] DXGI reinit failed: 0x{ex.ResultCode.Code:X8} — returning cached frame.");
            return false;
        }
        catch (Exception ex)
        {
            // DXGI objects may be in a broken state (e.g. after sleep/wake).
            Console.WriteLine($"[WARN] DXGI reinit failed: {ex.GetType().Name}: {ex.Message} — returning cached frame.");
            return false;
        }
    }

    private void DisposeDXGI()
    {
        _stagingTexture?.Dispose();
        _duplicatedOutput?.Dispose();
        _output?.Dispose();
        _device?.Dispose();
        _adapter?.Dispose();
        _factory?.Dispose();
    }
}