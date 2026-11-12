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
    private Shader _ringShader;   // draw-time band-ring overlay (screen-space edge detection)
    private int _locMask, _locAccent, _locAlphaScale, _locInvSize, _locHatchPeriod, _locHatchLineWidth,
        _locBandBase, _locBandBoost, _locBandFillMax, _locFillWhiteMix;
    private int _locRingBand, _locRingSize, _locRingWidth, _locRingColor, _locRingAlpha;
    private bool _ready;
    private bool _ringReady;
    private bool _hasContent;
    private (byte r, byte g, byte b) _accent;   // last fill accent, reused for the ring color

    public bool Ready => _ready;
    public bool HasContent => _hasContent;

    /// <summary>Clamp for open (+inf) polar buckets, comfortably past the table diagonal.</summary>
    private const float MaxFanReachInches = 100f;

    // Reused triangle-fan vertex buffer (2 perimeter verts per bucket + centre + close), so the per-frame
    // Self-mode path doesn't allocate a ~4k-element array per source per layer.
    private readonly Vector2[] _fanScratch = new Vector2[TacticalOverlayConfig.PolarBuckets * 2 + 2];

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

    // Draw-time band-ring overlay: samples the band-value texture at +-(ring half-width in SCREEN pixels)
    // and lights fragments near a band-value change. Because texelStep is derived from the current zoom,
    // the ring stays a constant thin width at any scale (the texture-baked ring bilinear-stretched into
    // chunkiness -- playtest feedback). texture0 = compositeRT (fill); bandTex = bandRT, v-flipped vs the
    // fill because the composite pass baked it flipped -- edge detection ORs all 4 directions so the flip
    // is immaterial to the result, it only realigns the centre sample.
    private const string RingFs = @"
#version 330
in vec2 fragTexCoord;
out vec4 finalColor;
uniform sampler2D bandTex;   // R = band value (Point-filtered; bilinear done manually below)
uniform vec2 bandTexSize;    // (W, H)
uniform float ringWidthPx;   // desired on-screen line width, constant at any zoom
uniform vec3 ringColor;
uniform float ringAlpha;

// Manual bilinear sample of the band-value field so it varies smoothly across a band edge, letting us
// draw a thin ANTI-ALIASED, constant-screen-width contour line via screen-space derivatives (fwidth).
float bandBilinear(vec2 uv)
{
    vec2 t = uv * bandTexSize - 0.5;
    vec2 f = fract(t);
    vec2 base = (floor(t) + 0.5) / bandTexSize;
    vec2 dx = vec2(1.0 / bandTexSize.x, 0.0);
    vec2 dy = vec2(0.0, 1.0 / bandTexSize.y);
    float b00 = texture(bandTex, base).r * 255.0;
    float b10 = texture(bandTex, base + dx).r * 255.0;
    float b01 = texture(bandTex, base + dy).r * 255.0;
    float b11 = texture(bandTex, base + dx + dy).r * 255.0;
    return mix(mix(b00, b10, f.x), mix(b01, b11, f.x), f.y);
}

