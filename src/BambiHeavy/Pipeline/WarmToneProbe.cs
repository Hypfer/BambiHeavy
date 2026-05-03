// Warm-tone detection gate used by ColorGain and SaturationBoost.
// Returns 0.0 (no effect) to 1.0 (full effect).
//
// Detects warm colors: reds, oranges, skin tones, fire, bronze, sunset, brown.
// Cool colors (greens, blues, purples) and grays return 0.0.
//
// Active on dark to mid-bright content. Requires negative U and positive V in BT.601.

namespace BambiHeavy.Pipeline;

public static class WarmToneProbe
{
    private const double LumGateStart = 0.625;
    private const double LumGateEnd = 0.75;
    private const double UThreshold = 0.25;
    private const double VThreshold = 0.125;

    public static double Strength(double r, double g, double b)
    {
        var y = Bt601.Y_R * r + Bt601.Y_G * g + Bt601.Y_B * b;
        var u = Bt601.U_R * r + Bt601.U_G * g + Bt601.U_B * b;
        var v = Bt601.V_R * r + Bt601.V_G * g + Bt601.V_B * b;

        var lumRamp = LumGateEnd - LumGateStart;
        var luminanceGate = (LumGateEnd - y) / lumRamp;
        if (luminanceGate < 0.0) luminanceGate = 0.0;
        if (luminanceGate > 1.0) luminanceGate = 1.0;

        var uNorm = u / 0.5;
        var vNorm = v / 0.5;
        var uNeg = -uNorm / UThreshold;
        if (uNeg < 0.0) uNeg = 0.0;
        if (uNeg > 1.0) uNeg = 1.0;
        var vPos = vNorm / VThreshold;
        if (vPos < 0.0) vPos = 0.0;
        if (vPos > 1.0) vPos = 1.0;

        return luminanceGate * uNeg * vPos;
    }
}