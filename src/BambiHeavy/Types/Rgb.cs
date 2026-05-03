namespace BambiHeavy.Types;

public readonly struct Rgb
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    public Rgb(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    public static Rgb Black => new(0, 0, 0);
}