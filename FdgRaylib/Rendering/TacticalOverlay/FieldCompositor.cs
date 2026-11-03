using System;
using System.Numerics;
using Raylib_cs;

namespace FdgRaylib.Rendering.TacticalOverlay;

/// <summary>
/// Turns a band <see cref="FieldMask"/> into a cached world-space texture and draws it through the
/// current Layout (spec section 5, "analytic geometry into cached textures"). Bilinear-filtered so the
/// grid downsamples with antialiased edges. The texture is baked only on rebuild; every frame just
/// draws it -- pan/zoom/resize rescale the draw rect, never the texture.
///
/// Boundary lines come from neighbour-texel band-change detection (the plan's primary for the focused
/// field): a texel whose orthogonal neighbour holds a different band is drawn near-opaque, giving crisp
/// nested outlines without a separate polyline pass.
/// </summary>
internal sealed class FieldCompositor : IDisposable
{
    private readonly int _w;
    private readonly int _h;
    private readonly byte[] _pixels;   // RGBA; image row 0 = screen top = world z max (vertical flip baked in)
    private Texture2D _texture;
    private bool _texReady;
    private bool _dirty;
    private bool _hasContent;

    public bool HasContent => _hasContent;

    /// <summary>Raw straight-alpha RGBA pixel buffer (row 0 = image top). Read by the GPU-diff harness
    /// as the reference picture; do not mutate.</summary>
    public byte[] Pixels => _pixels;

    public FieldCompositor(int w, int h)
    {
        _w = w;
        _h = h;
        _pixels = new byte[w * h * 4];
    }

    public void Clear()
    {
        if (!_hasContent) return;
        Array.Clear(_pixels, 0, _pixels.Length);
        _hasContent = false;
        _dirty = true;
    }

    /// <summary>
    /// Rebuilds the pixel buffer from the band mask in the pin's accent (preview dims via alphaScale).
    /// <paramref name="shadow"/> cells (no LoS to the target) are punched transparent -- no fill where
    /// the shot is blocked. <paramref name="cover"/> cells (shot through cover) get diagonal world-space
    /// hatching. Boundary texels (band-value change) draw near-opaque for crisp nested outlines.
    /// </summary>
    public void Compose(FieldMask bands, FieldMask shadow, FieldMask cover,
        (byte r, byte g, byte b) accent, float alphaScale)
    {
        Array.Clear(_pixels, 0, _pixels.Length);
        _hasContent = false;

        int W = _w, H = _h;
        byte[] cells   = bands.Cells;
        byte[] shadowC = shadow.Cells;
        byte[] coverC  = cover.Cells;
        int hatchPeriod = Math.Max(2, (int)(TacticalOverlayConfig.HatchSpacingInches * bands.Tpi));

        for (int cy = 0; cy < H; cy++)
        {
            int inRow  = cy * W;
            int outRow = (H - 1 - cy) * W; // vertical flip: world z increases upward, image rows go down
            for (int cx = 0; cx < W; cx++)
            {
                byte band = cells[inRow + cx];
                if (band == 0) continue;
                if (shadowC[inRow + cx] != 0) continue; // blocked -> no fill (spec section 1)
                _hasContent = true;

                // Fill only -- the band boundary rings are drawn as crisp VECTOR polylines on top (see
                // DrawFieldRings), not baked here, so they stay a consistent thin width at any zoom.
                float alpha = Math.Min(TacticalOverlayConfig.BandFillAlphaMax,
                    TacticalOverlayConfig.BandFillAlpha + (band - 1) * TacticalOverlayConfig.BandInnerAlphaBoost);

                // Cover crosshatch: thin lines in BOTH diagonal directions (a mesh) so it reads the same
                // against a band edge at any angle. Must match GpuFieldRenderer's shader exactly.
                if (coverC[inRow + cx] != 0)
                {
                    int imageRow = H - 1 - cy;
                    int lw = TacticalOverlayConfig.HatchLineWidthTexels;
                    int d1 = (cx + imageRow) % hatchPeriod;
                    int d2 = (((cx - imageRow) % hatchPeriod) + hatchPeriod) % hatchPeriod;
                    if (d1 < lw || d2 < lw) alpha = Math.Min(0.75f, alpha + 0.28f);
                }

                alpha *= alphaScale;

                // Fill = accent lerped toward white (a luminous glow).
                float mix = TacticalOverlayConfig.BandFillWhiteMix;
                byte cr = (byte)(accent.r + (255 - accent.r) * mix);
                byte cg = (byte)(accent.g + (255 - accent.g) * mix);
                byte cb = (byte)(accent.b + (255 - accent.b) * mix);

                int o = (outRow + cx) * 4;
                _pixels[o + 0] = cr;
                _pixels[o + 1] = cg;
                _pixels[o + 2] = cb;
                _pixels[o + 3] = (byte)(alpha * 255f);
            }
        }
        _dirty = true;
    }

    public void Draw(float originX, float originY, float tableW, float tableH, float scale)
    {
        if (!_hasContent) return;
        EnsureTexture();
        if (_dirty)
        {
            Raylib.UpdateTexture<byte>(_texture, _pixels);
            _dirty = false;
        }

        var src = new Rectangle(0, 0, _w, _h);
        var dst = new Rectangle(originX, originY, tableW * scale, tableH * scale);
        Raylib.DrawTexturePro(_texture, src, dst, Vector2.Zero, 0f, Color.White);
    }

    private void EnsureTexture()
    {
        if (_texReady) return;
        Image img = Raylib.GenImageColor(_w, _h, new Color(0, 0, 0, 0));
        _texture = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(_texture, TextureFilter.Bilinear);
        _texReady = true;
    }

    public void Dispose()
    {
        if (_texReady)
        {
            Raylib.UnloadTexture(_texture);
            _texReady = false;
        }
    }
}
