using System.Collections.Generic;
using FDG;

namespace FdgRaylib.Rendering.TacticalOverlay;

/// <summary>
/// Extracts the boundary of a raster region (cells >= threshold) as joined, simplified polylines in
/// world inches. CPU marching squares (spec section 5): preferred over GPU edge detection because the
/// polylines are what the threat frontiers dash and the secondary-pin contours draw, and dashing needs
/// arc-length traversal along a continuous line, not loose per-cell segments.
///
/// Points are returned as <see cref="Float2"/> with X = world x, Y = world z.
/// </summary>
internal static class MarchingSquares
{
    /// <summary>
    /// Boundary polylines of { cell >= threshold } in <paramref name="mask"/>. Segments are emitted per
    /// 2x2 cell-centre block (the classic 16-case table), joined into polylines by exact endpoint keys
    /// (all crossings land on a half-cell lattice, so keys are collision-free), then Douglas-Peucker
    /// simplified when <paramref name="simplifyEpsilonInches"/> &gt; 0.
    /// </summary>
    public static List<List<Float2>> Extract(FieldMask mask, byte threshold, float simplifyEpsilonInches)
    {
        int W = mask.W, H = mask.H;
        float tpi = mask.Tpi;
        byte[] cells = mask.Cells;

        // Segment endpoints keyed on the half-cell lattice: multiply world coords by (2 * tpi) and round
        // -> exact integers (bottom/top mids are integer columns, left/right mids half-integer, etc).
        var segA = new List<long>();
        var segB = new List<long>();
        var pointByKey = new Dictionary<long, Float2>();
        // key -> list of segment indices touching it (for chain walking)
        var adjacency = new Dictionary<long, List<int>>();

        long Key(float xIn, float zIn)
        {
            int kx = (int)MathF.Round(xIn * 2f * tpi);
            int kz = (int)MathF.Round(zIn * 2f * tpi);
            return (long)kx * 4_000_003L + kz;
        }

        void AddSeg(float ax, float az, float bx, float bz)
        {
            long ka = Key(ax, az), kb = Key(bx, bz);
            if (ka == kb) return;
            pointByKey[ka] = new Float2(ax, az);
            pointByKey[kb] = new Float2(bx, bz);
            int idx = segA.Count;
            segA.Add(ka);
            segB.Add(kb);
            (adjacency.TryGetValue(ka, out var la) ? la : adjacency[ka] = new List<int>()).Add(idx);
            (adjacency.TryGetValue(kb, out var lb) ? lb : adjacency[kb] = new List<int>()).Add(idx);
        }

        bool Inside(int cx, int cy) => cells[cy * W + cx] >= threshold;

        for (int cy = 0; cy < H - 1; cy++)
        {
            float zB = mask.CellCenterZ(cy);       // corner rows
            float zT = mask.CellCenterZ(cy + 1);
            float zMid = (zB + zT) * 0.5f;
            for (int cx = 0; cx < W - 1; cx++)
            {
                bool bl = Inside(cx, cy);
                bool br = Inside(cx + 1, cy);
                bool tr = Inside(cx + 1, cy + 1);
                bool tl = Inside(cx, cy + 1);
                int c = (bl ? 1 : 0) | (br ? 2 : 0) | (tr ? 4 : 0) | (tl ? 8 : 0);
                if (c == 0 || c == 15) continue;

                float xL = mask.CellCenterX(cx);
                float xR = mask.CellCenterX(cx + 1);
                float xMid = (xL + xR) * 0.5f;

                // Edge midpoints: B(ottom), R(ight), T(op), L(eft).
                float Bx = xMid, Bz = zB;
                float Rx = xR,   Rz = zMid;
                float Tx = xMid, Tz = zT;
                float Lx = xL,   Lz = zMid;

                switch (c)
                {
                    case 1:  AddSeg(Lx, Lz, Bx, Bz); break;
                    case 2:  AddSeg(Bx, Bz, Rx, Rz); break;
                    case 3:  AddSeg(Lx, Lz, Rx, Rz); break;
                    case 4:  AddSeg(Rx, Rz, Tx, Tz); break;
                    case 5:  AddSeg(Lx, Lz, Bx, Bz); AddSeg(Rx, Rz, Tx, Tz); break; // saddle
                    case 6:  AddSeg(Bx, Bz, Tx, Tz); break;
                    case 7:  AddSeg(Lx, Lz, Tx, Tz); break;
                    case 8:  AddSeg(Tx, Tz, Lx, Lz); break;
                    case 9:  AddSeg(Bx, Bz, Tx, Tz); break;
                    case 10: AddSeg(Bx, Bz, Rx, Rz); AddSeg(Tx, Tz, Lx, Lz); break; // saddle
                    case 11: AddSeg(Tx, Tz, Rx, Rz); break;
                    case 12: AddSeg(Lx, Lz, Rx, Rz); break;
                    case 13: AddSeg(Bx, Bz, Rx, Rz); break;
                    case 14: AddSeg(Lx, Lz, Bx, Bz); break;
                }
            }
        }

        // Walk segments into polylines: from an unused segment, extend forward off one end then the other.
        var used = new bool[segA.Count];
        var polylines = new List<List<Float2>>();

        int FindUnused(long key, int excluding)
        {
            if (!adjacency.TryGetValue(key, out var list)) return -1;
            foreach (int s in list)
                if (s != excluding && !used[s]) return s;
            return -1;
        }

        for (int s = 0; s < segA.Count; s++)
        {
            if (used[s]) continue;
            used[s] = true;

            var pts = new List<Float2> { pointByKey[segA[s]], pointByKey[segB[s]] };

            // Extend forward off segB[s].
            long endKey = segB[s];
            int prev = s;
            while (true)
            {
                int next = FindUnused(endKey, prev);
                if (next < 0) break;
                used[next] = true;
                long other = segA[next] == endKey ? segB[next] : segA[next];
                pts.Add(pointByKey[other]);
                endKey = other;
                prev = next;
            }

            // Extend backward off segA[s] (prepend).
            long startKey = segA[s];
            prev = s;
            while (true)
            {
                int next = FindUnused(startKey, prev);
                if (next < 0) break;
                used[next] = true;
                long other = segA[next] == startKey ? segB[next] : segA[next];
                pts.Insert(0, pointByKey[other]);
                startKey = other;
                prev = next;
            }

            if (simplifyEpsilonInches > 0f && pts.Count > 2)
                pts = Simplify(pts, simplifyEpsilonInches);

            if (pts.Count >= 2) polylines.Add(pts);
        }

        return polylines;
    }

