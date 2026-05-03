using BambiHeavy.Capture;
using BambiHeavy.Types;

namespace BambiHeavy.Edge;

public static class ContentBoundsDetector
{
    private const int BoundsDetectInterval = 30;

    // Persistent hysteresis for bounding box stability
    private static int hystLeft = -1, hystTop = -1, hystRight = -1, hystBottom = -1;

    // Detect content bounds at 1/4 pipeline FPS to avoid per-frame scan+alloc overhead
    private static int _boundsFrameCounter;

    private static readonly List<EdgeCandidate> _leftCandidates = new(42);
    private static readonly List<EdgeCandidate> _rightCandidates = new(42);
    private static readonly List<EdgeCandidate> _topCandidates = new(42);
    private static readonly List<EdgeCandidate> _bottomCandidates = new(42);

    private static readonly int[] _filterBuffer = new int[42];

    public static void Reset()
    {
        hystLeft = hystTop = hystRight = hystBottom = -1;
        _boundsFrameCounter = 0;
    }

    public static (int left, int top, int right, int bottom) Detect(ScreenCaptureResult frame, Rectangle screenBounds)
    {
        var px = frame.Pixels;
        var sw = screenBounds.Width;
        var sh = screenBounds.Height;
        var stride = frame.Stride;

        if (sw == 0 || sh == 0)
            return (0, 0, 0, 0);

        _boundsFrameCounter++;
        var detectNow = _boundsFrameCounter == 1 || _boundsFrameCounter % BoundsDetectInterval == 0;

        int cLeft, cTop, cRight, cBottom;
        if (detectNow)
        {
            (cLeft, cTop, cRight, cBottom) = DetectContentBounds(px, sw, sh, stride);

            var margin = 15;
            if (hystLeft < 0 || Math.Abs(cLeft - hystLeft) > margin) hystLeft = cLeft;
            if (hystTop < 0 || Math.Abs(cTop - hystTop) > margin) hystTop = cTop;
            if (hystRight < 0 || Math.Abs(cRight - hystRight) > margin) hystRight = cRight;
            if (hystBottom < 0 || Math.Abs(cBottom - hystBottom) > margin) hystBottom = cBottom;
        }
        else
        {
            cLeft = hystLeft;
            cTop = hystTop;
            cRight = hystRight;
            cBottom = hystBottom;
        }

        // First frame fallback: hysteresis not yet initialized
        if (cLeft < 0)
        {
            cLeft = 0;
            cTop = 0;
            cRight = sw;
            cBottom = sh;
        }

        return (cLeft, cTop, cRight, cBottom);
    }

    /// <summary>
    ///     Detects content bounding box by scanning 5px-wide bands from center outward in 4 directions.
    /// </summary>
    private static (int left, int top, int right, int bottom) DetectContentBounds(byte[] px, int w, int h, int stride)
    {
        if (w == 0 || h == 0) return (0, 0, 0, 0);

        var lines = 21;

        _leftCandidates.Clear();
        _rightCandidates.Clear();
        _topCandidates.Clear();
        _bottomCandidates.Clear();

        var cx = w / 2;
        var cy = h / 2;
        var step = 5;

        // Scan horizontal lines to find Left/Right edges (perpendicular coord = y)
        for (var i = 0; i < lines; i++)
        {
            var y = (int)(h * ((i + 1.0) / (lines + 1)));

            var lIo = ScanLine(px, w, h, stride, cx, y, -1, 0, false, step);
            var lOi = ScanLine(px, w, h, stride, 0, y, 1, 0, true, step);
            if (lIo >= 0) _leftCandidates.Add(new EdgeCandidate { Edge = lIo, Perpendicular = y });
            if (lOi >= 0) _leftCandidates.Add(new EdgeCandidate { Edge = lOi, Perpendicular = y });

            var rIo = ScanLine(px, w, h, stride, cx, y, 1, 0, false, step);
            var rOi = ScanLine(px, w, h, stride, w - 1, y, -1, 0, true, step);
            if (rIo >= 0) _rightCandidates.Add(new EdgeCandidate { Edge = rIo, Perpendicular = y });
            if (rOi >= 0) _rightCandidates.Add(new EdgeCandidate { Edge = rOi, Perpendicular = y });
        }

        // Scan vertical lines to find Top/Bottom edges (perpendicular coord = x)
        for (var i = 0; i < lines; i++)
        {
            var x = (int)(w * ((i + 1.0) / (lines + 1)));

            var tIo = ScanLine(px, w, h, stride, x, cy, 0, -1, false, step);
            var tOi = ScanLine(px, w, h, stride, x, 0, 0, 1, true, step);
            if (tIo >= 0) _topCandidates.Add(new EdgeCandidate { Edge = tIo, Perpendicular = x });
            if (tOi >= 0) _topCandidates.Add(new EdgeCandidate { Edge = tOi, Perpendicular = x });

            var bIo = ScanLine(px, w, h, stride, x, cy, 0, 1, false, step);
            var bOi = ScanLine(px, w, h, stride, x, h - 1, 0, -1, true, step);
            if (bIo >= 0) _bottomCandidates.Add(new EdgeCandidate { Edge = bIo, Perpendicular = x });
            if (bOi >= 0) _bottomCandidates.Add(new EdgeCandidate { Edge = bOi, Perpendicular = x });
        }

        // Round 1: compute initial medians without filtering
        var cLeft = GetMedian(_leftCandidates);
        var cRight = GetMedian(_rightCandidates);
        var cTop = GetMedian(_topCandidates);
        var cBottom = GetMedian(_bottomCandidates);

        if (cLeft < 0) cLeft = 0;
        if (cRight < 0) cRight = w;
        if (cTop < 0) cTop = 0;
        if (cBottom < 0) cBottom = h;

        // Round 2: spatially filter candidates and recompute
        var cLeftF = GetMedianFiltered(_leftCandidates, cTop, cBottom, cLeft);
        var cRightF = GetMedianFiltered(_rightCandidates, cTop, cBottom, cRight);
        var cTopF = GetMedianFiltered(_topCandidates, cLeft, cRight, cTop);
        var cBottomF = GetMedianFiltered(_bottomCandidates, cLeft, cRight, cBottom);

        cLeft = cLeftF;
        cRight = cRightF;
        cTop = cTopF;
        cBottom = cBottomF;

        // Compensate for step-skip inaccuracy by expanding back out by 'step'
        if (cLeft >= 0) cLeft = Math.Max(0, cLeft - step);
        if (cRight >= 0) cRight = Math.Min(w, cRight + step);
        if (cTop >= 0) cTop = Math.Max(0, cTop - step);
        if (cBottom >= 0) cBottom = Math.Min(h, cBottom + step);

        if (cLeft < 0) cLeft = 0;
        if (cRight < 0) cRight = w;
        if (cTop < 0) cTop = 0;
        if (cBottom < 0) cBottom = h;

        // Clamp: left < right, top < bottom
        if (cLeft >= cRight)
        {
            var mid = (cLeft + cRight) / 2;
            cLeft = mid;
            cRight = mid;
        }

        if (cTop >= cBottom)
        {
            var mid = (cTop + cBottom) / 2;
            cTop = mid;
            cBottom = mid;
        }

        // Sanity-check: if detected area doesn't match a known video aspect ratio,
        // assume content fills the screen
        if (!MatchesVideoAspectRatio(cLeft, cTop, cRight, cBottom))
        {
            cLeft = 0;
            cTop = 0;
            cRight = w;
            cBottom = h;
        }
        else
        {
            // Sanity-check 2: Letterboxing/pillarboxing should be roughly symmetrical.
            // If the center of the detected bounds is far from the center of the screen,
            // it's probably just a dark scene in a full-screen video.
            var boundsCenterX = (cLeft + cRight) / 2;
            var boundsCenterY = (cTop + cBottom) / 2;
            var screenCenterX = w / 2;
            var screenCenterY = h / 2;
            
            // Allow 5% of screen dimension tolerance for off-center
            if (Math.Abs(boundsCenterX - screenCenterX) > w * 0.05 ||
                Math.Abs(boundsCenterY - screenCenterY) > h * 0.05)
            {
                cLeft = 0;
                cTop = 0;
                cRight = w;
                cBottom = h;
            }
        }

        return (cLeft, cTop, cRight, cBottom);
    }

