using BambiHeavy.Pipeline;

namespace BambiHeavy.PipelineTests.Pipeline;

public class SaturationBoostTests : IDisposable
{
    private readonly TuningProfile _profile;

    public SaturationBoostTests()
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
        SaturationBoost.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertBlack(To8Bit(r, g, b));
    }

    [Theory]
    [InlineData(0x80)]
    [InlineData(0x40)]
    [InlineData(0xC0)]
    public void Gray_Preserved(byte v)
    {
        double r = v / 255.0, g = v / 255.0, b = v / 255.0;
        SaturationBoost.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertGrayPreserved(To8Bit(r, g, b), 8);
    }

    [Fact]
    public void SaturatedRed_HuePreserved()
    {
        double r = 1.0, g = 0.0, b = 0.0;
        SaturationBoost.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertHuePreserved(To8Bit(r, g, b), 'R');
    }

    [Theory]
    [InlineData(0xFF, 0x00, 0x00)]
    [InlineData(0x00, 0xFF, 0x00)]
    [InlineData(0x00, 0x00, 0xFF)]
    [InlineData(0xFF, 0x80, 0x00)]
    [InlineData(0x80, 0x00, 0xFF)]
    public void Output_InValidRange(byte r, byte g, byte b)
    {
        double ri = r / 255.0, gi = g / 255.0, bi = b / 255.0;
        SaturationBoost.Apply(ref ri, ref gi, ref bi, _profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(ri, gi, bi));
    }

    [Theory]
    [InlineData(BambiStyle.Standard)]
    [InlineData(BambiStyle.Cinema)]
    [InlineData(BambiStyle.Sports)]
    [InlineData(BambiStyle.Gaming)]
    public void AllStyles_ValidOutput(BambiStyle style)
    {
        Config.ActiveStyle = style;
        var profile = Config.GetProfile();
        double r = 0xA0 / 255.0, g = 0x40 / 255.0, b = 0x60 / 255.0;
        SaturationBoost.Apply(ref r, ref g, ref b, profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
    }

    [Fact]
    public void NearBlackRed_FullLumLimit()
    {
        double r = 3.0 / 255, g = 0.0, b = 0.0;
        SaturationBoost.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
        r.Should().BeGreaterThan(3.0 / 255, "Near-black red should get full saturation boost at low luminance");
    }

    [Fact]
    public void NearGray_LowSaturation()
    {
        double r = 128.0 / 255, g = 128.0 / 255, b = 130.0 / 255;
        SaturationBoost.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
        PipelineAssertions.AssertGrayPreserved(To8Bit(r, g, b));
    }

    [Fact]
    public void Cyan_TwoChannelMax()
    {
        double r = 0.0, g = 1.0, b = 1.0;
        SaturationBoost.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
    }

    [Fact]
    public void Magenta_TwoChannelMax()
    {
        double r = 1.0, g = 0.0, b = 1.0;
        SaturationBoost.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
        g.Should().BeLessOrEqualTo(Math.Min(r, b), "G must remain minimum for magenta hue");
    }

    [Fact]
    public void Yellow_TwoChannelMax()
    {
        double r = 1.0, g = 1.0, b = 0.0;
        SaturationBoost.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
    }

    [Theory]
    [InlineData(0x80, 0x80, 0x81)]
    [InlineData(0x40, 0x40, 0x41)]
    public void SingleUnitDelta_Preserved(byte r, byte g, byte b)
    {
        double ri = r / 255.0, gi = g / 255.0, bi = b / 255.0;
        SaturationBoost.Apply(ref ri, ref gi, ref bi, _profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(ri, gi, bi));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(192)]
    public void PureRed_AtVariousIntensities(byte v)
    {
        double r = v / 255.0, g = 0.0, b = 0.0;
        SaturationBoost.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
    }

    [Fact]
    public void HighLuminanceGray_LumLimitDisabled()
    {
        double r = 192.0 / 255, g = 192.0 / 255, b = 192.0 / 255;
        SaturationBoost.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertGrayPreserved(To8Bit(r, g, b));
    }

    [Fact]
    public void NegativeComponentRemoval_PreservesHue()
    {
        double r = 1.0, g = 0.0, b = 0.0;
        SaturationBoost.Apply(ref r, ref g, ref b, _profile);
        r.Should().BeGreaterOrEqualTo(g, "R must remain dominant after negative removal");
        r.Should().BeGreaterOrEqualTo(b, "R must remain dominant after negative removal");
    }
}