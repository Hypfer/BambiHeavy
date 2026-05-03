using BambiHeavy.Types;

namespace BambiHeavy.Edge;

public readonly struct EdgeStrip
{
    public int Length { get; }
    public byte[] R { get; }
    public byte[] G { get; }
    public byte[] B { get; }
    public int ContentStart { get; }
    public int ContentEnd { get; }

    public EdgeStrip(int length, byte[] r, byte[] g, byte[] b, int contentStart = 0, int contentEnd = 0)
    {
        Length = length;
        R = r;
        G = g;
        B = b;
        ContentStart = contentStart;
        ContentEnd = contentEnd;
    }

    public Rgb this[int index] => new(R[index], G[index], B[index]);

    public static EdgeStrip Empty(int length)
    {
        return new EdgeStrip(length,
            new byte[length], new byte[length], new byte[length]);
    }

    public Rgb ContentAverage()
    {
        var start = ContentStart;
        var end = ContentEnd;
        if (start >= Length) return Rgb.Black;
        if (start == end) end = start + 1;
        end = Math.Min(end, Length);

        long sumR = 0, sumG = 0, sumB = 0;
        var count = end - start;
        for (var i = start; i < end; i++)
        {
            sumR += R[i];
            sumG += G[i];
            sumB += B[i];
        }

        return new Rgb(
            (byte)(sumR / count),
            (byte)(sumG / count),
            (byte)(sumB / count));
    }
}