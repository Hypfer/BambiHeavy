using BambiHeavy.Pipeline;

namespace BambiHeavy.PipelineTests.Pipeline;

public class AdaptiveIntegratorTests : IDisposable
{
    private readonly PipelineSettings _settings;

    public AdaptiveIntegratorTests()
    {
        _settings = new PipelineSettings();
        Config.Reset();
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
    public void StableInput_Converges()
    {
        var integrator = new AdaptiveIntegrator();
        integrator.UpdateSettings(_settings);
        var profile = Config.GetProfile();

        for (var i = 0; i < 120; i++)
        {
            double r = 0x80 / 255.0, g = 0x60 / 255.0, b = 0x40 / 255.0;
            integrator.Apply(ref r, ref g, ref b, profile, _settings);
        }

        double r1 = 0x80 / 255.0, g1 = 0x60 / 255.0, b1 = 0x40 / 255.0;
        integrator.Apply(ref r1, ref g1, ref b1, profile, _settings);

        double r2 = 0x80 / 255.0, g2 = 0x60 / 255.0, b2 = 0x40 / 255.0;
        integrator.Apply(ref r2, ref g2, ref b2, profile, _settings);

        PipelineAssertions.AssertNear8Bit(To8Bit(r1, g1, b1), To8Bit(r2, g2, b2), 3);
    }

    [Fact]
    public void TransientDetection_TriggersBypass()
    {
        var integrator = new AdaptiveIntegrator();
        integrator.UpdateSettings(_settings);
        var profile = Config.GetProfile();

        double r = 0x80 / 255.0, g = 0x80 / 255.0, b = 0x80 / 255.0;
        integrator.Apply(ref r, ref g, ref b, profile, _settings);
        var stableR = r;

        r = 1.0;
        g = 1.0;
        b = 1.0;
        integrator.Apply(ref r, ref g, ref b, profile, _settings);

        r.Should().BeGreaterThan(stableR, "Large step should be detected and respond");
    }

    [Fact]
    public void IntegrationDisabled_PassThrough()
    {
        var integrator = new AdaptiveIntegrator();
        var settings = new PipelineSettings { IntegrationEnabled = false };
        integrator.UpdateSettings(settings);
        var profile = Config.GetProfile();

        double r = 0xA0 / 255.0, g = 0x60 / 255.0, b = 0x40 / 255.0;
        integrator.Apply(ref r, ref g, ref b, profile, settings);
        PipelineAssertions.AssertNear8Bit(To8Bit(r, g, b), (0xA0, 0x60, 0x40));
    }

    [Fact]
    public void BypassDisabled_NormalIntegration()
    {
        var integrator = new AdaptiveIntegrator();
        var settings = new PipelineSettings { TransientBypassEnabled = false };
        integrator.UpdateSettings(settings);
        var profile = Config.GetProfile();

        double r = 0.0, g = 0.0, b = 0.0;
        for (var i = 0; i < 120; i++)
        {
            r = 0x80 / 255.0;
            g = 0x60 / 255.0;
            b = 0x40 / 255.0;
            integrator.Apply(ref r, ref g, ref b, profile, settings);
        }

        r.Should().BeInRange(90.0 / 255, 150.0 / 255, "R should converge toward 128");
        g.Should().BeInRange(80.0 / 255, 110.0 / 255, "G should converge toward 96");
        b.Should().BeInRange(40.0 / 255, 80.0 / 255, "B should converge toward 64");
    }

    [Fact]
    public void Output_Always8Bit()
    {
        var integrator = new AdaptiveIntegrator();
        integrator.UpdateSettings(_settings);
        var profile = Config.GetProfile();
        double[] values = { 0.0, 0x40 / 255.0, 0x80 / 255.0, 0xC0 / 255.0, 1.0 };
        foreach (var v in values)
        {
            double r = v, g = v, b = v;
            integrator.Apply(ref r, ref g, ref b, profile, _settings);
            PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
        }
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var integrator = new AdaptiveIntegrator();
        integrator.UpdateSettings(_settings);
        var profile = Config.GetProfile();
        double r = 1.0, g = 1.0, b = 1.0;
        integrator.Apply(ref r, ref g, ref b, profile, _settings);
        integrator.Reset();
        r = 0x80 / 255.0;
        g = 0x80 / 255.0;
        b = 0x80 / 255.0;
        integrator.Apply(ref r, ref g, ref b, profile, _settings);
        PipelineAssertions.AssertNear8Bit(To8Bit(r, g, b), (0x80, 0x80, 0x80));
    }

    [Fact]
    public void BypassStats_Tracked()
    {
        var integrator = new AdaptiveIntegrator();
        integrator.UpdateSettings(_settings);
        var profile = Config.GetProfile();
        for (var i = 0; i < 10; i++)
        {
            double r = 0x80 / 255.0, g = 0x80 / 255.0, b = 0x80 / 255.0;
            integrator.Apply(ref r, ref g, ref b, profile, _settings);
        }

        var stats = integrator.GetBypassStats();
        stats.totalFrames.Should().Be(10);
    }

    [Fact]
    public void NoisyInput_PermaBypass()
    {
        var integrator = new AdaptiveIntegrator();
        var settings = new PipelineSettings { SettleThreshold = 15 };
        integrator.UpdateSettings(settings);
        var profile = Config.GetProfile();

        double r = 0x80 / 255.0, g = 0x80 / 255.0, b = 0x80 / 255.0;
        integrator.Apply(ref r, ref g, ref b, profile, settings);

        for (var i = 0; i < 50; i++)
        {
            var noise = (i % 2 == 0 ? 1 : -1) * (30.0 / 255);
            r = 0x80 / 255.0 + noise;
            g = 0x80 / 255.0 + noise;
            b = 0x80 / 255.0 + noise;
            integrator.Apply(ref r, ref g, ref b, profile, settings);
        }

        var stats = integrator.GetBypassStats();
        stats.bypassFrames.Should().BeGreaterThan(0, "Noisy input should keep integrator in bypass mode");
    }

    [Fact]
    public void RecoveryStateMachine_CorrectTransition()
    {
        var integrator = new AdaptiveIntegrator();
        integrator.UpdateSettings(_settings);
        var profile = Config.GetProfile();

        double r = 0x80 / 255.0, g = 0x80 / 255.0, b = 0x80 / 255.0;
        for (var i = 0; i < 10; i++)
        {
            r = 0x80 / 255.0;
            g = 0x80 / 255.0;
            b = 0x80 / 255.0;
            integrator.Apply(ref r, ref g, ref b, profile, _settings);
        }

        r = 1.0;
        g = 1.0;
        b = 1.0;
        integrator.Apply(ref r, ref g, ref b, profile, _settings);

        for (var i = 0; i < 10; i++)
        {
            r = 1.0;
            g = 1.0;
            b = 1.0;
            integrator.Apply(ref r, ref g, ref b, profile, _settings);
        }

        var stats = integrator.GetBypassStats();
        stats.bypassFrames.Should().BeGreaterThan(0, "Should have entered bypass on transient");
    }

    [Fact]
    public void IntegrationDisabled_StatsZero()
    {
        var integrator = new AdaptiveIntegrator();
        var settings = new PipelineSettings { IntegrationEnabled = false };
        integrator.UpdateSettings(settings);
        var profile = Config.GetProfile();

        for (var i = 0; i < 10; i++)
        {
            double r = 0x80 / 255.0, g = 0x80 / 255.0, b = 0x80 / 255.0;
            integrator.Apply(ref r, ref g, ref b, profile, settings);
        }

        var stats = integrator.GetBypassStats();
        stats.totalFrames.Should().Be(0, "Disabled integration should not count frames");
    }

    [Fact]
    public void GamingConvergesFasterThanStandard()
    {
        Config.ActiveStyle = BambiStyle.Gaming;
        var gamingIntegrator = new AdaptiveIntegrator();
        gamingIntegrator.UpdateSettings(_settings);
        var gamingProfile = Config.GetProfile();

        Config.ActiveStyle = BambiStyle.Standard;
        var stdIntegrator = new AdaptiveIntegrator();
        stdIntegrator.UpdateSettings(_settings);
        var stdProfile = Config.GetProfile();

        double gr = 0xCC / 255.0, gg = 0x66 / 255.0, gb = 0x33 / 255.0;
        double sr = 0xCC / 255.0, sg = 0x66 / 255.0, sb = 0x33 / 255.0;
        for (var i = 0; i < 60; i++)
        {
            gr = 0xCC / 255.0;
            gg = 0x66 / 255.0;
            gb = 0x33 / 255.0;
            gamingIntegrator.Apply(ref gr, ref gg, ref gb, gamingProfile, _settings);

            sr = 0xCC / 255.0;
            sg = 0x66 / 255.0;
            sb = 0x33 / 255.0;
            stdIntegrator.Apply(ref sr, ref sg, ref sb, stdProfile, _settings);
        }

        var gamingDist = Math.Abs((int)Math.Round(gr * 255) - 0xCC) + Math.Abs((int)Math.Round(gg * 255) - 0x66) +
                         Math.Abs((int)Math.Round(gb * 255) - 0x33);
        var stdDist = Math.Abs((int)Math.Round(sr * 255) - 0xCC) + Math.Abs((int)Math.Round(sg * 255) - 0x66) +
                      Math.Abs((int)Math.Round(sb * 255) - 0x33);
        gamingDist.Should()
            .BeLessOrEqualTo(stdDist, "Gaming should converge faster than Standard (higher coeff indices)");
    }

    [Fact]
    public void GrayRoundTrip_NoColorCast()
    {
        var integrator = new AdaptiveIntegrator();
        integrator.UpdateSettings(_settings);
        var profile = Config.GetProfile();

        double r = 0x80 / 255.0, g = 0x80 / 255.0, b = 0x80 / 255.0;
        for (var i = 0; i < 100; i++)
        {
            r = 0x80 / 255.0;
            g = 0x80 / 255.0;
            b = 0x80 / 255.0;
            integrator.Apply(ref r, ref g, ref b, profile, _settings);
        }

        Math.Abs((int)Math.Round(r * 255) - (int)Math.Round(g * 255)).Should()
            .BeLessOrEqualTo(1, "Gray should not get color cast from YUV round-trip");
        Math.Abs((int)Math.Round(g * 255) - (int)Math.Round(b * 255)).Should().BeLessOrEqualTo(1);
    }
}