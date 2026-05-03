// Saturation Boost — scales chroma relative to luminance.
// Base 2.0× boost, scaled by profile SatBoostFactor. Warm colors (skin, fire, sunset)
// get up to 50% less boost to prevent oversaturated ambient glow.

namespace BambiHeavy.Pipeline;

public static class SaturationBoost
{
    private const double SatBase = 2.0;

    public static void Apply(ref double r, ref double g, ref double b, TuningProfile profile)
    {
        var y = Bt601.Y_R * r + Bt601.Y_G * g + Bt601.Y_B * b;
        var warm = WarmToneProbe.Strength(r, g, b);

        var ceiling = SatBase * profile.SatBoostFactor;
        var floor = profile.SatBlendFactor;
        var satFactor = ceiling * (1.0 - warm) + floor * warm;

        var rOut = satFactor * (r - y) + y;
        var gOut = satFactor * (g - y) + y;
        var bOut = satFactor * (b - y) + y;

        var minOut = Math.Min(rOut, Math.Min(gOut, bOut));
        if (minOut < 0.0)
        {
            rOut -= minOut;
            gOut -= minOut;
            bOut -= minOut;
        }

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