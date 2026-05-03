using System.Runtime.InteropServices;
using BambiHeavy.Types;

namespace BambiHeavy.Capture;

public class LinuxScreenCapture : IScreenCapture
{
    private IntPtr _display = IntPtr.Zero;

    public Rectangle GetPrimaryScreenBounds()
    {
        CheckX11();
        if (_display == IntPtr.Zero)
            _display = XOpenDisplay(IntPtr.Zero);

        IntPtr root = DefaultRootWindow(_display);
        XGetGeometry(_display, root, out IntPtr rootReturn, out int x, out int y, out uint width, out uint height, out _, out _);
        return new Rectangle(x, y, (int)width, (int)height);
    }

    public ScreenCaptureResult Capture(Rectangle bounds)
    {
        CheckX11();
        if (_display == IntPtr.Zero)
            _display = XOpenDisplay(IntPtr.Zero);

        IntPtr root = DefaultRootWindow(_display);
        IntPtr image = XGetImage(_display, root, bounds.X, bounds.Y, (uint)bounds.Width, (uint)bounds.Height, ~0, ZPixmap);
        if (image == IntPtr.Zero)
            throw new InvalidOperationException("XGetImage failed - are you running under X11? Wayland is not supported.");

        int width = XImageWidth(image);
        int height = XImageHeight(image);
        int bytesPerPixel = XImageBytesPerPixel(image);
        int stride = XImageScanlinePad(_display, width * bytesPerPixel);
        stride = (stride / 8) * bytesPerPixel;
        int totalBytes = height * stride;

        IntPtr data = XImageData(image);
        byte[] pixels = new byte[totalBytes];
        Marshal.Copy(data, pixels, 0, totalBytes);

        XDestroyImage(image);

        if (bytesPerPixel == 3)
        {
            byte[] bgra = new byte[width * 4 * height];
            for (int y = 0; y < height; y++)
            {
                int srcIdx = y * stride;
                int dstIdx = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    bgra[dstIdx] = pixels[srcIdx];
                    bgra[dstIdx + 1] = pixels[srcIdx + 1];
                    bgra[dstIdx + 2] = pixels[srcIdx + 2];
                    bgra[dstIdx + 3] = 255;
                    srcIdx += 3;
                    dstIdx += 4;
                }
            }
            return new ScreenCaptureResult(width, height, width * 4, bgra);
        }

        return new ScreenCaptureResult(width, height, stride, pixels);
    }

    static void CheckX11()
    {
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        if (waylandDisplay != null && Environment.GetEnvironmentVariable("DISPLAY") == null)
            throw new PlatformNotSupportedException(
                "Wayland detected. BambiHeavy requires X11 for screen capture. " +
                "Either run under X11, or set DISPLAY to an Xwayland display.");
    }

    public void Dispose()
    {
        if (_display != IntPtr.Zero)
        {
            XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }

    private const string LibX11 = "libX11";

    [DllImport(LibX11)] public static extern IntPtr XOpenDisplay(IntPtr displayName);
    [DllImport(LibX11)] public static extern void XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] public static extern IntPtr DefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] public static extern void XGetGeometry(IntPtr display, IntPtr drawable, out IntPtr root, out int x, out int y, out uint width, out uint height, out uint borderWidth, out int depth);
    [DllImport(LibX11)] public static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, int planeMask, int format);
    [DllImport(LibX11)] public static extern void XDestroyImage(IntPtr image);
    [DllImport(LibX11)] public static extern int XImageWidth(IntPtr image);
    [DllImport(LibX11)] public static extern int XImageHeight(IntPtr image);
    [DllImport(LibX11)] public static extern int XImageBytesPerPixel(IntPtr image);
    [DllImport(LibX11)] public static extern int XImageScanlinePad(IntPtr display, int byteCount);
    [DllImport(LibX11)] public static extern IntPtr XImageData(IntPtr image);

    private const int ZPixmap = 2;
}
