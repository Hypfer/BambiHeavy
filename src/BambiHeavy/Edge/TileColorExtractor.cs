using BambiHeavy.Capture;
using BambiHeavy.Types;

namespace BambiHeavy.Edge;

public static class TileColorExtractor
{
    // Per-edge band configuration
    private const int TilesPerEdge = 128;
    private const double BandFraction = 0.25; // 25% of content dimension

    // Tile sampling
    private const int TileSampleStep = 4;

    // Neighbor reinforcement
    private const double ReinforceC = 2.0;
    private const double DarkThreshold = 50.0;

    // Edge proximity falloff (within band)
    private const int DistPower = 2;

    // Over-suppression fallback
    private const double SuppressionThreshold = 0.1;

    // Pre-allocated tile data per edge (128 tiles each). Index: 0=left, 1=right, 2=top, 3=bottom
    private static readonly float[] _tileR = new float[4 * TilesPerEdge];
    private static readonly float[] _tileG = new float[4 * TilesPerEdge];
    private static readonly float[] _tileB = new float[4 * TilesPerEdge];
    private static readonly float[] _tileBright = new float[4 * TilesPerEdge];
    private static readonly float[] _tileFactor = new float[4 * TilesPerEdge];
    private static readonly int[] _tileCount = new int[4 * TilesPerEdge];
    private static readonly float[] _neighborBuf = new float[4];

    // Per-edge grid dimensions and band bounds (for reflection-based debug access)
#pragma warning disable IDE0052 // Remove unread private members
    private static readonly int[] _cols = new int[4];
    private static readonly int[] _rows = new int[4];
    private static readonly int[] _bandLeft = new int[4];
    private static readonly int[] _bandTop = new int[4];
    private static readonly int[] _bandRight = new int[4];
    private static readonly int[] _bandBottom = new int[4];
#pragma warning restore IDE0052

    // Pre-allocated output arrays for zero-allocation Extract
    private static byte[] _topRArr = Array.Empty<byte>();
    private static byte[] _topGArr = Array.Empty<byte>();
    private static byte[] _topBArr = Array.Empty<byte>();

    private static byte[] _botRArr = Array.Empty<byte>();
    private static byte[] _botGArr = Array.Empty<byte>();
    private static byte[] _botBArr = Array.Empty<byte>();

    private static byte[] _lRArr = Array.Empty<byte>();
    private static byte[] _lGArr = Array.Empty<byte>();
    private static byte[] _lBArr = Array.Empty<byte>();

    private static byte[] _rRArr = Array.Empty<byte>();
    private static byte[] _rGArr = Array.Empty<byte>();
    private static byte[] _rBArr = Array.Empty<byte>();