void main()
{
    // Sample bandTex at the SAME coord as the fill: compositeRT was built from bandRT through the
    // composite pass's flipped draw, so at draw time (this pass also uses a flipped src) the two line up
    // directly -- an extra v-flip mirrors the rings vertically (harness-verified).
    float b = bandBilinear(fragTexCoord);
    float d = abs(fract(b) - 0.5);   // band-units to the nearest half-integer boundary (edge or band-band)
    float g = fwidth(b);             // band-units per screen pixel
    if (g < 1e-5) { finalColor = vec4(0.0); return; }
    float pxDist = d / g;            // distance to the boundary in screen pixels
    // Solid core of half-width ringWidthPx*0.5, then a 1px anti-aliased fade (avoids a faint/stippled line).
    float a = 1.0 - smoothstep(ringWidthPx * 0.5, ringWidthPx * 0.5 + 1.0, pxDist);
    finalColor = vec4(ringColor, a * ringAlpha);  // transparent off the line -> drawable above terrain
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

            // Ring overlay shader is optional: on failure the field just draws without GPU rings (the
            // controller keeps the CPU vector-ring fallback available).
            _ringShader = Raylib.LoadShaderFromMemory(null, RingFs);
            if (_ringShader.Id != 0 && _ringShader.Id != Rlgl.GetShaderIdDefault())
            {
                _locRingBand  = Raylib.GetShaderLocation(_ringShader, "bandTex");
                _locRingSize  = Raylib.GetShaderLocation(_ringShader, "bandTexSize");
                _locRingWidth = Raylib.GetShaderLocation(_ringShader, "ringWidthPx");
                _locRingColor = Raylib.GetShaderLocation(_ringShader, "ringColor");
                _locRingAlpha = Raylib.GetShaderLocation(_ringShader, "ringAlpha");
                _ringReady = _locRingBand >= 0;
            }

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
        _accent = accent;   // rings reuse the fill accent (selected unit's team color)

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

        Vector2[] pts = _fanScratch;   // sized PolarBuckets*2+2 == n*2+2
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

        Raylib.DrawTriangleFan(pts, n * 2 + 2, color);
    }

    // World inches -> texel-space draw coordinates (top-left origin, z up -> y down).
    private Vector2 ToTexel(float xIn, float zIn) =>
        new(xIn * _tpi, (GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES - zIn) * _tpi);

    /// <summary>True when the GPU draws the band rings itself (the controller then skips CPU vector rings).</summary>
    public bool RingsReady => _ringReady;

    /// <summary>Per-frame draw of the cached fill composite (straight-alpha) through the current layout.
    /// Rings are a separate overlay (<see cref="DrawRings"/>) so they can sit above terrain.</summary>
    public void DrawComposite(float originX, float originY, float tableWIn, float tableHIn, float scale)
    {
        if (!_ready || !_hasContent) return;
        var src = new Rectangle(0, 0, _w, -_h);
        var dst = new Rectangle(originX, originY, tableWIn * scale, tableHIn * scale);
        Raylib.DrawTexturePro(_compositeRT.Texture, src, dst, Vector2.Zero, 0f, Color.White);
    }

    /// <summary>
    /// Draws ONLY the band rings (transparent elsewhere) via screen-space edge detection on the band
    /// texture, so the ring stays a thin constant width at any zoom. Call above terrain, after
    /// DrawComposite. No-op when the ring shader is unavailable (controller uses CPU vector rings then).
    /// </summary>
    public void DrawRings(float originX, float originY, float tableWIn, float tableHIn, float scale)
    {
        if (!_ready || !_ringReady || !_hasContent) return;

        // fwidth-based contour: constant thin screen width at any zoom, no per-zoom texel math needed.
        (byte rr, byte gg, byte bb) = _accent;   // rings match the fill (selected unit's team color)
        Raylib.SetShaderValue(_ringShader, _locRingSize, new Vector2(_w, _h), ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_ringShader, _locRingWidth, TacticalOverlayConfig.BandBoundaryThicknessPx, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_ringShader, _locRingColor, new Vector3(rr / 255f, gg / 255f, bb / 255f), ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_ringShader, _locRingAlpha, TacticalOverlayConfig.BandBoundaryAlpha, ShaderUniformDataType.Float);

        var src = new Rectangle(0, 0, _w, -_h);
        var dst = new Rectangle(originX, originY, tableWIn * scale, tableHIn * scale);
        Raylib.BeginShaderMode(_ringShader);
        Raylib.SetShaderValueTexture(_ringShader, _locRingBand, _bandRT.Texture);
        Raylib.DrawTexturePro(_compositeRT.Texture, src, dst, Vector2.Zero, 0f, Color.White); // quad carrier
        Raylib.EndShaderMode();
    }

    /// <summary>Raw composite texture for the harness readback.</summary>
    public Texture2D CompositeTexture => _compositeRT.Texture;

    public void Dispose()
    {
        if (_ready)
        {
            Raylib.UnloadShader(_shader);
            if (_ringShader.Id != 0 && _ringShader.Id != Rlgl.GetShaderIdDefault())
                Raylib.UnloadShader(_ringShader);
            Raylib.UnloadRenderTexture(_bandRT);
            Raylib.UnloadRenderTexture(_maskRT);
            Raylib.UnloadRenderTexture(_compositeRT);
            _ready = false;
            _ringReady = false;
        }
    }
}
