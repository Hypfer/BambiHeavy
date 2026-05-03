using BambiHeavy.Types;

namespace BambiHeavy.Pipeline;

[Flags]
public enum PipelineStage : byte
{
    None = 0,
    Gain = 1,
    Compress = 2,
    Saturation = 4,
    NoiseGate = 8
}

public static class ColorPipeline
{
    public static (double r, double g, double b) ProcessPreIntegration(Rgb input, TuningProfile profile,
        PipelineStage activeStages = PipelineStage.Gain | PipelineStage.Compress | PipelineStage.Saturation |
                                     PipelineStage.NoiseGate)
    {
        var r = input.R / 255.0;
        var g = input.G / 255.0;
        var b = input.B / 255.0;

        if ((activeStages & PipelineStage.Gain) != 0)
        {
            ColorGain.Apply(ref r, ref g, ref b, profile);
            Clamp(ref r, ref g, ref b);
        }

        if ((activeStages & PipelineStage.Compress) != 0)
        {
            Compress.Apply(ref r, ref g, ref b, profile);
            Clamp(ref r, ref g, ref b);
        }

        if ((activeStages & PipelineStage.Saturation) != 0)
        {
            SaturationBoost.Apply(ref r, ref g, ref b, profile);
            Clamp(ref r, ref g, ref b);
        }

        if ((activeStages & PipelineStage.NoiseGate) != 0)
        {
            NoiseGate.Apply(ref r, ref g, ref b, profile);
            Clamp(ref r, ref g, ref b);
        }

        return (r, g, b);
    }

    public static bool IsBlack(double r, double g, double b)
    {
        return Math.Max(Math.Max(r, g), b) < 0.0392;
        // ~10/255
    }

    private static void Clamp(ref double r, ref double g, ref double b)
    {
        if (r < 0.0) r = 0.0;
        else if (r > 1.0) r = 1.0;
        if (g < 0.0) g = 0.0;
        else if (g > 1.0) g = 1.0;
        if (b < 0.0) b = 0.0;
        else if (b > 1.0) b = 1.0;
    }
}