    public static ColorGrid Extract(ScreenCaptureResult frame, Rectangle screenBounds,
        (int left, int top, int right, int bottom) contentBounds)
    {
        var pixels = Config.EdgePixels;
        var stride = frame.Stride;
        var px = frame.Pixels;
        var sw = screenBounds.Width;
        var sh = screenBounds.Height;

        if (sw == 0 || sh == 0)
            return new ColorGrid(
                new EdgeStrip(pixels, _lRArr, _lGArr, _lBArr),
                new EdgeStrip(pixels, _topRArr, _topGArr, _topBArr),
                new EdgeStrip(pixels, _rRArr, _rGArr, _rBArr),
                new EdgeStrip(pixels, _botRArr, _botGArr, _botBArr));

        var (cLeft, cTop, cRight, cBottom) = contentBounds;
        var contentW = cRight - cLeft;
        var contentH = cBottom - cTop;

        if (contentW <= 0 || contentH <= 0)
            return FillOutput(pixels, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        // Compute band regions with overlap protection
        var bandDepthH = (int)(contentW * BandFraction); // horizontal band depth (left/right)
        var bandDepthV = (int)(contentH * BandFraction); // vertical band depth (top/bottom)

        // Clamp to prevent overlap
        bandDepthH = Math.Min(bandDepthH, contentW / 2);
        bandDepthV = Math.Min(bandDepthV, contentH / 2);

        var (ltR, ltG, ltB) = ProcessEdge(px, stride,
            cLeft, cTop, cLeft + bandDepthH, cBottom, Edge.Left, 0);
        var (rtR, rtG, rtB) = ProcessEdge(px, stride,
            cRight - bandDepthH, cTop, cRight, cBottom, Edge.Right, 1);
        var (ttR, ttG, ttB) = ProcessEdge(px, stride,
            cLeft, cTop, cRight, cTop + bandDepthV, Edge.Top, 2);
        var (btR, btG, btB) = ProcessEdge(px, stride,
            cLeft, cBottom - bandDepthV, cRight, cBottom, Edge.Bottom, 3);

        return FillOutput(pixels, ttR, ttG, ttB, btR, btG, btB, ltR, ltG, ltB, rtR, rtG, rtB);
    }

    private static (byte r, byte g, byte b) ProcessEdge(
        byte[] px, int stride, int bandLeft, int bandTop, int bandRight, int bandBottom, Edge edge, int edgeIdx)
    {
        var bandW = bandRight - bandLeft;
        var bandH = bandBottom - bandTop;

        if (bandW <= 0 || bandH <= 0)
            return (0, 0, 0);

        // Compute grid dimensions for this edge's band (target ~128 tiles, square-ish)
        var bandAspect = (double)bandW / bandH;
        var cols = (int)Math.Round(Math.Sqrt(TilesPerEdge * bandAspect));
        cols = Math.Clamp(cols, 1, TilesPerEdge); // Ensure cols doesn't wildly overshoot on extreme ratios

        // Use integer division to strictly guarantee that (cols * rows) <= TilesPerEdge
        var rows = TilesPerEdge / cols;
        if (rows < 1) rows = 1;

        // Store grid dims and band bounds for reflection-based debug access
        StoreEdgeMeta(edgeIdx, cols, rows, bandLeft, bandTop, bandRight, bandBottom);

        // Phase 1: Compute tiles
        ComputeTiles(px, stride, bandLeft, bandTop, bandRight, bandBottom, cols, rows, edgeIdx);

        // Phase 2: Reinforcement
        ComputeReinforcement(cols, rows, edgeIdx);

        // Phase 3: Aggregate
        var suppressed = IsEdgeSuppressed(cols, rows, edgeIdx);
        return AggregateEdge(cols, rows, edge, suppressed, edgeIdx);
    }

    private static void StoreEdgeMeta(int idx, int cols, int rows, int bLeft, int bTop, int bRight, int bBottom)
    {
        _cols[idx] = cols;
        _rows[idx] = rows;
        _bandLeft[idx] = bLeft;
        _bandTop[idx] = bTop;
        _bandRight[idx] = bRight;
        _bandBottom[idx] = bBottom;
    }

    private static void ComputeTiles(byte[] px, int stride,
        int bandLeft, int bandTop, int bandRight, int bandBottom, int cols, int rows, int edgeIdx)
    {
        var bandW = bandRight - bandLeft;
        var bandH = bandBottom - bandTop;
        var tileW = (double)bandW / cols;
        var tileH = (double)bandH / rows;
        var off = edgeIdx * TilesPerEdge;
        var totalTiles = cols * rows;

        Array.Clear(_tileR, off, totalTiles);
        Array.Clear(_tileG, off, totalTiles);
        Array.Clear(_tileB, off, totalTiles);
        Array.Clear(_tileCount, off, totalTiles);

        unsafe
        {
            fixed (byte* pPx = px)
            {
                for (var y = bandTop; y < bandBottom; y += TileSampleStep)
                {
                    var rowOff = y * stride;
                    var row = (int)((y - bandTop) / tileH);
                    if (row >= rows) row = rows - 1;

                    for (var x = bandLeft; x < bandRight; x += TileSampleStep)
                    {
                        var col = (int)((x - bandLeft) / tileW);
                        if (col >= cols) col = cols - 1;

                        var idx = off + row * cols + col;
                        var p = pPx + rowOff + x * 4;

                        _tileB[idx] += p[0];
                        _tileG[idx] += p[1];
                        _tileR[idx] += p[2];
                        _tileCount[idx]++;
                    }
                }
            }
        }

        for (var i = 0; i < totalTiles; i++)
        {
            var idx = off + i;
            var count = _tileCount[idx];
            if (count > 0)
            {
                _tileR[idx] /= count;
                _tileG[idx] /= count;
                _tileB[idx] /= count;
            }

            _tileBright[idx] = 0.299f * _tileR[idx] + 0.587f * _tileG[idx] + 0.114f * _tileB[idx];
        }
    }

    private static void ComputeReinforcement(int cols, int rows, int edgeIdx)
    {
        var off = edgeIdx * TilesPerEdge;
        var total = cols * rows;

        for (var i = 0; i < total; i++)
        {
            var ii = off + i;
            var row = i / cols;
            var col = i % cols;

            if (_tileBright[ii] < DarkThreshold)
            {
                _tileFactor[ii] = 1f;
                continue;
            }

            var nCount = 0;
            if (col > 0)       _neighborBuf[nCount++] = _tileBright[ii - 1];
            if (col < cols - 1) _neighborBuf[nCount++] = _tileBright[ii + 1];
            if (row > 0)       _neighborBuf[nCount++] = _tileBright[ii - cols];
            if (row < rows - 1) _neighborBuf[nCount++] = _tileBright[ii + cols];

            var medianBright = Median(_neighborBuf, nCount);
            var ratio = _tileBright[ii] / (medianBright + 1f);
            _tileFactor[ii] = (float)Math.Exp(-ratio / ReinforceC);
        }
    }

    private static bool IsEdgeSuppressed(int cols, int rows, int edgeIdx)
    {
        var off = edgeIdx * TilesPerEdge;
        var total = cols * rows;
        var contributing = 0;
        double totalFactor = 0;

        for (var i = 0; i < total; i++)
        {
            var ii = off + i;
            if (_tileBright[ii] > DarkThreshold)
            {
                contributing++;
                totalFactor += _tileFactor[ii];
            }
        }

        if (contributing == 0) return false;
        return totalFactor / contributing < SuppressionThreshold;
    }

    private static (byte r, byte g, byte b) AggregateEdge(int cols, int rows, Edge edge, bool fallback, int edgeIdx)
    {
        var off = edgeIdx * TilesPerEdge;
        double sumR = 0, sumG = 0, sumB = 0, sumW = 0;

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var idx = off + row * cols + col;
                var dist = GetNormalizedDistance(col, row, cols, rows, edge);
                
                // DistPower is 2, so we can optimize Math.Pow to simple multiplication
                var invDist = 1.0 - dist;
                var proximity = invDist * invDist;

                if (proximity < 0.01) continue;

                var w = fallback ? proximity : _tileFactor[idx] * proximity;

                sumR += _tileR[idx] * w;
                sumG += _tileG[idx] * w;
                sumB += _tileB[idx] * w;
                sumW += w;
            }
        }