    /// <summary>
    ///     Scans a 1D line looking for content.
    ///     If outsideIn is true, finds the FIRST bright pixel.
    ///     If outsideIn is false, finds the LAST bright pixel.
    /// </summary>
    private static unsafe int ScanLine(byte[] px, int w, int h, int stride, int startX, int startY, int dirX, int dirY,
        bool outsideIn, int step)
    {
        var threshold = 4;

        var currentX = startX;
        var currentY = startY;
        var lastFound = -1;

        var stepIdx = dirY * step * stride + dirX * step * 4;
        var idx = currentY * stride + currentX * 4;

        fixed (byte* pPx = px)
        {
            while (currentX >= 0 && currentX < w && currentY >= 0 && currentY < h)
            {
                byte p0 = pPx[idx];
                byte p1 = pPx[idx + 1];
                byte p2 = pPx[idx + 2];

                int m = p0 > p1 ? p0 > p2 ? p0 : p2 :
                    p1 > p2 ? p1 : p2;

                if (m >= threshold)
                {
                    var coord = dirX != 0 ? currentX : currentY;
                    if (outsideIn) return coord;
                    lastFound = coord;
                }

                currentX += dirX * step;
                currentY += dirY * step;
                idx += stepIdx;
            }
        }

        return lastFound;
    }

    private static int GetMedian(List<EdgeCandidate> candidates)
    {
        if (candidates.Count == 0) return -1;

        candidates.Sort();
        return candidates[candidates.Count / 2].Edge;
    }

    private static int GetMedianFiltered(List<EdgeCandidate> candidates, int rangeStart, int rangeEnd, int fallback)
    {
        var minKeep = 3;
        var count = 0;

        foreach (var c in candidates)
            if (c.Perpendicular >= rangeStart && c.Perpendicular <= rangeEnd)
                _filterBuffer[count++] = c.Edge;

        if (count < minKeep) return fallback;

        Array.Sort(_filterBuffer, 0, count);
        return _filterBuffer[count / 2];
    }

    private static bool MatchesVideoAspectRatio(int left, int top, int right, int bottom)
    {
        var contentW = right - left;
        var contentH = bottom - top;
        if (contentW <= 0 || contentH <= 0) return false;

        var ar = (double)contentW / contentH;
        var tolerance = 0.05;

        int[] knownArX100 = { 178, 160, 235, 239, 185, 200, 190, 150, 133, 100, 56, 75 };
        foreach (var kar in knownArX100)
        {
            var diff = Math.Abs(ar - kar / 100.0);
            if (diff <= tolerance) return true;
        }

        return false;
    }

    private struct EdgeCandidate : IComparable<EdgeCandidate>
    {
        public int Edge;
        public int Perpendicular;

        public int CompareTo(EdgeCandidate other)
        {
            return Edge.CompareTo(other.Edge);
        }
    }
}