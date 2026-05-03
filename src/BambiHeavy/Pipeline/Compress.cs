// Compress — power curve, crushes shadows and midtones.
// Higher exponent = more aggressive compression. Output stays 0-1 for exponent >= 1.

namespace BambiHeavy.Pipeline;

public static class Compress
{
    public static void Apply(ref double r, ref double g, ref double b, TuningProfile profile)
    {
        var e = profile.CompressExponent;
        r = Math.Pow(r, e);
        g = Math.Pow(g, e);
        b = Math.Pow(b, e);
    }
}