namespace BambiHeavy.Pipeline;

public static class RgbToXy
{
    private const double Wpx = 0.3127;
    private const double Wpy = 0.3290;

    // Standard sRGB (D65) → XYZ matrix (IEC 61966-2-1)
    private const double MxR = 0.4124564, MxG = 0.3575761, MxB = 0.1804375;
    private const double MyR = 0.2126729, MyG = 0.7151522, MyB = 0.0721750;
    private const double MzR = 0.0193339, MzG = 0.1191920, MzB = 0.9503043;

    private const ushort BriMax = 2047;
    private const ushort XyMax = 1023;

    private static readonly Output BlackOutput = new()
    {
        Brightness = 0,
        X = (ushort)Math.Round(Wpx * XyMax),
        Y = (ushort)Math.Round(Wpy * XyMax)
    };

    public static Output Apply(double r, double g, double b)
    {
        if (r == 0 && g == 0 && b == 0)
            return BlackOutput;

        // Input is already linear-ish (pipeline gamma stage applies squaring).
        var X = r * MxR + g * MxG + b * MxB;
        var Y = r * MyR + g * MyG + b * MyB;
        var Z = r * MzR + g * MzG + b * MzB;
        var total = X + Y + Z;

        // XYZ → xy chromaticity
        var cx = total > 0 ? X / total : Wpx;
        var cy = total > 0 ? Y / total : Wpy;

        // Brightness from max channel, scaled to 11-bit
        var maxCh = Math.Max(r, Math.Max(g, b));
        var brightness = (ushort)Math.Clamp(Math.Round(maxCh * BriMax), 0, BriMax);

        // Pack xy to 10-bit (raw CIE xy × 1023)
        var xq = (ushort)Math.Clamp(Math.Round(cx * XyMax), 0, XyMax);
        var yq = (ushort)Math.Clamp(Math.Round(cy * XyMax), 0, XyMax);

        return new Output { Brightness = brightness, X = xq, Y = yq };
    }

    public struct Output
    {
        public ushort Brightness; // 0-2047
        public ushort X; // 0-1023 (raw CIE x × 1023)
        public ushort Y; // 0-1023 (raw CIE y × 1023)
    }
}