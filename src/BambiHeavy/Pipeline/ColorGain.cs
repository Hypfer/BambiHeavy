// Color Gain — brightness boost with warm-tone damping.
// Base 1.5× gain, scaled by profile GainFactor. Warm colors (skin, fire, sunset)
// get 25% less gain to prevent harsh ambient glow.

namespace BambiHeavy.Pipeline;

public static class ColorGain
{
    private const double GainBase = 1.5;
    private const double WarmToneFloor = 0.75; // When gate is fully active, gain drops to 75% of base

    public static void Apply(ref double r, ref double g, ref double b, TuningProfile profile)
    {
        var warm = WarmToneProbe.Strength(r, g, b);

        var baseGain = GainBase * profile.GainFactor;
        var multiplier = baseGain * (WarmToneFloor + (1.0 - WarmToneFloor) * (1.0 - warm));

        var rOut = r * multiplier;
        var gOut = g * multiplier;
        var bOut = b * multiplier;

        var maxOut = Math.Max(rOut, Math.Max(gOut, bOut));

        if (maxOut > 1.0)
        {
            var scale = 1.0 / maxOut;
            rOut *= scale;
            gOut *= scale;
            bOut *= scale;
        }

        r = rOut;
        g = gOut;
        b = bOut;
    }
}