using System;
using System.Collections.Generic;
using System.Numerics;
using FDG;
using Raylib_cs;

namespace FdgRaylib.Rendering.TacticalOverlay;

/// <summary>
/// GPU rasterizer for the opportunity field (#162 perf rework). Same picture as
/// <see cref="FieldCompositor"/>, produced in three GPU passes instead of a CPU texel loop + upload:
/// <list type="number">
/// <item><b>bandRT</b> -- band discs drawn opaque per band, ascending value, so inner (shorter-range)
/// bands overwrite outer: R channel = band value. The GPU twin of the CPU max-blend.</item>
/// <item><b>maskRT</b> -- per target model, the LIT region as a bucket-stepped triangle fan straight off
/// its <see cref="PolarSightMap"/> (perimeter radius = that bucket's nearest-blocker depth). Additive:
/// R accumulates "visible" fans (blocking layer), G accumulates "clear" fans (blocking+cover layer).
/// Union across sources = "any model sees it", matching the engine's rule.</item>
/// <item><b>compositeRT</b> -- a fullscreen shader pass reproducing FieldCompositor.Compose exactly:
/// band alpha (+inner boost), neighbour band-change boundary lines, transparent where blocked,
/// diagonal hatch where cover. Rendered once per REBUILD, not per frame; the per-frame cost is one
/// textured quad.</item>
/// </list>
/// The composite quad is written with <see cref="BlendMode.AlphaPremultiply"/> (ONE, ONE-MINUS-SRC-ALPHA)
/// onto a transparent-black clear, which stores the shader's output VERBATIM -- raylib's default blend
/// is not separate, so a plain alpha-blend write would square the alpha channel (the harness caught
/// exactly that as a uniformly pale field). compositeRT therefore holds a straight-alpha image and
/// <see cref="DrawComposite"/> draws it exactly like the CPU path draws its texture.
///
/// This is still the *picture* pipeline (spec section 0): it consumes the same PolarSightMaps the CPU
/// path does, pips/counts keep calling the real engine functions, and the offscreen harness
/// (<see cref="FieldHarness"/>) pixel-diffs this against the CPU reference.
/// </summary>
internal sealed class GpuFieldRenderer : IDisposable
{
    private readonly int _w;
    private readonly int _h;
    private readonly float _tpi;

    private RenderTexture2D _bandRT;
    private RenderTexture2D _maskRT;
    private RenderTexture2D _compositeRT;
    private Shader _shader;
    private int _locMask, _locAccent, _locAlphaScale, _locInvSize, _locHatchPeriod, _locHatchLineWidth,
        _locBandBase, _locBandBoost, _locBandFillMax, _locFillWhiteMix;
    private bool _ready;
    private bool _hasContent;

    public bool Ready => _ready;
    public bool HasContent => _hasContent;

    /// <summary>Clamp for open (+inf) polar buckets, comfortably past the table diagonal.</summary>
    private const float MaxFanReachInches = 100f;

    // The composite shader: FieldCompositor.Compose, texel for texel. texture0 = bandRT (R = band value),
    // maskTex = visibility (R>0 visible, G>0 clear). See the class summary for the encoding.
    private const string CompositeFs = @"
#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;
uniform sampler2D texture0;
uniform sampler2D maskTex;
uniform vec3 accent;
uniform float alphaScale;
uniform vec2 invSize;
uniform float hatchPeriod;
uniform float hatchLineWidth;   // crosshatch line thickness in texels, matching FieldCompositor
uniform float bandBase;
uniform float bandBoost;
uniform float bandFillMax;      // fill clamp, = FieldCompositor's BandFillAlphaMax
uniform float fillWhiteMix;     // fill lerps this far toward white (a luminous glow)

// Fill only -- the band boundary RINGS are drawn as vector polylines on top (DrawFieldRings), not baked
// here, so they stay a consistent thin width at any zoom. Matches FieldCompositor.Compose texel for texel.
void main()
{
    float b = texture(texture0, fragTexCoord).r * 255.0;
    if (b < 0.5) { finalColor = vec4(0.0); return; }

    vec2 mask = texture(maskTex, fragTexCoord).rg;
    if (mask.r < 0.004) { finalColor = vec4(0.0); return; }   // blocked: no fill

    float alpha = min(bandFillMax, bandBase + (b - 1.0) * bandBoost);
    if (mask.g < 0.004)   // visible only through cover: crosshatch mesh
    {
        // pix.y here is the TEXTURE-MEMORY row (the composite pass samples with a flipped src rect),
        // which is H-1-imageRow; the CPU hatch uses the image row, so convert -- otherwise the hatch
        // mirrors and the pixel diff lights up half the cover region.
        vec2 size = 1.0 / invSize;
        vec2 pix = floor(fragTexCoord * size);
        float imageRow = size.y - 1.0 - pix.y;
        float d1 = mod(pix.x + imageRow, hatchPeriod);   // GLSL mod is always in [0, period)
        float d2 = mod(pix.x - imageRow, hatchPeriod);
        if (d1 < hatchLineWidth || d2 < hatchLineWidth) alpha = min(0.75, alpha + 0.28);
    }

    vec3 col = mix(accent, vec3(1.0), fillWhiteMix);
    finalColor = vec4(col, alpha * alphaScale);
}
";

