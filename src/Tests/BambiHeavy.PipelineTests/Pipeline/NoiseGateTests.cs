using BambiHeavy.Pipeline;

namespace BambiHeavy.PipelineTests.Pipeline;

public class NoiseGateTests : IDisposable
{
    public void Dispose()
    {
        Config.Reset();
    }

    private static (int r, int g, int b) To8Bit(double r, double g, double b)
    {
        return ((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
    }

    [Fact]
    public void BrightColor_PassesThrough()
    {
        var profile = Config.GetProfile();
        double r = 0xC0 / 255.0, g = 0x80 / 255.0, b = 0x40 / 255.0;
        NoiseGate.Apply(ref r, ref g, ref b, profile);
        PipelineAssertions.AssertNear8Bit(To8Bit(r, g, b), (0xC0, 0x80, 0x40));
    }

    [Theory]
    [InlineData(0x01, 0x00, 0x00)]
    [InlineData(0x01, 0x01, 0x01)]
    [InlineData(0x03, 0x03, 0x03)]
    [InlineData(0x05, 0x05, 0x05)]
    [InlineData(0x06, 0x06, 0x06)]
    [InlineData(0x07, 0x07, 0x07)]
    [InlineData(0x0A, 0x0A, 0x0A)]
    public void NearBlack_Desaturated(byte r, byte g, byte b)
    {
        var profile = Config.GetProfile();
        double ri = r / 255.0, gi = g / 255.0, bi = b / 255.0;
        NoiseGate.Apply(ref ri, ref gi, ref bi, profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(ri, gi, bi));

        var maxOut = Math.Max(ri, Math.Max(gi, bi));
        var maxIn = Math.Max(r / 255.0, Math.Max(g / 255.0, b / 255.0));
        maxOut.Should().BeLessOrEqualTo(maxIn, "Desaturation should not increase brightness");
    }

    [Fact]
    public void VeryDarkColor_IsScaledDown()
    {
        var profile = Config.GetProfile();
        double r = 0x08 / 255.0, g = 0x04 / 255.0, b = 0x0C / 255.0;
        NoiseGate.Apply(ref r, ref g, ref b, profile);
        var maxOut = Math.Max(r, Math.Max(g, b));
        maxOut.Should().BeLessThan(0x0C / 255.0, "Dark colors should be scaled down");
    }

    [Fact]
    public void Black_RemainsBlack()
    {
        var profile = Config.GetProfile();
        double r = 0.0, g = 0.0, b = 0.0;
        NoiseGate.Apply(ref r, ref g, ref b, profile);
        PipelineAssertions.AssertBlack(To8Bit(r, g, b));
    }

    [Fact]
    public void AtThreshold_PassesThrough()
    {
        var profile = Config.GetProfile();
        double r = profile.NoiseGateThreshold / 100.0, g = 0.0, b = 0.0;
        NoiseGate.Apply(ref r, ref g, ref b, profile);
        var roundedR = (int)Math.Round(r * 255);
        var expectedR = (int)Math.Round(profile.NoiseGateThreshold / 100.0 * 255);
        roundedR.Should().BeInRange(
            Math.Max(0, expectedR - 3),
            Math.Min(255, expectedR + 3));
    }

    [Fact]
    public void Scaling_IsQuadratic()
    {
        var profile = Config.GetProfile();
        var threshold = profile.NoiseGateThreshold / 100.0;
        var halfThreshold = threshold / 2;

        double r1 = halfThreshold, g1 = 0.0, b1 = 0.0;
        NoiseGate.Apply(ref r1, ref g1, ref b1, profile);

        double r2 = threshold, g2 = 0.0, b2 = 0.0;
        NoiseGate.Apply(ref r2, ref g2, ref b2, profile);

        var ratio = r2 > 0 ? r1 / r2 : 0;
        ratio.Should().BeApproximately(0.25, 0.05,
            "Desaturation scale is quadratic: (max/threshold)^2, so half threshold gives 1/4 output");
    }

    [Fact]
    public void HigherThreshold_MoreAggressive()
    {
        Config.ActiveStyle = BambiStyle.Cinema;
        var cinemaProfile = Config.GetProfile();

        Config.ActiveStyle = BambiStyle.Standard;
        var standardProfile = Config.GetProfile();

        cinemaProfile.NoiseGateThreshold.Should().BeGreaterThan(standardProfile.NoiseGateThreshold);

        double rC = 0x30 / 255.0, gC = 0x20 / 255.0, bC = 0x10 / 255.0;
        NoiseGate.Apply(ref rC, ref gC, ref bC, cinemaProfile);

        double rS = 0x30 / 255.0, gS = 0x20 / 255.0, bS = 0x10 / 255.0;
        NoiseGate.Apply(ref rS, ref gS, ref bS, standardProfile);

        Math.Max(rC, Math.Max(gC, bC))
            .Should().BeLessOrEqualTo(Math.Max(rS, Math.Max(gS, bS)),
                "Cinema should desaturate more at this brightness");
    }

    [Fact]
    public void Sports_HighThreshold_Aggressive()
    {
        Config.ActiveStyle = BambiStyle.Sports;
        var profile = Config.GetProfile();

        double r = 64.0 / 255, g = 32.0 / 255, b = 16.0 / 255;
        NoiseGate.Apply(ref r, ref g, ref b, profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
        var maxOut = Math.Max(r, Math.Max(g, b));
        maxOut.Should().BeLessThan(64.0 / 255, "Sports threshold(112) should catch mid-range colors");
    }

    [Fact]
    public void Gaming_SameThresholdAsCinema()
    {
        Config.ActiveStyle = BambiStyle.Gaming;
        var gaming = Config.GetProfile();

        Config.ActiveStyle = BambiStyle.Cinema;
        var cinema = Config.GetProfile();

        gaming.NoiseGateThreshold.Should().Be(cinema.NoiseGateThreshold, "Gaming and Cinema share desat threshold");

        double rG = 64.0 / 255, gG = 0.0, bG = 0.0;
        double rC = 64.0 / 255, gC = 0.0, bC = 0.0;
        NoiseGate.Apply(ref rG, ref gG, ref bG, gaming);
        NoiseGate.Apply(ref rC, ref gC, ref bC, cinema);
        (rG, gG, bG).Should().Be((rC, gC, bC), "Same threshold produces same desaturation");
    }

    [Fact]
    public void JustBelowThreshold_Standard_Discontinuity()
    {
        var profile = Config.GetProfile();
        var threshold = profile.NoiseGateThreshold / 100.0;

        double r = threshold - 0.004, g = 0.0, b = 0.0;
        NoiseGate.Apply(ref r, ref g, ref b, profile);
        var outBelow = r;

        double r2 = threshold, g2 = 0.0, b2 = 0.0;
        NoiseGate.Apply(ref r2, ref g2, ref b2, profile);
        var outAt = r2;

        outBelow.Should().BeLessThan(outAt, "Just below threshold should be lower than at threshold");
    }

    [Fact]
    public void JustAboveThreshold_PassesThrough()
    {
        var profile = Config.GetProfile();
        var threshold = profile.NoiseGateThreshold / 100.0;

        double r = threshold + 0.004, g = threshold / 2 + 0.004, b = 0.0;
        NoiseGate.Apply(ref r, ref g, ref b, profile);
        PipelineAssertions.AssertNear8Bit(To8Bit(r, g, b),
            ((int)Math.Round((threshold + 0.004) * 255), (int)Math.Round((threshold / 2 + 0.004) * 255), 0), 1);
    }

    [Fact]
    public void Chromatic_PreservesRatio()
    {
        var profile = Config.GetProfile();
        double r1 = 12.0 / 255, g1 = 4.0 / 255, b1 = 8.0 / 255;
        NoiseGate.Apply(ref r1, ref g1, ref b1, profile);

        var ratioIn = 12.0 / 4.0;
        var ratioOut = g1 > 0 ? r1 / g1 : double.MaxValue;
        ratioOut.Should().BeApproximately(ratioIn, 0.1, "Channel ratios should be preserved through desat scaling");
    }

    [Fact]
    public void Monochromatic_ScalesUniformly()
    {
        var profile = Config.GetProfile();
        double r = 8.0 / 255, g = 8.0 / 255, b = 8.0 / 255;
        NoiseGate.Apply(ref r, ref g, ref b, profile);
        PipelineAssertions.AssertGrayPreserved(To8Bit(r, g, b), 2);
        r.Should().BeLessThan(8.0 / 255, "Dark gray should be scaled down by quadratic formula");
    }

    [Fact]
    public void PureColor_BelowThreshold_ZerosPreserved()
    {
        var profile = Config.GetProfile();
        double r = 15.0 / 255, g = 0.0, b = 0.0;
        NoiseGate.Apply(ref r, ref g, ref b, profile);
        g.Should().Be(0);
        b.Should().Be(0);
        r.Should().BeLessThan(15.0 / 255);
    }
}