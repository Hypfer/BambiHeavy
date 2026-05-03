namespace BambiHeavy.Edge;

public readonly struct ColorGrid
{
    public EdgeStrip Left { get; }
    public EdgeStrip Top { get; }
    public EdgeStrip Right { get; }
    public EdgeStrip Bottom { get; }

    public ColorGrid(EdgeStrip left, EdgeStrip top, EdgeStrip right, EdgeStrip bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }
}