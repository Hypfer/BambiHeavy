using BambiHeavy.Pipeline;
using BambiHeavy.Types;

namespace BambiHeavy.PipelineTests.Pipeline;

public class ColorPipelineTests : IDisposable
{
    public void Dispose()
    {
        Config.Reset();
    }

    private static (int r, int g, int b) To8Bit(double r, double g, double b)
    {
        return ((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
    }

    [Theory]
    [InlineData(0x00, 0x00, 0x00)]
    [InlineData(0xFF, 0xFF, 0xFF)]
    [InlineData(0xFF, 0x00, 0x00)]
    [InlineData(0x00, 0xFF, 0x00)]
    [InlineData(0x00, 0x00, 0xFF)]
    [InlineData(0x80, 0x80, 0x80)]
    [InlineData(0x40, 0x80, 0xC0)]
    [InlineData(0xA0, 0x50, 0x70)]
    [InlineData(0x01, 0x00, 0x00)]
    [InlineData(0x10, 0x10, 0x10)]
    [InlineData(0x40, 0x40, 0x40)]
    [InlineData(0xC0, 0xC0, 0xC0)]
    [InlineData(0xC8, 0x50, 0x14)]
    [InlineData(0xB4, 0x28, 0x1E)]
    [InlineData(0x1E, 0x64, 0x1E)]
    [InlineData(0x14, 0x14, 0xB4)]
    [InlineData(0xFF, 0xFF, 0x00)]
    [InlineData(0x00, 0xFF, 0xFF)]
    [InlineData(0xFF, 0x00, 0xFF)]
    [InlineData(0x32, 0x96, 0x50)]
    [InlineData(0xB4, 0x64, 0x3C)]
    [InlineData(0x80, 0x00, 0x00)]
    [InlineData(0x00, 0x80, 0x00)]
    [InlineData(0x00, 0x00, 0x80)]
    [InlineData(0xFF, 0x80, 0x00)]
    [InlineData(0x80, 0xFF, 0x00)]
    [InlineData(0x00, 0x80, 0xFF)]
    public void Output_InValidRange(byte r, byte g, byte b)
    {
        var profile = Config.GetProfile();
        var result = ColorPipeline.ProcessPreIntegration(new Rgb(r, g, b), profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(result.r, result.g, result.b));
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x03)]
    [InlineData(0x05)]
    [InlineData(0x06)]
    [InlineData(0x07)]
    [InlineData(0x0A)]
    public void NearBlackGrays_DesatBoundary(byte v)
    {
        var profile = Config.GetProfile();
        var result = ColorPipeline.ProcessPreIntegration(new Rgb(v, v, v), profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(result.r, result.g, result.b));
        PipelineAssertions.AssertGrayPreserved(To8Bit(result.r, result.g, result.b), 10);
    }

    [Fact]
    public void Black_Input_ProducesBlackOrNearBlack()
    {
        var profile = Config.GetProfile();
        var result = ColorPipeline.ProcessPreIntegration(Rgb.Black, profile);
        PipelineAssertions.AssertBlack(To8Bit(result.r, result.g, result.b), 5);
    }

    [Theory]
    [InlineData(0x80)]
    [InlineData(0x40)]
    public void Gray_Preserved(byte v)
    {
        var profile = Config.GetProfile();
        var result = ColorPipeline.ProcessPreIntegration(new Rgb(v, v, v), profile);
        PipelineAssertions.AssertGrayPreserved(To8Bit(result.r, result.g, result.b), 10);
    }

    [Fact]
    public void BlackDetection_WorksWithDoubleValues()
    {
        ColorPipeline.IsBlack(0x06 / 255.0, 0x04 / 255.0, 0x08 / 255.0)
            .Should().BeTrue("Very dark color should be detected as black");
        ColorPipeline.IsBlack(0x10 / 255.0, 0.0, 0.0)
            .Should().BeFalse("Color above threshold should not be black");
    }

    [Fact]
    public void DisablingStages_ChangesOutput()
    {
        var profile = Config.GetProfile();
        var input = new Rgb(0x80, 0x60, 0x40);
        var full = ColorPipeline.ProcessPreIntegration(input, profile);
        var none = ColorPipeline.ProcessPreIntegration(input, profile, PipelineStage.None);
        full.Should().NotBe(none, "Disabled stages should produce different output");
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
        var result = ColorPipeline.ProcessPreIntegration(new Rgb(0xA0, 0x50, 0x70), profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(result.r, result.g, result.b));
    }

    [Theory]
    [InlineData(BambiStyle.Standard)]
    [InlineData(BambiStyle.Cinema)]
    [InlineData(BambiStyle.Sports)]
    [InlineData(BambiStyle.Gaming)]
    public void AllStyles_RedOrange(BambiStyle style)
    {
        Config.ActiveStyle = style;
        var profile = Config.GetProfile();
        var result = ColorPipeline.ProcessPreIntegration(new Rgb(200, 80, 20), profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(result.r, result.g, result.b));
        PipelineAssertions.AssertHuePreserved(To8Bit(result.r, result.g, result.b), 'R');
    }

    [Fact]
    public void IsBlack_ExactBoundary()
    {
        ColorPipeline.IsBlack(9.0 / 255 + 1.0 / 255 / 255.0, 0, 0).Should().BeTrue("Just below threshold");
        ColorPipeline.IsBlack(10.0 / 255.0, 0, 0).Should().BeFalse("At threshold, not black");
        ColorPipeline.IsBlack(0, 0, 9.0 / 255 + 1.0 / 255 / 255.0).Should().BeTrue("Only max channel matters");
    }

    [Theory]
    [InlineData(PipelineStage.Gain)]
    [InlineData(PipelineStage.Compress)]
    [InlineData(PipelineStage.Saturation)]
    [InlineData(PipelineStage.NoiseGate)]
    public void IndividualStage_ValidOutput(PipelineStage stage)
    {
        var profile = Config.GetProfile();
        var result = ColorPipeline.ProcessPreIntegration(new Rgb(128, 64, 32), profile, stage);
        PipelineAssertions.AssertValid8Bit(To8Bit(result.r, result.g, result.b));
    }

    [Theory]
    [InlineData(PipelineStage.Gain | PipelineStage.Compress)]
    [InlineData(PipelineStage.Gain | PipelineStage.Saturation)]
    [InlineData(PipelineStage.Compress | PipelineStage.Saturation)]
    [InlineData(PipelineStage.Compress | PipelineStage.NoiseGate)]
    [InlineData(PipelineStage.Saturation | PipelineStage.NoiseGate)]
    public void StagePairs_ValidOutput(PipelineStage stages)
    {
        var profile = Config.GetProfile();
        var result = ColorPipeline.ProcessPreIntegration(new Rgb(128, 64, 32), profile, stages);
        PipelineAssertions.AssertValid8Bit(To8Bit(result.r, result.g, result.b));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    public void NearBlackSaturated_ValidOutput(byte r, byte g, byte b)
    {
        var profile = Config.GetProfile();
        var result = ColorPipeline.ProcessPreIntegration(new Rgb(r, g, b), profile);
        PipelineAssertions.AssertValid8Bit(To8Bit(result.r, result.g, result.b));
    }

    [Fact]
    public void Deterministic()
    {
        var profile = Config.GetProfile();
        var input = new Rgb(0x90, 0x50, 0x70);
        var result1 = ColorPipeline.ProcessPreIntegration(input, profile);
        var result2 = ColorPipeline.ProcessPreIntegration(input, profile);
        result1.Should().Be(result2);
    }
}