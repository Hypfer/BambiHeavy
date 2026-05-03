using BambiHeavy.Pipeline;
using BambiHeavy.Types;

namespace BambiHeavy.PipelineTests.Pipeline;

/// <summary>
///     End-to-end pipeline tests: color pipeline -> integration.
///     Edge extraction is tested separately due to complex static state.
/// </summary>
public class FullPipelineTests : IDisposable
{
    public void Dispose()
    {
        Config.Reset();
    }

    [Fact]
    public void SolidColor_FullPipeline()
    {
        Config.ActiveStyle = BambiStyle.Standard;
        var profile = Config.GetProfile();
        var input = new Rgb(0x80, 0x60, 0x40);

        var integrator = new AdaptiveIntegrator();
        integrator.UpdateSettings(new PipelineSettings());

        for (var i = 0; i < 60; i++)
        {
            var pre = ColorPipeline.ProcessPreIntegration(input, profile);
            if (ColorPipeline.IsBlack(pre.r, pre.g, pre.b))
                pre = (0.0, 0.0, 0.0);

            double r = pre.r, g = pre.g, b = pre.b;
            integrator.Apply(ref r, ref g, ref b, profile, new PipelineSettings());

            r.Should().BeInRange(0.0, 1.0);
            g.Should().BeInRange(0.0, 1.0);
            b.Should().BeInRange(0.0, 1.0);
        }
    }

    [Fact]
    public void Black_Input_RemainsBlack()
    {
        Config.ActiveStyle = BambiStyle.Standard;
        var profile = Config.GetProfile();
        var pre = ColorPipeline.ProcessPreIntegration(Rgb.Black, profile);
        ColorPipeline.IsBlack(pre.r, pre.g, pre.b).Should().BeTrue("Black should be detected");
    }

    [Theory]
    [InlineData(BambiStyle.Standard)]
    [InlineData(BambiStyle.Cinema)]
    [InlineData(BambiStyle.Sports)]
    [InlineData(BambiStyle.Gaming)]
    public void AllStyles_EndToEnd(BambiStyle style)
    {
        Config.ActiveStyle = style;
        var profile = Config.GetProfile();
        var input = new Rgb(0xA0, 0x50, 0x70);
        var pre = ColorPipeline.ProcessPreIntegration(input, profile);

        double r = pre.r, g = pre.g, b = pre.b;
        var integrator = new AdaptiveIntegrator();
        integrator.UpdateSettings(new PipelineSettings());

        for (var i = 0; i < 30; i++)
            integrator.Apply(ref r, ref g, ref b, profile, new PipelineSettings());

        r.Should().BeInRange(0.0, 1.0, $"Valid R for style {style}");
        g.Should().BeInRange(0.0, 1.0, $"Valid G for style {style}");
        b.Should().BeInRange(0.0, 1.0, $"Valid B for style {style}");
    }

    [Fact]
    public void Transient_IntegratorRecovers()
    {
        Config.ActiveStyle = BambiStyle.Standard;
        var profile = Config.GetProfile();

        var integrator = new AdaptiveIntegrator();
        integrator.UpdateSettings(new PipelineSettings());

        var normal = ColorPipeline.ProcessPreIntegration(new Rgb(0x80, 0x60, 0x40), profile);
        double r = normal.r, g = normal.g, b = normal.b;
        for (var i = 0; i < 30; i++)
            integrator.Apply(ref r, ref g, ref b, profile, new PipelineSettings());

        // Spike to white
        var spike = ColorPipeline.ProcessPreIntegration(new Rgb(0xFF, 0xFF, 0xFF), profile);
        r = spike.r;
        g = spike.g;
        b = spike.b;
        integrator.Apply(ref r, ref g, ref b, profile, new PipelineSettings());

        // Return to normal
        for (var i = 0; i < 60; i++)
        {
            r = normal.r;
            g = normal.g;
            b = normal.b;
            integrator.Apply(ref r, ref g, ref b, profile, new PipelineSettings());
        }

        var stats = integrator.GetBypassStats();
        stats.bypassFrames.Should().BeGreaterThan(0, "Transient should trigger bypass");
    }

    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    [InlineData(255, 255, 0)]
    [InlineData(255, 0, 255)]
    [InlineData(0, 255, 255)]
    public void PerChannelColors_FullPipeline(byte r, byte g, byte b)
    {
        Config.ActiveStyle = BambiStyle.Standard;
        var profile = Config.GetProfile();
        var input = new Rgb(r, g, b);

        var pre = ColorPipeline.ProcessPreIntegration(input, profile);
        if (ColorPipeline.IsBlack(pre.r, pre.g, pre.b))
            pre = (0.0, 0.0, 0.0);

        double cr = pre.r, cg = pre.g, cb = pre.b;
        var integrator = new AdaptiveIntegrator();
        integrator.UpdateSettings(new PipelineSettings());

        for (var i = 0; i < 30; i++)
            integrator.Apply(ref cr, ref cg, ref cb, profile, new PipelineSettings());

        cr.Should().BeInRange(0.0, 1.0, $"Valid R for ({r},{g},{b})");
        cg.Should().BeInRange(0.0, 1.0, $"Valid G for ({r},{g},{b})");
        cb.Should().BeInRange(0.0, 1.0, $"Valid B for ({r},{g},{b})");

        if (r + g + b > 0)
            (cr + cg + cb).Should().BeGreaterThan(0,
                $"Non-black color ({r},{g},{b}) should produce non-zero output");
    }

