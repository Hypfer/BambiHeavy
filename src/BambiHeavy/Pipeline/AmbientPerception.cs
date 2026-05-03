namespace BambiHeavy.Pipeline;

public static class AmbientPerception
{
    private const double DesatThreshold = 0.05;
    private const double WhiteCap = 0.75;

    public static (double r, double g, double b) Apply(double r, double g, double b)
    {
        double rf = r, gf = g, bf = b;
        var maxCh = Math.Max(rf, Math.Max(gf, bf));
        var minCh = Math.Min(rf, Math.Min(gf, bf));

        // Low-brightness desaturation: blend toward gray when max channel is very low
        if (maxCh < DesatThreshold)
        {
            var factor = maxCh / DesatThreshold;
            var gray = (rf + gf + bf) / 3.0;
            rf = gray + (rf - gray) * factor;
            gf = gray + (gf - gray) * factor;
            bf = gray + (bf - gray) * factor;
        }

        // White brightness cap
        if (maxCh > 0)
        {
            var whiteness = minCh / maxCh;
            var scale = 1.0 - (1.0 - WhiteCap) * whiteness;
            rf *= scale;
            gf *= scale;
            bf *= scale;
        }

        return (rf, gf, bf);
    }
}