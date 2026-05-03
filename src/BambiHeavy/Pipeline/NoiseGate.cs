// Noise Gate — prevent color noise in dark scenes.
// Scales dark signals toward black.

namespace BambiHeavy.Pipeline;

public static class NoiseGate
{
    public static void Apply(ref double r, ref double g, ref double b, TuningProfile profile)
    {
        var maxCh = Math.Max(r, Math.Max(g, b));
        var threshold = profile.NoiseGateThreshold / 100.0;

        if (maxCh < threshold)
        {
            var scale = threshold > 0.0 ? maxCh / threshold : 0.0;
            r *= scale;
            g *= scale;
            b *= scale;
        }
    }
}