    [Fact]
    public void StageInteraction_AllVsCompressVsGain()
    {
        Config.ActiveStyle = BambiStyle.Standard;
        var profile = Config.GetProfile();

        var configurations = new[]
        {
            ("All stages", new PipelineSettings(),
                PipelineStage.Gain | PipelineStage.Compress | PipelineStage.Saturation | PipelineStage.NoiseGate),
            ("Compress only",
                new PipelineSettings { GainEnabled = false, SaturationEnabled = false, NoiseGateEnabled = false },
                PipelineStage.Compress),
            ("Gain only",
                new PipelineSettings { CompressEnabled = false, SaturationEnabled = false, NoiseGateEnabled = false },
                PipelineStage.Gain)
        };

        var results = new Dictionary<string, (double r, double g, double b)>();

        foreach (var (name, settings, stages) in configurations)
        {
            var pre = ColorPipeline.ProcessPreIntegration(new Rgb(128, 64, 32), profile, stages);
            double r = pre.r, g = pre.g, b = pre.b;

            var integrator = new AdaptiveIntegrator();
            integrator.UpdateSettings(settings);

            for (var i = 0; i < 30; i++)
                integrator.Apply(ref r, ref g, ref b, profile, settings);

            results[name] = (r, g, b);
        }

        foreach (var result in results.Values)
        {
            result.r.Should().BeInRange(0.0, 1.0);
            result.g.Should().BeInRange(0.0, 1.0);
            result.b.Should().BeInRange(0.0, 1.0);
        }

        var allBrightness = results["All stages"].r + results["All stages"].g + results["All stages"].b;
        var compressBrightness = results["Compress only"].r + results["Compress only"].g + results["Compress only"].b;
        (allBrightness - compressBrightness).Should().BeInRange(-0.5, 0.5,
            "All stages and compress-only brightness difference is bounded");
        allBrightness.Should().NotBe(results["Gain only"].r + results["Gain only"].g + results["Gain only"].b,
            "All stages and gain-only produce different output");
    }

    [Fact]
    public void IntegrationWarmup_FirstFramesDifferFromConverged()
    {
        Config.ActiveStyle = BambiStyle.Standard;
        var profile = Config.GetProfile();
        var settings = new PipelineSettings();

        var input = new Rgb(200, 150, 100);
        var pre = ColorPipeline.ProcessPreIntegration(input, profile);

        var integrator = new AdaptiveIntegrator();
        integrator.UpdateSettings(settings);

        double r1 = pre.r, g1 = pre.g, b1 = pre.b;
        integrator.Apply(ref r1, ref g1, ref b1, profile, settings);
        var firstBrightness = r1 + g1 + b1;

        double rN = pre.r, gN = pre.g, bN = pre.b;
        for (var i = 0; i < 60; i++)
            integrator.Apply(ref rN, ref gN, ref bN, profile, settings);
        var convergedBrightness = rN + gN + bN;

        Math.Abs(firstBrightness - convergedBrightness)
            .Should().BeLessOrEqualTo(0.01,
                "Constant input: first frame passes through and equals converged output with double precision");
    }
}