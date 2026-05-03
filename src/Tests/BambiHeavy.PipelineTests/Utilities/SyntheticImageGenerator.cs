using BambiHeavy.Capture;

namespace BambiHeavy.PipelineTests.Utilities;

public static class SyntheticImageGenerator
{
    public static ScreenCaptureResult SolidColor(int width, int height, byte r, byte g, byte b)
    {
        var stride = width * 4;
        var pixels = new byte[height * stride];

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < width; x++)
            {
                var idx = rowOffset + x * 4;
                pixels[idx] = b;
                pixels[idx + 1] = g;
                pixels[idx + 2] = r;
                pixels[idx + 3] = 0xFF;
            }
        }

        return new ScreenCaptureResult(width, height, stride, pixels);
    }

    public static ScreenCaptureResult HorizontalGradient(int width, int height, byte rStart, byte gStart, byte bStart,
        byte rEnd, byte gEnd, byte bEnd)
    {
        var stride = width * 4;
        var pixels = new byte[height * stride];

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < width; x++)
            {
                var t = width > 1 ? (double)x / (width - 1) : 0;
                var r = (byte)(rStart + (rEnd - rStart) * t);
                var g = (byte)(gStart + (gEnd - gStart) * t);
                var b = (byte)(bStart + (bEnd - bStart) * t);

                var idx = rowOffset + x * 4;
                pixels[idx] = b;
                pixels[idx + 1] = g;
                pixels[idx + 2] = r;
                pixels[idx + 3] = 0xFF;
            }
        }

        return new ScreenCaptureResult(width, height, stride, pixels);
    }

    public static ScreenCaptureResult VerticalGradient(int width, int height, byte rStart, byte gStart, byte bStart,
        byte rEnd, byte gEnd, byte bEnd)
    {
        var stride = width * 4;
        var pixels = new byte[height * stride];

        for (var y = 0; y < height; y++)
        {
            var t = height > 1 ? (double)y / (height - 1) : 0;
            var r = (byte)(rStart + (rEnd - rStart) * t);
            var g = (byte)(gStart + (gEnd - gStart) * t);
            var b = (byte)(bStart + (bEnd - bStart) * t);

            var rowOffset = y * stride;
            for (var x = 0; x < width; x++)
            {
                var idx = rowOffset + x * 4;
                pixels[idx] = b;
                pixels[idx + 1] = g;
                pixels[idx + 2] = r;
                pixels[idx + 3] = 0xFF;
            }
        }

        return new ScreenCaptureResult(width, height, stride, pixels);
    }

    public static ScreenCaptureResult CenteredContent(int outerW, int outerH, int contentW, int contentH, byte contentR,
        byte contentG, byte contentB, byte borderR = 0, byte borderG = 0, byte borderB = 0)
    {
        var stride = outerW * 4;
        var pixels = new byte[outerH * stride];

        var contentLeft = (outerW - contentW) / 2;
        var contentTop = (outerH - contentH) / 2;

        for (var y = 0; y < outerH; y++)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < outerW; x++)
            {
                var inContent = x >= contentLeft && x < contentLeft + contentW && y >= contentTop &&
                                y < contentTop + contentH;
                var r = inContent ? contentR : borderR;
                var g = inContent ? contentG : borderG;
                var b = inContent ? contentB : borderB;

                var idx = rowOffset + x * 4;
                pixels[idx] = b;
                pixels[idx + 1] = g;
                pixels[idx + 2] = r;
                pixels[idx + 3] = 0xFF;
            }
        }

        return new ScreenCaptureResult(outerW, outerH, stride, pixels);
    }

    public static ScreenCaptureResult Letterbox16_9(int outerW, int outerH, byte contentR, byte contentG, byte contentB)
    {
        var targetAr = 16.0 / 9.0;
        var outerAr = (double)outerW / outerH;

        int contentW, contentH;
        if (outerAr > targetAr)
        {
            contentH = outerH;
            contentW = (int)(contentH * targetAr);
        }
        else
        {
            contentW = outerW;
            contentH = (int)(contentW / targetAr);
        }

        return CenteredContent(outerW, outerH, contentW, contentH, contentR, contentG, contentB);
    }

    public static ScreenCaptureResult Checkerboard(int width, int height, int cellSize, byte color1R, byte color1G,
        byte color1B, byte color2R, byte color2G, byte color2B)
    {
        var stride = width * 4;
        var pixels = new byte[height * stride];

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < width; x++)
            {
                var cellX = x / cellSize;
                var cellY = y / cellSize;
                var isColor1 = (cellX + cellY) % 2 == 0;

                var r = isColor1 ? color1R : color2R;
                var g = isColor1 ? color1G : color2G;
                var b = isColor1 ? color1B : color2B;

                var idx = rowOffset + x * 4;
                pixels[idx] = b;
                pixels[idx + 1] = g;
                pixels[idx + 2] = r;
                pixels[idx + 3] = 0xFF;
            }
        }

        return new ScreenCaptureResult(width, height, stride, pixels);
    }

    public static ScreenCaptureResult Quads(int width, int height, byte topLeftR, byte topLeftG, byte topLeftB,
        byte topRightR, byte topRightG, byte topRightB, byte bottomLeftR, byte bottomLeftG, byte bottomLeftB,
        byte bottomRightR, byte bottomRightG, byte bottomRightB)
    {
        var stride = width * 4;
        var pixels = new byte[height * stride];
        var midX = width / 2;
        var midY = height / 2;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < width; x++)
            {
                byte r, g, b;
                if (x < midX && y < midY)
                {
                    r = topLeftR;
                    g = topLeftG;
                    b = topLeftB;
                }
                else if (x >= midX && y < midY)
                {
                    r = topRightR;
                    g = topRightG;
                    b = topRightB;
                }
                else if (x < midX && y >= midY)
                {
                    r = bottomLeftR;
                    g = bottomLeftG;
                    b = bottomLeftB;
                }
                else
                {
                    r = bottomRightR;
                    g = bottomRightG;
                    b = bottomRightB;
                }

                var idx = rowOffset + x * 4;
                pixels[idx] = b;
                pixels[idx + 1] = g;
                pixels[idx + 2] = r;
                pixels[idx + 3] = 0xFF;
            }
        }

        return new ScreenCaptureResult(width, height, stride, pixels);
    }
}