namespace BambiHeavy.PipelineTests.Utilities;

/// <summary>
///     Assertions that operate in 0-255 (8-bit) space.
///     Tolerances are absolute offsets (e.g., 5 means +/-5), not percentages.
/// </summary>
public static class PipelineAssertions
{
    private const int DefaultTolerance = 5;

    public static void AssertNear8Bit(int actual, int expected, int tolerance = DefaultTolerance)
    {
        actual.Should().BeInRange(
            Math.Max(0, expected - tolerance),
            Math.Min(255, expected + tolerance),
            $"actual={actual}, expected={expected}+/-{tolerance}");
    }

    public static void AssertNear8Bit(double actual, int expected, int tolerance = DefaultTolerance)
    {
        AssertNear8Bit((int)Math.Round(actual), expected, tolerance);
    }

    public static void AssertNear8Bit(double actual, double expected, int tolerance = DefaultTolerance)
    {
        AssertNear8Bit((int)Math.Round(actual), (int)Math.Round(expected), tolerance);
    }

    public static void AssertNear8Bit((int r, int g, int b) actual, (int r, int g, int b) expected,
        int tolerance = DefaultTolerance)
    {
        AssertNear8Bit(actual.r, expected.r, tolerance);
        AssertNear8Bit(actual.g, expected.g, tolerance);
        AssertNear8Bit(actual.b, expected.b, tolerance);
    }

    /// <summary>Assert a value is effectively black (within tolerance of 0).</summary>
    public static void AssertBlack(int value, int tolerance = 3)
    {
        value.Should().BeInRange(0, tolerance, $"expected black, got {value}");
    }

    /// <summary>Assert a value is effectively white (within tolerance of 255).</summary>
    public static void AssertWhite(int value, int tolerance = 3)
    {
        value.Should().BeInRange(255 - tolerance, 255, $"expected white, got {value}");
    }

    /// <summary>Assert all three channels are effectively black.</summary>
    public static void AssertBlack((int r, int g, int b) rgb, int tolerance = 3)
    {
        AssertBlack(rgb.r, tolerance);
        AssertBlack(rgb.g, tolerance);
        AssertBlack(rgb.b, tolerance);
    }

    /// <summary>Assert all three channels are effectively white.</summary>
    public static void AssertWhite((int r, int g, int b) rgb, int tolerance = 3)
    {
        AssertWhite(rgb.r, tolerance);
        AssertWhite(rgb.g, tolerance);
        AssertWhite(rgb.b, tolerance);
    }

    /// <summary>Assert output is in valid 0-255 range.</summary>
    public static void AssertValid8Bit(int value, string? label = null)
    {
        value.Should().BeInRange(0, 255, $"{label ?? "value"} should be 0-255, got {value}");
    }

    /// <summary>Assert all channels are in valid 0-255 range.</summary>
    public static void AssertValid8Bit((int r, int g, int b) rgb)
    {
        AssertValid8Bit(rgb.r, "R");
        AssertValid8Bit(rgb.g, "G");
        AssertValid8Bit(rgb.b, "B");
    }

    /// <summary>Assert gray input produces approximately equal output channels.</summary>
    public static void AssertGrayPreserved((int r, int g, int b) rgb, int tolerance = 5)
    {
        var maxDiff = Math.Max(Math.Abs(rgb.r - rgb.g), Math.Max(Math.Abs(rgb.g - rgb.b), Math.Abs(rgb.r - rgb.b)));
        maxDiff.Should().BeLessOrEqualTo(tolerance,
            $"gray should be preserved, got R={rgb.r} G={rgb.g} B={rgb.b} (max diff {maxDiff})");
    }

    /// <summary>Assert the dominant channel remains dominant after processing.</summary>
    public static void AssertHuePreserved((int r, int g, int b) rgb, char expectedDominant)
    {
        var max = expectedDominant switch
        {
            'R' => rgb.r,
            'G' => rgb.g,
            'B' => rgb.b,
            _ => throw new ArgumentException("expectedDominant must be R, G, or B")
        };
        max.Should().BeGreaterOrEqualTo(rgb.r, $"{expectedDominant} should remain dominant over R");
        max.Should().BeGreaterOrEqualTo(rgb.g, $"{expectedDominant} should remain dominant over G");
        max.Should().BeGreaterOrEqualTo(rgb.b, $"{expectedDominant} should remain dominant over B");
    }
}