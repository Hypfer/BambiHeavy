using BambiHeavy.Pipeline;

namespace BambiHeavy.PipelineTests.Pipeline;

public class CompressTests : IDisposable
{
    private readonly TuningProfile _profile;

    public CompressTests()
    {
        _profile = Config.GetProfile();
    }

    public void Dispose()
    {
        Config.Reset();
    }

    private static (int r, int g, int b) To8Bit(double r, double g, double b)
    {
        return ((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
    }

    [Fact]
    public void Black_RemainsBlack()
    {
        double r = 0.0, g = 0.0, b = 0.0;
        Compress.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertBlack(To8Bit(r, g, b));
    }

    [Fact]
    public void White_RemainsWhite()
    {
        double r = 1.0, g = 1.0, b = 1.0;
        Compress.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertWhite(To8Bit(r, g, b));
    }

    [Fact]
    public void MidGray_Darkens()
    {
        double r = 0x80 / 255.0, g = 0x80 / 255.0, b = 0x80 / 255.0;
        Compress.Apply(ref r, ref g, ref b, _profile);
        r.Should().BeLessThan(0x80 / 255.0, "Exponent > 1 should darken mid values");
    }

    [Theory]
    [InlineData(0xFF, 0x00, 0x00)]
    [InlineData(0x00, 0xFF, 0x00)]
    [InlineData(0x00, 0x00, 0xFF)]
    [InlineData(0x80, 0x40, 0xC0)]
    public void Output_InValidRange(byte r, byte g, byte b)
    {
        double ri = r / 255.0, gi = g / 255.0, bi = b / 255.0;
        Compress.Apply(ref ri, ref gi, ref bi, _profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(ri, gi, bi));
    }

    [Fact]
    public void LowValues_TruncateToZero()
    {
        for (byte v = 1; v <= 11; v++)
        {
            double r = v / 255.0, g = 0.0, b = 0.0;
            Compress.Apply(ref r, ref g, ref b, _profile);
            var rounded = (int)Math.Round(r * 255);
            rounded.Should().Be(0, $"Compression of {v} should round to 0");
        }
    }

    [Theory]
    [InlineData(0xFE)]
    [InlineData(0xF0)]
    [InlineData(0xF1)]
    [InlineData(0xF2)]
    public void NearWhite_StrictlyIncreasing(byte v)
    {
        double r = v / 255.0, g = 0.0, b = 0.0;
        Compress.Apply(ref r, ref g, ref b, _profile);
        var cur = r;

        double prevR = (v - 1) / 255.0, prevG = 0.0, prevB = 0.0;
        Compress.Apply(ref prevR, ref prevG, ref prevB, _profile);
        var prevOut = prevR;

        cur.Should().BeGreaterThan(prevOut, $"Compression should be strictly increasing: ({v}) > ({v - 1})");
    }

    [Theory]
    [InlineData(BambiStyle.Standard, 3.0)]
    [InlineData(BambiStyle.Cinema, 2.6)]
    [InlineData(BambiStyle.Sports, 2.0)]
    [InlineData(BambiStyle.Gaming, 2.0)]
    public void PerStyle_ExponentApplied(BambiStyle style, double expectedExponent)
    {
        Config.ActiveStyle = style;
        var profile = Config.GetProfile();
        profile.CompressExponent.Should().Be(expectedExponent);

        double r = 0.5, g = 0.0, b = 0.0;
        Compress.Apply(ref r, ref g, ref b, profile);
        var expected = Math.Pow(0.5, expectedExponent);
        Math.Abs(r - expected).Should().BeLessOrEqualTo(0.001,
            $"Style {style} should apply exponent {expectedExponent}");
    }

    [Fact]
    public void HigherExponent_CrushesMore()
    {
        double r1 = 0.5, g1 = 0.0, b1 = 0.0;
        double r2 = 0.5, g2 = 0.0, b2 = 0.0;

        var heavy = new TuningProfile(3.0, 1.0, 1.0, 1.0, 6.25, 8, 16, 8, 10);
        var light = new TuningProfile(2.0, 1.0, 1.0, 1.0, 6.25, 8, 16, 8, 10);

        Compress.Apply(ref r1, ref g1, ref b1, heavy);
        Compress.Apply(ref r2, ref g2, ref b2, light);

        r1.Should().BeLessThan(r2, "Exponent 3.0 should crush more than 2.0");
    }
}