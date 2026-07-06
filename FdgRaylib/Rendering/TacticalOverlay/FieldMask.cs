using FDG;

namespace FdgRaylib.Rendering.TacticalOverlay;

/// <summary>
/// A CPU raster over the whole table at a fixed texels-per-inch, in world space (x in
/// [0,TableW], z in [0,TableH]). Cells hold a byte: for threat masks 0/1 (union), for opportunity
/// bands the best band index a position achieves (max-blended). World-space + fixed density is what
/// lets the field survive pan/zoom with no rebuild (spec section 5) -- the picture is baked once and
/// merely drawn through the current Layout.
///
/// This is pure geometry -- it never consults game rules. The authoritative determinations live in
/// <see cref="RulesProbe"/>; this class only paints the approximate picture (spec section 0).
/// </summary>
internal sealed class FieldMask
{
    public readonly int   W;
    public readonly int   H;
    public readonly float Tpi;
    public readonly byte[] Cells;

    public FieldMask(int w, int h, float tpi)
    {
        W = w;
        H = h;
        Tpi = tpi;
        Cells = new byte[w * h];
    }

    public void Clear() => System.Array.Clear(Cells, 0, Cells.Length);

    /// <summary>Cell-center world coordinate (inches) for a column/row.</summary>
    public float CellCenterX(int cx) => (cx + 0.5f) / Tpi;
    public float CellCenterZ(int cy) => (cy + 0.5f) / Tpi;

    /// <summary>
    /// Max-blends a filled disc of world radius <paramref name="rIn"/> centred at (<paramref name="cxIn"/>,
    /// <paramref name="czIn"/>) inches into the grid: every cell whose centre falls inside the disc takes
    /// <paramref name="value"/> if it is greater than what's already there. Union of discs = repeated calls
    /// with the same value; nested bands = calls with increasing value (spec section 5).
    /// </summary>
    public void RasterizeDiscMax(float cxIn, float czIn, float rIn, byte value)
    {
        if (rIn <= 0f) return;
        float r2 = rIn * rIn;

        int minCx = System.Math.Max(0,     (int)MathF.Floor((cxIn - rIn) * Tpi));
        int maxCx = System.Math.Min(W - 1, (int)MathF.Ceiling((cxIn + rIn) * Tpi));
        int minCy = System.Math.Max(0,     (int)MathF.Floor((czIn - rIn) * Tpi));
        int maxCy = System.Math.Min(H - 1, (int)MathF.Ceiling((czIn + rIn) * Tpi));

        for (int cy = minCy; cy <= maxCy; cy++)
        {
            float dz = CellCenterZ(cy) - czIn;
            float dz2 = dz * dz;
            int rowBase = cy * W;
            for (int cx = minCx; cx <= maxCx; cx++)
            {
                float dx = CellCenterX(cx) - cxIn;
                if (dx * dx + dz2 <= r2)
                {
                    int idx = rowBase + cx;
                    if (value > Cells[idx]) Cells[idx] = value;
                }
            }
        }
    }
}
