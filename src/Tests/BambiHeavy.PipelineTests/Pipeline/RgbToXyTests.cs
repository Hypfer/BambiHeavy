using BambiHeavy.Pipeline;

namespace BambiHeavy.PipelineTests.Pipeline;

public class RgbToXyTests
{
    [Fact]
    public void Black_ProducesZeroBrightness()
    {
        var output = RgbToXy.Apply(0.0, 0.0, 0.0);
        output.Brightness.Should().Be(0);
    }

    [Fact]
    public void Black_OutputD65WhitePoint()
    {
        var output = RgbToXy.Apply(0, 0, 0);
        // D65: x=0.3127, y=0.3290, raw CIE scaled to 10-bit
        output.X.Should().Be((ushort)Math.Round(0.3127 * 1023));
        output.Y.Should().Be((ushort)Math.Round(0.3290 * 1023));
    }

    [Fact]
    public void White_FullBrightness()
    {
        var output = RgbToXy.Apply(1.0, 1.0, 1.0);
        output.Brightness.Should().Be(2047);
    }

    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    public void SaturatedColors_FullBrightness(byte r, byte g, byte b)
    {
        var output = RgbToXy.Apply(r / 255.0, g / 255.0, b / 255.0);
        output.Brightness.Should().Be(2047,
            $"Saturated color ({r},{g},{b}) should produce full brightness");
    }

    [Fact]
    public void Output_InValidRanges()
    {
        var testCases = new (double r, double g, double b)[]
        {
            (0, 0, 0), (1, 1, 1), (1, 0, 0), (0, 1, 0), (0, 0, 1),
            (128.0 / 255, 128.0 / 255, 128.0 / 255), (64.0 / 255, 128.0 / 255, 192.0 / 255),
            (200.0 / 255, 100.0 / 255, 50.0 / 255)
        };
        foreach (var (r, g, b) in testCases)
        {
            var output = RgbToXy.Apply(r, g, b);
            output.Brightness.Should().BeInRange(0, 2047, $"Brightness valid for ({r},{g},{b})");
            output.X.Should().BeInRange(0, 1023, $"X valid for ({r},{g},{b})");
            output.Y.Should().BeInRange(0, 1023, $"Y valid for ({r},{g},{b})");
        }
    }

    [Fact]
    public void MonotonicBrightness_GrayRamp()
    {
        ushort prev = 0;
        for (var i = 0; i <= 255; i++)
        {
            var v = i / 255.0;
            var output = RgbToXy.Apply(v, v, v);
            output.Brightness.Should().BeGreaterOrEqualTo(prev,
                $"Gray brightness should be monotonic at {i}");
            prev = output.Brightness;
        }
    }

    [Fact]
    public void Deterministic()
    {
        var out1 = RgbToXy.Apply(128.0 / 255, 64.0 / 255, 192.0 / 255);
        var out2 = RgbToXy.Apply(128.0 / 255, 64.0 / 255, 192.0 / 255);
        out1.Should().Be(out2);
    }

    [Fact]
    public void Red_HighXChromaticity()
    {
        var output = RgbToXy.Apply(1.0, 0.0, 0.0);
        output.X.Should().BeGreaterThan(output.Y, "Red should have higher X than Y");
    }

    [Fact]
    public void Green_HighYChromaticity()
    {
        var output = RgbToXy.Apply(0.0, 1.0, 0.0);
        output.Y.Should().BeGreaterThan(output.X, "Green should have higher Y than X");
    }

    [Fact]
    public void Blue_LowChromaticity()
    {
        var output = RgbToXy.Apply(0.0, 0.0, 1.0);
        output.X.Should().BeLessThan(20000, "Blue should have low X");
        output.Y.Should().BeLessThan(10000, "Blue should have low Y");
    }

    [Fact]
    public void GrayRamp_WhitePointChromaticity()
    {
        // Grays should all produce the same chromaticity (white point)
        var gray1 = RgbToXy.Apply(64.0 / 255, 64.0 / 255, 64.0 / 255);
        var gray2 = RgbToXy.Apply(128.0 / 255, 128.0 / 255, 128.0 / 255);
        var gray3 = RgbToXy.Apply(192.0 / 255, 192.0 / 255, 192.0 / 255);
        gray1.X.Should().Be(gray2.X, "Gray chromaticity should be independent of brightness");
        gray2.X.Should().Be(gray3.X);
        gray1.Y.Should().Be(gray2.Y);
        gray2.Y.Should().Be(gray3.Y);
    }

    [Fact]
    public void PureVsNearPureRed_SmallOutputChanges()
    {
        var pure = RgbToXy.Apply(1.0, 0.0, 0.0);
        var near1 = RgbToXy.Apply(1.0, 1.0 / 255, 0.0);
        var near2 = RgbToXy.Apply(1.0, 2.0 / 255, 0.0);

        Math.Abs(pure.X - near1.X).Should().BeLessThan(200,
            "X change from (1.0,0,0) to (1.0,1/255,0) should be small");
        Math.Abs(pure.Y - near1.Y).Should().BeLessThan(200,
            "Y change from (1.0,0,0) to (1.0,1/255,0) should be small");
        Math.Abs(pure.Brightness - near1.Brightness).Should().Be(0,
            "Brightness should not change (max channel is still 1.0)");

        Math.Abs(near1.X - near2.X).Should().BeLessThan(200,
            "X change from (1.0,1/255,0) to (1.0,2/255,0) should be small");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(224)]
    [InlineData(255)]
    public void GrayLevelSampling_ValidOutput(byte gray)
    {
        var v = gray / 255.0;
        var output = RgbToXy.Apply(v, v, v);
        output.Brightness.Should().BeInRange(0, 2047);
        output.X.Should().BeInRange(0, 1023);
        output.Y.Should().BeInRange(0, 1023);

        if (gray == 0)
            output.Brightness.Should().Be(0);
        else if (gray >= 128)
            output.Brightness.Should().BeGreaterOrEqualTo(128);
    }

    [Fact]
    public void ComplementaryColors_DistinctChromaticity()
    {
        var red = RgbToXy.Apply(1.0, 0.0, 0.0);
        var cyan = RgbToXy.Apply(0.0, 1.0, 1.0);
        var green = RgbToXy.Apply(0.0, 1.0, 0.0);
        var magenta = RgbToXy.Apply(1.0, 0.0, 1.0);
        var blue = RgbToXy.Apply(0.0, 0.0, 1.0);
        var yellow = RgbToXy.Apply(1.0, 1.0, 0.0);

        Math.Abs(red.X - cyan.X).Should().BeGreaterThan(100,
            "Red and cyan should be far apart in X chromaticity");
        Math.Abs(blue.Y - yellow.Y).Should().BeGreaterThan(100,
            "Blue and yellow should be far apart in Y chromaticity");
    }
}