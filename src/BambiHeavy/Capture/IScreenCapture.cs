using BambiHeavy.Types;

namespace BambiHeavy.Capture;

public interface IScreenCapture : IDisposable
{
    Rectangle GetPrimaryScreenBounds();
    ScreenCaptureResult Capture(Rectangle bounds, int timeoutMs = 50);
}

public readonly struct ScreenCaptureResult : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public byte[] Pixels { get; }

    public ScreenCaptureResult(int width, int height, int stride, byte[] pixels)
    {
        Width = width;
        Height = height;
        Stride = stride;
        Pixels = pixels;
    }

    public void Dispose()
    {
    }
}