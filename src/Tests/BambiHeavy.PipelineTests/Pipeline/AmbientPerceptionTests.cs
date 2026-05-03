using BambiHeavy.Pipeline;

namespace BambiHeavy.PipelineTests.Pipeline;

public class AmbientPerceptionTests
{
    [Fact]
    public void Black_PassesThrough()
    {
        var result = AmbientPerception.Apply(0.0, 0.0, 0.0);
        result.Should().Be((0.0, 0.0, 0.0));
    }

    [Fact]
    public void White_CappedAt75Percent()
    {
        var result = AmbientPerception.Apply(1.0, 1.0, 1.0);
        // White: desat blends to 0 (maxCh=1, factor=1, gray=1, no change from desat)
        // Then white cap: whiteness=1.0, scale=0.75, 1.0*0.75=0.75
        result.r.Should().BeApproximately(0.75, 0.001);
        result.g.Should().BeApproximately(0.75, 0.001);
        result.b.Should().BeApproximately(0.75, 0.001);
    }

    [Fact]
    public void SaturatedRed_NoWhiteCap()
    {
        var result = AmbientPerception.Apply(1.0, 0.0, 0.0);
        // Saturated: whiteness=0/1=0, scale=1.0 (no cap)
        // Above desat threshold, no desat
        result.Should().Be((1.0, 0.0, 0.0));
    }

    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    public void SaturatedColors_PassThrough(byte r, byte g, byte b)
    {
        var result = AmbientPerception.Apply(r / 255.0, g / 255.0, b / 255.0);
        result.Should().Be((r / 255.0, g / 255.0, b / 255.0),
            "Saturated colors above threshold should pass through unchanged");
    }

    [Fact]
    public void NearWhite_ReducedBrightness()
    {
        var result = AmbientPerception.Apply(1.0, 254.0 / 255, 253.0 / 255);
        // whiteness = 253/255 / 1.0 = 0.992, scale = 1 - 0.25*0.992 = 0.752
        // 1.0 * 0.752 = 0.752
        result.r.Should().BeLessThan(1.0, "Near-white should be dimmed");
        result.r.Should().BeGreaterThan(0.7, "But not dimmed too much");
    }

    [Fact]
    public void DimRed_Desaturated()
    {
        var result = AmbientPerception.Apply(5.0 / 255, 0.0, 0.0);
        // maxCh=5/255 < threshold (0.05), so desat applies
        // factor = (5/255) / 0.05 = 0.392
        // gray = (5/255 + 0 + 0) / 3 = 0.00654
        // r = 0.00654 + (5/255 - 0.00654) * 0.392 = 0.00654 + 0.0051 = 0.0116
        // g = 0.00654 + (0 - 0.00654) * 0.392 = 0.00654 - 0.00256 = 0.00398
        // b = same as g
        // Then white cap: maxCh≈0.0116, minCh≈0.00398, whiteness=0.00398/0.0116=0.343, scale=1-0.25*0.343=0.914
        result.r.Should().BeLessOrEqualTo(5.0 / 255, "Dim red should not increase");
        result.g.Should().BeGreaterOrEqualTo(0.0, "Green should be near zero");
        result.b.Should().BeGreaterOrEqualTo(0.0, "Blue should be near zero");
        // After desat, the color should be closer to gray than the original
        var spread = Math.Max(result.r, Math.Max(result.g, result.b)) -
                     Math.Min(result.r, Math.Min(result.g, result.b));
        spread.Should().BeLessThan(5.0 / 255, "Dim color should be desaturated (smaller spread)");
    }

    [Fact]
    public void MidtoneGray_PassesThroughDesat()
    {
        var result = AmbientPerception.Apply(128.0 / 255, 128.0 / 255, 128.0 / 255);
        // Above desat threshold, no desat
        // White cap: whiteness=1.0, scale=0.75, 128/255*0.75 = 0.3765
        var expected = 128.0 / 255 * 0.75;
        result.r.Should().BeApproximately(expected, 0.001);
        result.g.Should().BeApproximately(expected, 0.001);
        result.b.Should().BeApproximately(expected, 0.001);
    }

    [Fact]
    public void MonotonicBrightness_GrayRamp()
    {
        // Verify that increasing gray input produces non-decreasing output
        double prev = 0;
        for (var i = 0; i <= 255; i++)
        {
            var v = i / 255.0;
            var result = AmbientPerception.Apply(v, v, v);
            result.r.Should().BeGreaterOrEqualTo(prev,
                $"Gray brightness should be monotonic at {i}");
            prev = result.r;
        }
    }

    [Fact]
    public void Output_InValidRange()
    {
        var testCases = new (double r, double g, double b)[]
        {
            (0, 0, 0), (1, 1, 1), (1, 0, 0), (0, 1, 0), (0, 0, 1),
            (128.0 / 255, 128.0 / 255, 128.0 / 255), (64.0 / 255, 128.0 / 255, 192.0 / 255),
            (200.0 / 255, 100.0 / 255, 50.0 / 255), (10.0 / 255, 5.0 / 255, 0), (3.0 / 255, 3.0 / 255, 3.0 / 255)
        };
        foreach (var (r, g, b) in testCases)
        {
            var result = AmbientPerception.Apply(r, g, b);
            result.r.Should().BeInRange(0, 1, $"R valid for ({r},{g},{b})");
            result.g.Should().BeInRange(0, 1, $"G valid for ({r},{g},{b})");
            result.b.Should().BeInRange(0, 1, $"B valid for ({r},{g},{b})");
        }
    }

    [Fact]
    public void Deterministic()
    {
        var out1 = AmbientPerception.Apply(128.0 / 255, 64.0 / 255, 192.0 / 255);
        var out2 = AmbientPerception.Apply(128.0 / 255, 64.0 / 255, 192.0 / 255);
        out1.Should().Be(out2);
    }

    [Fact]
    public void WhiteCap_InterpolatesLinearly()
    {
        // Saturated → no cap, near-white → near 75%
        var saturated = AmbientPerception.Apply(1.0, 0.0, 0.0);
        var mid = AmbientPerception.Apply(1.0, 128.0 / 255, 0.0);
        var nearWhite = AmbientPerception.Apply(1.0, 254.0 / 255, 253.0 / 255);
        var white = AmbientPerception.Apply(1.0, 1.0, 1.0);

        var maxSaturated = Math.Max(saturated.r, Math.Max(saturated.g, saturated.b));
        var maxMid = Math.Max(mid.r, Math.Max(mid.g, mid.b));
        var maxNearWhite = Math.Max(nearWhite.r, Math.Max(nearWhite.g, nearWhite.b));
        var maxWhite = Math.Max(white.r, Math.Max(white.g, white.b));

        maxSaturated.Should().BeGreaterOrEqualTo(maxMid);
        maxMid.Should().BeGreaterOrEqualTo(maxNearWhite);
        maxNearWhite.Should().BeGreaterOrEqualTo(maxWhite);
    }
}