    // Douglas-Peucker on an open polyline (closed loops are treated open -- a negligible seam artifact
    // for these organic contours). Iterative to avoid deep recursion on long boundaries.
    private static List<Float2> Simplify(List<Float2> pts, float eps)
    {
        int n = pts.Count;
        var keep = new bool[n];
        keep[0] = true;
        keep[n - 1] = true;

        var stack = new Stack<(int lo, int hi)>();
        stack.Push((0, n - 1));
        float eps2 = eps * eps;

        while (stack.Count > 0)
        {
            var (lo, hi) = stack.Pop();
            if (hi <= lo + 1) continue;

            float maxD = -1f;
            int maxI = -1;
            Float2 a = pts[lo], b = pts[hi];
            float abx = b.X - a.X, abz = b.Y - a.Y;
            float abLen2 = abx * abx + abz * abz;

            for (int i = lo + 1; i < hi; i++)
            {
                Float2 p = pts[i];
                float d2;
                if (abLen2 < 1e-12f)
                {
                    float dx = p.X - a.X, dz = p.Y - a.Y;
                    d2 = dx * dx + dz * dz;
                }
                else
                {
                    float t = ((p.X - a.X) * abx + (p.Y - a.Y) * abz) / abLen2;
                    t = System.Math.Clamp(t, 0f, 1f);
                    float projx = a.X + t * abx, projz = a.Y + t * abz;
                    float dx = p.X - projx, dz = p.Y - projz;
                    d2 = dx * dx + dz * dz;
                }
                if (d2 > maxD) { maxD = d2; maxI = i; }
            }

            if (maxD > eps2 && maxI > 0)
            {
                keep[maxI] = true;
                stack.Push((lo, maxI));
                stack.Push((maxI, hi));
            }
        }

        var outPts = new List<Float2>();
        for (int i = 0; i < n; i++)
            if (keep[i]) outPts.Add(pts[i]);
        return outPts;
    }
}
