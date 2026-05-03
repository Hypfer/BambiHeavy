using BambiHeavy.Pipeline;

namespace BambiHeavy.PipelineTests.Pipeline;

public class TemporalIntegratorTests : IDisposable
{
    public void Dispose()
    {
        Config.Reset();
    }

    private TemporalIntegrator CreateIntegrator()
    {
        return new TemporalIntegrator();
    }

    private static (int r, int g, int b) To8Bit(double r, double g, double b)
    {
        return ((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
    }

    [Fact]
    public void FirstFrame_PassesThrough8Bit()
    {
        var integrator = CreateIntegrator();
        var profile = Config.GetProfile();
        double r = 0xA0 / 255.0, g = 0x60 / 255.0, b = 0x40 / 255.0;
        integrator.Apply(ref r, ref g, ref b, profile);
        PipelineAssertions.AssertNear8Bit(To8Bit(r, g, b), (0xA0, 0x60, 0x40));
    }

    [Fact]
    public void ConstantInput_Converges()
    {
        var integrator = CreateIntegrator();
        var profile = Config.GetProfile();

        double r = 0x80 / 255.0, g = 0x60 / 255.0, b = 0x40 / 255.0;
        integrator.Apply(ref r, ref g, ref b, profile);
        double firstR = r, firstG = g, firstB = b;

        for (var i = 0; i < 60; i++)
        {
            r = 0x80 / 255.0;
            g = 0x60 / 255.0;
            b = 0x40 / 255.0;
            integrator.Apply(ref r, ref g, ref b, profile);
        }

        PipelineAssertions.AssertNear8Bit(To8Bit(r, g, b), To8Bit(firstR, firstG, firstB));
    }

    [Fact]
    public void Black_RemainsBlack_AfterConvergence()
    {
        var integrator = CreateIntegrator();
        var profile = Config.GetProfile();
        double r = 0.0, g = 0.0, b = 0.0;
        for (var i = 0; i < 30; i++)
            integrator.Apply(ref r, ref g, ref b, profile);
        PipelineAssertions.AssertBlack(To8Bit(r, g, b));
    }

    [Fact]
    public void StepChange_SmoothTransitions()
    {
        var integrator = CreateIntegrator();
        var profile = Config.GetProfile();
        double r = 0.0, g = 0.0, b = 0.0;
        integrator.Apply(ref r, ref g, ref b, profile);
        var prevR = r;

        r = 1.0;
        g = 1.0;
        b = 1.0;
        integrator.Apply(ref r, ref g, ref b, profile);

        r.Should().BeGreaterThan(prevR, "Should start transitioning toward new value");
        r.Should().BeLessThan(1.0, "Should not jump to full value instantly");
    }

    [Fact]
    public void Output_Always8Bit()
    {
        var integrator = CreateIntegrator();
        var profile = Config.GetProfile();
        double[] values = { 0.0, 0x40 / 255.0, 0x80 / 255.0, 0xC0 / 255.0, 1.0 };
        foreach (var v in values)
        {
            double r = v, g = v, b = v;
            integrator.Apply(ref r, ref g, ref b, profile);
            PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
        }
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var integrator = CreateIntegrator();
        var profile = Config.GetProfile();
        double r = 1.0, g = 1.0, b = 1.0;
        integrator.Apply(ref r, ref g, ref b, profile);
        integrator.Reset();
        r = 0x80 / 255.0;
        g = 0x80 / 255.0;
        b = 0x80 / 255.0;
        integrator.Apply(ref r, ref g, ref b, profile);
        PipelineAssertions.AssertNear8Bit(To8Bit(r, g, b), (0x80, 0x80, 0x80));
    }

    [Fact]
    public void OverrideCoefficients_AllowFastResponse()
    {
        var integrator = CreateIntegrator();
        var profile = Config.GetProfile();
        double r = 0.0, g = 0.0, b = 0.0;
        integrator.Apply(ref r, ref g, ref b, profile);

        double slowR = 1.0, slowG = 1.0, slowB = 1.0;
        integrator.Apply(ref slowR, ref slowG, ref slowB, profile);

        var integrator2 = CreateIntegrator();
        r = 0.0;
        g = 0.0;
        b = 0.0;
        integrator2.Apply(ref r, ref g, ref b, profile);

        double fastR = 1.0, fastG = 1.0, fastB = 1.0;
        var fastAlpha = TemporalIntegrator.BlendAlpha(2); // n=2 → 50% blend
        integrator2.Apply(ref fastR, ref fastG, ref fastB, profile, fastAlpha, fastAlpha, fastAlpha, fastAlpha);

        fastR.Should().BeGreaterOrEqualTo(slowR, "Fast coefficient should respond faster");
    }

    [Fact]
    public void WhiteToBlack_DecayMonotonic()
    {
        var integrator = CreateIntegrator();
        var profile = Config.GetProfile();
        double r = 1.0, g = 1.0, b = 1.0;
        integrator.Apply(ref r, ref g, ref b, profile);

        var prev = r;
        for (var i = 0; i < 30; i++)
        {
            r = 0.0;
            g = 0.0;
            b = 0.0;
            integrator.Apply(ref r, ref g, ref b, profile);
            r.Should().BeLessOrEqualTo(prev, "Decay should be monotonically decreasing");
            prev = r;
        }
    }

    [Fact]
    public void OscillatingInput_BoundedOutput()
    {
        var integrator = CreateIntegrator();
        var profile = Config.GetProfile();
        double r = 0.0, g = 0.0, b = 0.0;
        integrator.Apply(ref r, ref g, ref b, profile);

        for (var i = 0; i < 200; i++)
        {
            var v = i % 2 == 0 ? 0.0 : 1.0;
            r = v;
            g = v;
            b = v;
            integrator.Apply(ref r, ref g, ref b, profile);
            PipelineAssertions.AssertValid8Bit(To8Bit(r, g, b));
        }
    }

    [Fact]
    public void ConstantNonGray_ConvergesToInput()
    {
        var integrator = CreateIntegrator();
        var profile = Config.GetProfile();

        double r = 0xCC / 255.0, g = 0x66 / 255.0, b = 0x33 / 255.0;
        for (var i = 0; i < 200; i++)
        {
            r = 0xCC / 255.0;
            g = 0x66 / 255.0;
            b = 0x33 / 255.0;
            integrator.Apply(ref r, ref g, ref b, profile);
        }

        PipelineAssertions.AssertNear8Bit(To8Bit(r, g, b), (0xCC, 0x66, 0x33), 10);
    }

    [Fact]
    public void DeadZone_SingleLsbStep()
    {
        var integrator = CreateIntegrator();
        var profile = Config.GetProfile();
        double r = 1.0 / 255, g = 1.0 / 255, b = 1.0 / 255;
        integrator.Apply(ref r, ref g, ref b, profile);

        for (var i = 0; i < 50; i++)
        {
            r = 2.0 / 255;
            g = 2.0 / 255;
            b = 2.0 / 255;
            integrator.Apply(ref r, ref g, ref b, profile);
        }

        var final = (int)Math.Round(r * 255);
        final.Should().BeInRange(0, 3, "Single LSB step should stay bounded near input range");
    }

    [Fact]
    public void FirstFrameFullWhite_NoDarkFlash()
    {
        var integrator = CreateIntegrator();
        var profile = Config.GetProfile();
        double r = 1.0, g = 1.0, b = 1.0;
        integrator.Apply(ref r, ref g, ref b, profile);
        PipelineAssertions.AssertNear8Bit(To8Bit(r, g, b), (0xFF, 0xFF, 0xFF), 1);
    }

    [Fact]
    public void MaxCoeff_InstantPassthrough()
    {
        var integrator = CreateIntegrator();
        var profile = Config.GetProfile();
        double r = 0.0, g = 0.0, b = 0.0;
        integrator.Apply(ref r, ref g, ref b, profile);

        r = 1.0;
        g = 1.0;
        b = 1.0;
        var coeff = TemporalIntegrator.BlendAlpha(1); // n=1 → instant passthrough
        integrator.Apply(ref r, ref g, ref b, profile, coeff, coeff, coeff, coeff);
        PipelineAssertions.AssertNear8Bit(To8Bit(r, g, b), (0xFF, 0xFF, 0xFF));
    }
}