        if (sumW > 0)
            return (
                (byte)Math.Clamp(sumR / sumW, 0, 255),
                (byte)Math.Clamp(sumG / sumW, 0, 255),
                (byte)Math.Clamp(sumB / sumW, 0, 255));

        return (0, 0, 0);
    }

    /// <summary>
    /// Normalized distance [0,1] from tile center to the edge boundary within the band.
    /// 0 = adjacent to edge, 1 = farthest from edge (inner side of band).
    /// </summary>
    private static double GetNormalizedDistance(int col, int row, int cols, int rows, Edge edge)
    {
        return edge switch
        {
            Edge.Left => (col + 0.5) / cols,       // 0 = leftmost col, 1 = innermost col
            Edge.Right => 1.0 - (col + 0.5) / cols, // 0 = rightmost col, 1 = innermost col
            Edge.Top => (row + 0.5) / rows,         // 0 = topmost row, 1 = innermost row
            Edge.Bottom => 1.0 - (row + 0.5) / rows, // 0 = bottommost row, 1 = innermost row
            _ => 0
        };
    }

    private static float Median(float[] buf, int count)
    {
        if (count == 0) return 0f;
        if (count == 1) return buf[0];
        if (count == 2) return (buf[0] + buf[1]) * 0.5f;

        for (var i = 0; i < count - 1; i++)
            for (var j = i + 1; j < count; j++)
                if (buf[i] > buf[j])
                {
                    var t = buf[i];
                    buf[i] = buf[j];
                    buf[j] = t;
                }

        return count % 2 == 1 ? buf[count / 2] : (buf[count / 2 - 1] + buf[count / 2]) * 0.5f;
    }

    private static ColorGrid FillOutput(int pixels, byte tR, byte tG, byte tB,
        byte bR, byte bG, byte bB, byte lR, byte lG, byte lB, byte rR, byte rG, byte rB)
    {
        if (_topRArr.Length != pixels)
        {
            _topRArr = new byte[pixels];
            _topGArr = new byte[pixels];
            _topBArr = new byte[pixels];
            _botRArr = new byte[pixels];
            _botGArr = new byte[pixels];
            _botBArr = new byte[pixels];
            _lRArr = new byte[pixels];
            _lGArr = new byte[pixels];
            _lBArr = new byte[pixels];
            _rRArr = new byte[pixels];
            _rGArr = new byte[pixels];
            _rBArr = new byte[pixels];
        }

        Array.Fill(_topRArr, tR);
        Array.Fill(_topGArr, tG);
        Array.Fill(_topBArr, tB);
        Array.Fill(_botRArr, bR);
        Array.Fill(_botGArr, bG);
        Array.Fill(_botBArr, bB);
        Array.Fill(_lRArr, lR);
        Array.Fill(_lGArr, lG);
        Array.Fill(_lBArr, lB);
        Array.Fill(_rRArr, rR);
        Array.Fill(_rGArr, rG);
        Array.Fill(_rBArr, rB);

        return new ColorGrid(
            new EdgeStrip(pixels, _lRArr, _lGArr, _lBArr),
            new EdgeStrip(pixels, _topRArr, _topGArr, _topBArr),
            new EdgeStrip(pixels, _rRArr, _rGArr, _rBArr),
            new EdgeStrip(pixels, _botRArr, _botGArr, _botBArr));
    }

    internal enum Edge
    {
        Top,
        Bottom,
        Left,
        Right
 }
}