    public GpuFieldRenderer(int w, int h, float tpi)
    {
        _w = w;
        _h = h;
        _tpi = tpi;
    }

    /// <summary>Creates GPU resources. Call once, after the window exists. False = no usable shader
    /// (caller should fall back to the CPU path); partially-created resources are unloaded.</summary>
    public bool TryInit()
    {
        bool rtsCreated = false, shaderCreated = false;
        try
        {
            _bandRT      = Raylib.LoadRenderTexture(_w, _h);
            _maskRT      = Raylib.LoadRenderTexture(_w, _h);
            _compositeRT = Raylib.LoadRenderTexture(_w, _h);
            rtsCreated = true;
            Raylib.SetTextureFilter(_bandRT.Texture, TextureFilter.Point);
            Raylib.SetTextureFilter(_maskRT.Texture, TextureFilter.Point);
            Raylib.SetTextureFilter(_compositeRT.Texture, TextureFilter.Bilinear);
            Raylib.SetTextureWrap(_bandRT.Texture, TextureWrap.Clamp); // no edge wrap when sampled

            _shader = Raylib.LoadShaderFromMemory(null, CompositeFs);
            shaderCreated = _shader.Id != 0;
            if (_shader.Id == 0 || _shader.Id == Rlgl.GetShaderIdDefault())
                throw new InvalidOperationException("composite shader failed to compile");

            _locMask        = Raylib.GetShaderLocation(_shader, "maskTex");
            _locAccent      = Raylib.GetShaderLocation(_shader, "accent");
            _locAlphaScale  = Raylib.GetShaderLocation(_shader, "alphaScale");
            _locInvSize     = Raylib.GetShaderLocation(_shader, "invSize");
            _locHatchPeriod = Raylib.GetShaderLocation(_shader, "hatchPeriod");
            _locHatchLineWidth = Raylib.GetShaderLocation(_shader, "hatchLineWidth");
            _locBandBase    = Raylib.GetShaderLocation(_shader, "bandBase");
            _locBandBoost   = Raylib.GetShaderLocation(_shader, "bandBoost");
            _locBandFillMax = Raylib.GetShaderLocation(_shader, "bandFillMax");
            _locFillWhiteMix = Raylib.GetShaderLocation(_shader, "fillWhiteMix");
            if (_locMask < 0)
                throw new InvalidOperationException("maskTex uniform missing");

            _ready = true;
            return true;
        }
        catch
        {
            if (shaderCreated && _shader.Id != Rlgl.GetShaderIdDefault()) Raylib.UnloadShader(_shader);
            if (rtsCreated)
            {
                Raylib.UnloadRenderTexture(_bandRT);
                Raylib.UnloadRenderTexture(_maskRT);
                Raylib.UnloadRenderTexture(_compositeRT);
            }
            return false;
        }
    }

    public void Clear() => _hasContent = false;

    /// <summary>One filled disc of a band region, radius fully inflated by the caller.</summary>
    public readonly record struct BandDisc(float X, float Z, float RadiusInches);

