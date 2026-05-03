using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using BambiHeavy.Capture;
using BambiHeavy.Edge;
using Context = System.IO.Path;

namespace BambiHeavy.PipelineTests.Edge;

public class ContentBoundsDetectorTests : IDisposable
{
    public void Dispose()
    {
        ContentBoundsDetector.Reset();
    }

    public static IEnumerable<object[]> TestCaseData()
    {
        // (filename, expectedLeft, expectedTop, expectedRight, expectedBottom)
        foreach (var (file, l, t, r, b) in new[]
                 {
                     ("1.png", 497, 3, 3343, 1600),
                     ("2.png", 497, 3, 3343, 1600),
                     ("3.png", 0, 0, 3840, 1600),
                     ("4.png", 495, 0, 3345, 1600),
                     ("5.png", 0, 0, 3840, 1600),
                     ("6.png", 0, 0, 3840, 1600),
                     ("7.png", 0, 0, 3840, 1600),
                     ("8.png", 0, 0, 3840, 1600),
                     ("9.png", 834, 0, 3008, 1600),
                     ("10.png", 498, 0, 3341, 1600),
                     ("11.png", 498, 204, 3341, 1395),
                     ("12.png", 498, 204, 3341, 1395),
                     ("13.png", 0, 0, 3840, 1600),
                     ("14.png", 498, 0, 3341, 1600),
                     ("15.png", 498, 0, 3341, 1600),
                     ("16.png", 640, 0, 3199, 1600),
                 })
            yield return [file, l, t, r, b];
    }

    private static (byte[] pixels, int width, int height, int stride) LoadPngAsBgra(string fileName)
    {
        var testDir = Context.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location!)!, "Assets");
        var imgPath = Context.Combine(testDir, fileName);

        using var bmp = new Bitmap(imgPath);
        var lockRect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var bmpData = bmp.LockBits(lockRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        var stride = Math.Abs(bmpData.Stride);
        var bytes = stride * bmp.Height;
        var pixels = new byte[bytes];
        Marshal.Copy(bmpData.Scan0, pixels, 0, bytes);
        bmp.UnlockBits(bmpData);

        return (pixels, bmp.Width, bmp.Height, stride);
    }

    [Theory]
    [MemberData(nameof(TestCaseData))]
    public void DetectContentBounds_RealWorldScreenshots(string fileName, int expLeft, int expTop, int expRight,
        int expBottom)
    {
        ContentBoundsDetector.Reset();

        var (pixels, width, height, stride) = LoadPngAsBgra(fileName);
        var screenBounds = new Types.Rectangle(0, 0, width, height);
        var frame = new ScreenCaptureResult(width, height, stride, pixels);

        var (left, top, right, bottom) = ContentBoundsDetector.Detect(frame, screenBounds);

        var tolerance = 20;
        left.Should().BeInRange(expLeft - tolerance, expLeft + tolerance,
            $"Left edge for {fileName}");
        top.Should().BeInRange(expTop - tolerance, expTop + tolerance,
            $"Top edge for {fileName}");
        right.Should().BeInRange(expRight - tolerance, expRight + tolerance,
            $"Right edge for {fileName}");
        bottom.Should().BeInRange(expBottom - tolerance, expBottom + tolerance,
            $"Bottom edge for {fileName}");
    }
}