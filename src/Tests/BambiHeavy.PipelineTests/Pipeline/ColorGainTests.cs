using BambiHeavy.Pipeline;

namespace BambiHeavy.PipelineTests.Pipeline;

public class ColorGainTests : IDisposable
{
    private readonly TuningProfile _profile;

    public ColorGainTests()
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
        ColorGain.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertBlack(To8Bit(r, g, b));
    }

    [Fact]
    public void MidGray_IsBoosted()
    {
        double r = 0x80 / 255.0, g = 0x80 / 255.0, b = 0x80 / 255.0;
        ColorGain.Apply(ref r, ref g, ref b, _profile);
        r.Should().BeGreaterThan(0x80 / 255.0, "Gain should boost mid-gray");
    }

    [Theory]
    [InlineData(0x80)]
    [InlineData(0x40)]
    public void Gray_Preserved(byte v)
    {
        double r = v / 255.0, g = v / 255.0, b = v / 255.0;
        ColorGain.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertGrayPreserved(To8Bit(r, g, b), 8);
    }

    [Fact]
    public void SaturatedRed_HuePreserved()
    {
        double r = 1.0, g = 0.0, b = 0.0;
        ColorGain.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertHuePreserved(To8Bit(r, g, b), 'R');
    }

    [Theory]
    [InlineData(0xFF, 0x00, 0x00)]
    [InlineData(0x00, 0xFF, 0x00)]
    [InlineData(0x00, 0x00, 0xFF)]
    [InlineData(0xFF, 0x80, 0x00)]
    [InlineData(0x00, 0x80, 0xFF)]
    public void Output_InValidRange(byte r, byte g, byte b)
    {
        double ri = r / 255.0, gi = g / 255.0, bi = b / 255.0;
        ColorGain.Apply(ref ri, ref gi, ref bi, _profile);
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
        double r = 0x80 / 255.0, g = 0x60 / 255.0, b = 0x40 / 255.0;
        ColorGain.Apply(ref r, ref g, ref b, profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
    }

    [Fact]
    public void NearBlackGray_ValidOutput()
    {
        double r = 1.0 / 255, g = 1.0 / 255, b = 1.0 / 255;
        ColorGain.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
        PipelineAssertions.AssertGrayPreserved(To8Bit(r, g, b));
    }

    [Fact]
    public void NearWhiteGray_ClampedToWhite()
    {
        double r = 254.0 / 255, g = 254.0 / 255, b = 254.0 / 255;
        ColorGain.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
        PipelineAssertions.AssertGrayPreserved(To8Bit(r, g, b), 10);
    }

    [Fact]
    public void Yellow_ClampedProportionally()
    {
        double r = 1.0, g = 1.0, b = 0.0;
        ColorGain.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
    }

    [Theory]
    [InlineData(0x7F)]
    [InlineData(0x81)]
    public void MidGrayContinuity_EitherSide(byte v)
    {
        double r = v / 255.0, g = v / 255.0, b = v / 255.0;
        ColorGain.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertGrayPreserved(To8Bit(r, g, b), 8);
    }

    [Fact]
    public void MinimalRed_HuePreserved()
    {
        double r = 1.0 / 255, g = 0.0, b = 0.0;
        ColorGain.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
        PipelineAssertions.AssertHuePreserved(To8Bit(r, g, b), 'R');
    }

    [Fact]
    public void Magenta_TwoChannelSaturated()
    {
        double r = 1.0, g = 0.0, b = 1.0;
        ColorGain.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
    }

    [Fact]
    public void Cyan_TwoChannelSaturated()
    {
        double r = 0.0, g = 1.0, b = 1.0;
        ColorGain.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
    }

    [Theory]
    [InlineData(0x20)]
    [InlineData(0x40)]
    [InlineData(0x60)]
    [InlineData(0x80)]
    [InlineData(0xA0)]
    [InlineData(0xC0)]
    [InlineData(0xE0)]
    public void GraySweep_Preserved(byte v)
    {
        double r = v / 255.0, g = v / 255.0, b = v / 255.0;
        ColorGain.Apply(ref r, ref g, ref b, _profile);
        PipelineAssertions.AssertGrayPreserved(To8Bit(r, g, b), 10);
    }
}