    /// <summary>
    /// Target-anchored rebuild: every band ring is (range + shooter radius + that target's radius)
    /// around every target model -- the standard pinned-field shape.
    /// </summary>
    public void Rebuild(IReadOnlyList<FieldTargetModel> targets, float shooterRadius,
        IReadOnlyList<BandSpec> bands, IReadOnlyList<PolarSightMap> maps,
        (byte r, byte g, byte b) accent, float alphaScale)
    {
        var perBand = new List<(BandSpec band, List<BandDisc> discs)>(bands.Count);
        foreach (BandSpec band in bands)
        {
            var discs = new List<BandDisc>(targets.Count);
            foreach (FieldTargetModel t in targets)
                discs.Add(new BandDisc(t.X, t.Z, band.RangeInches + shooterRadius + t.Radius));
            perBand.Add((band, discs));
        }
        RebuildDiscs(perBand, maps, accent, alphaScale);
    }

    /// <summary>
    /// Rebuilds all three RTs from precomputed per-band discs + polar maps. GPU-cheap (a few hundred
    /// triangles + one fullscreen quad); called when the field's rebuild signature changes -- or every
    /// frame in ghost-anchored mode, which is the point of this renderer.
    /// </summary>
    public void RebuildDiscs(IReadOnlyList<(BandSpec band, List<BandDisc> discs)> perBand,
        IReadOnlyList<PolarSightMap> maps, (byte r, byte g, byte b) accent, float alphaScale)
    {
        if (!_ready) return;
        bool any = false;
        foreach ((_, List<BandDisc> discs) in perBand)
            if (discs.Count > 0) { any = true; break; }
        if (!any) { _hasContent = false; return; }
        _hasContent = true;

        // ---- Pass 1: band values (opaque overwrite, ascending band value = descending range) --------
        var ordered = new List<(BandSpec band, List<BandDisc> discs)>(perBand);
        ordered.Sort((a, b) => a.band.Value.CompareTo(b.band.Value));

        Raylib.BeginTextureMode(_bandRT);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));
        foreach ((BandSpec band, List<BandDisc> discs) in ordered)
        {
            var col = new Color((byte)band.Value, (byte)0, (byte)0, (byte)255);
            foreach (BandDisc d in discs)
            {
                float rTex = d.RadiusInches * _tpi;
                Vector2 c = ToTexel(d.X, d.Z);
                int segments = Math.Clamp((int)(rTex * 0.5f), 64, 256);
                Raylib.DrawCircleSector(c, rTex, 0f, 360f, segments, col);
            }
        }
        Raylib.EndTextureMode();

        // ---- Pass 2: visibility mask (additive union of per-source fans) ---------------------------
        Raylib.BeginTextureMode(_maskRT);
        if (maps.Count == 0)
        {
            // No blockers anywhere: everything visible and clear.
            Raylib.ClearBackground(new Color(255, 255, 0, 255));
        }
        else
        {
            Raylib.ClearBackground(new Color(0, 0, 0, 255));
            Raylib.BeginBlendMode(BlendMode.Additive);
            foreach (PolarSightMap map in maps)
            {
                DrawDepthFan(map, useCoverLayer: false, new Color(255, 0, 0, 255)); // visible (not blocked)
                DrawDepthFan(map, useCoverLayer: true, new Color(0, 255, 0, 255));  // clear (nothing at all)
            }
            Raylib.EndBlendMode();
        }
        Raylib.EndTextureMode();

        // ---- Pass 3: composite shader quad ----------------------------------------------------------
        Vector3 accentV = new(accent.r / 255f, accent.g / 255f, accent.b / 255f);
        Vector2 invSize = new(1f / _w, 1f / _h);
        Raylib.SetShaderValue(_shader, _locAccent, accentV, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _locAlphaScale, alphaScale, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locInvSize, invSize, ShaderUniformDataType.Vec2);
        // Match FieldCompositor exactly: period via int truncation, threshold via int division.
        int hatchPeriod = Math.Max(2, (int)(TacticalOverlayConfig.HatchSpacingInches * _tpi));
        Raylib.SetShaderValue(_shader, _locHatchPeriod, (float)hatchPeriod, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locHatchLineWidth, (float)TacticalOverlayConfig.HatchLineWidthTexels, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locBandBase, TacticalOverlayConfig.BandFillAlpha, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locBandBoost, TacticalOverlayConfig.BandInnerAlphaBoost, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locBandFillMax, TacticalOverlayConfig.BandFillAlphaMax, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locFillWhiteMix, TacticalOverlayConfig.BandFillWhiteMix, ShaderUniformDataType.Float);

        Raylib.BeginTextureMode(_compositeRT);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));
        // (ONE, ONE-MINUS-SRC-ALPHA) onto transparent black = write shader output verbatim, keeping the
        // RT straight-alpha. Default alpha blend here would square the alpha (harness-caught).
        Raylib.BeginBlendMode(BlendMode.AlphaPremultiply);
        Raylib.BeginShaderMode(_shader);
        Raylib.SetShaderValueTexture(_shader, _locMask, _maskRT.Texture);
        // Both source RTs share the same in-memory orientation, so sampling maskTex at fragTexCoord
        // stays aligned with texture0 regardless of the flip on this draw.
        Raylib.DrawTextureRec(_bandRT.Texture, new Rectangle(0, 0, _w, -_h), Vector2.Zero, Color.White);
        Raylib.EndShaderMode();
        Raylib.EndBlendMode();
        Raylib.EndTextureMode();
    }

    /// <summary>
    /// One source's lit region as a bucket-stepped triangle fan: perimeter at radius = the bucket's
    /// nearest-interruption depth (blocking layer, or min(blocking, cover) for the clear layer). Two
    /// perimeter vertices per bucket reproduce the map's bucket-constant semantics exactly.
    /// </summary>
    private void DrawDepthFan(PolarSightMap map, bool useCoverLayer, Color color)
    {
        int n = map.BucketCount;
        float[] block = map.BlockDepthRaw;
        float[] cov = map.CoverDepthRaw;

        var pts = new Vector2[n * 2 + 2];
        pts[0] = ToTexel(map.Source.X, map.Source.Y);

        int k = 1;
        for (int i = 0; i < n; i++)
        {
            float d = useCoverLayer ? MathF.Min(block[i], cov[i]) : block[i];
            if (d > MaxFanReachInches) d = MaxFanReachInches;

            float a0 = map.BucketEdgeAngle(i);
            float a1 = map.BucketEdgeAngle(i + 1);
            pts[k++] = ToTexel(map.Source.X + MathF.Cos(a0) * d, map.Source.Y + MathF.Sin(a0) * d);
            pts[k++] = ToTexel(map.Source.X + MathF.Cos(a1) * d, map.Source.Y + MathF.Sin(a1) * d);
        }
        // Close the fan back to the first perimeter point.
        pts[k] = pts[1];

        Raylib.DrawTriangleFan(pts, pts.Length, color);
    }

    // World inches -> texel-space draw coordinates (top-left origin, z up -> y down).
    private Vector2 ToTexel(float xIn, float zIn) =>
        new(xIn * _tpi, (GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES - zIn) * _tpi);

    /// <summary>Per-frame draw of the cached composite through the current layout. The RT is
    /// straight-alpha, so this is the same default-blend draw the CPU path uses.</summary>
    public void DrawComposite(float originX, float originY, float tableWIn, float tableHIn, float scale)
    {
        if (!_ready || !_hasContent) return;
        var src = new Rectangle(0, 0, _w, -_h);
        var dst = new Rectangle(originX, originY, tableWIn * scale, tableHIn * scale);
        Raylib.DrawTexturePro(_compositeRT.Texture, src, dst, Vector2.Zero, 0f, Color.White);
    }

    /// <summary>Raw composite texture for the harness readback.</summary>
    public Texture2D CompositeTexture => _compositeRT.Texture;

    public void Dispose()
    {
        if (_ready)
        {
            Raylib.UnloadShader(_shader);
            Raylib.UnloadRenderTexture(_bandRT);
            Raylib.UnloadRenderTexture(_maskRT);
            Raylib.UnloadRenderTexture(_compositeRT);
            _ready = false;
        }
    }
}
