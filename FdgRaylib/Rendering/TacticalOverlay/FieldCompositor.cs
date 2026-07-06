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

                bool boundary =
                    (cx == 0     || cells[inRow + cx - 1] != band) ||
                    (cx == W - 1 || cells[inRow + cx + 1] != band) ||
                    (cy == 0     || cells[(cy - 1) * W + cx] != band) ||
                    (cy == H - 1 || cells[(cy + 1) * W + cx] != band);

                float alpha = boundary
                    ? 0.85f
                    : Math.Min(0.6f, TacticalOverlayConfig.BandFillAlpha + (band - 1) * TacticalOverlayConfig.BandInnerAlphaBoost);

                // Cover hatch: diagonal stripes in image space -> world-space diagonals when drawn.
                if (!boundary && coverC[inRow + cx] != 0)
                {
                    int imageRow = H - 1 - cy;
                    bool onStripe = ((cx + imageRow) % hatchPeriod) < hatchPeriod / 2;
                    alpha = onStripe ? Math.Min(0.75f, alpha + 0.28f) : alpha * 0.55f;
                }

                alpha *= alphaScale;

                int o = (outRow + cx) * 4;
                _pixels[o + 0] = accent.r;
                _pixels[o + 1] = accent.g;
                _pixels[o + 2] = accent.b;
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
