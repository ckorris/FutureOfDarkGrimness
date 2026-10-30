using System.Numerics;
using Raylib_cs;

namespace FdgRaylib.Rendering.MenuBackground;

/// <summary>
/// Paints the main menu's holographic war table (#448) behind the ImGui menu: the
/// <see cref="HoloTableScene"/> projected through a <see cref="HoloCamera"/> fitted to
/// <see cref="MainMenuLayout.TableRegion"/>, drawn as additive glowing lines, then bloomed, scanlined,
/// vignetted and darkened behind the button panel.
///
/// <para>Pipeline: the scene is drawn into a supersampled target (2x up to ~1200p, so line edges come out
/// smooth without MSAA), box-filtered down to screen size, then blurred at half and quarter size by a
/// separable Gaussian shader; the blurs are added back over the sharp image for the glow. The whole
/// pipeline runs every frame, since the fire, tracers, pulse and measuring line animate (slice 3) - a
/// couple of thousand lines and five small blur passes. If the blur shader will not compile the table
/// is still drawn, just without glow.</para>
/// </summary>
internal sealed class MenuBackgroundRenderer : IDisposable
{
    private static readonly Vector3 TerrainRgb = new(0.20f, 0.72f, 1.00f);
    private static readonly Vector3 FoliageRgb = new(0.30f, 0.85f, 0.95f);
    private static readonly Vector3 RimTopRgb = new(0.55f, 0.90f, 1.00f);
    private static readonly Vector3 BracketRgb = new(0.80f, 0.97f, 1.00f);
    private static readonly Vector3 SurfaceRgb = new(0.016f, 0.040f, 0.070f);
    private static readonly Color BaseColor = new(2, 3, 6, 255);
    private static readonly Vector3 TracerRgb = new(1.00f, 0.97f, 0.88f);   // white-hot: reads on any army colour
    private static readonly Vector3 MeasureRgb = new(0.90f, 0.95f, 1.00f);
    private static readonly Vector3 PulseRgb = new(0.50f, 0.90f, 1.00f);

    // Animation timings, seconds. The pulse sweeps out to PulseReach inches and starts again; tracers
    // cross their line once per TracerCycle; the measuring line draws, holds, fades, rests.
    private const float PulsePeriod = 4.5f;
    private const float PulseReach = 26f;
    private const float TracerCycle = 0.9f;
    private const float MeasureCycle = 7f, MeasureDraw = 1.2f, MeasureHold = 5.5f, MeasureFade = 6.3f;

    // Separable 9-tap Gaussian folded into 5 bilinear fetches (offsets/weights from the standard
    // linear-sampling derivation). texelStep carries both the direction and the spread. threshold is a
    // bright-pass applied to every tap: only light above it blooms, so the dense faint grid and the
    // projector pool stay crisp instead of smearing into a blue haze. Zero on every pass but the first.
    private const string BlurFs = @"
#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;
uniform sampler2D texture0;
uniform vec2 texelStep;
uniform float threshold;
vec3 tap(vec2 uv) { return max(texture(texture0, uv).rgb - vec3(threshold), vec3(0.0)); }
void main()
{
    vec3 c = tap(fragTexCoord) * 0.2270270270;
    c += (tap(fragTexCoord + texelStep * 1.3846153846) + tap(fragTexCoord - texelStep * 1.3846153846)) * 0.3162162162;
    c += (tap(fragTexCoord + texelStep * 3.2307692308) + tap(fragTexCoord - texelStep * 3.2307692308)) * 0.0702702703;
    finalColor = vec4(c, 1.0) * fragColor;
}";

    /// <summary>Bright-pass level for the glow: roughly the 6" grid's brightness, so grid lines stay sharp
    /// and only terrain, rim and labels bloom.</summary>
    private const float BloomThreshold = 0.16f;

    private readonly HoloTableScene _scene = HoloTableScene.BuildDefault();
    private readonly Random _rng;
    private HoloSkirmish _skirmish;
    private HoloEngagement _engagement;

    private int _w, _h, _ss;
    private bool _haveTargets;
    private RenderTexture2D _sceneRT;   // supersampled scene
    private RenderTexture2D _imageRT;   // scene at screen size
    private RenderTexture2D _halfA, _halfB, _quarterA, _quarterB;
    private RenderTexture2D _finalRT;   // the finished frame

    private bool _triedInit;
    private Shader _blur;
    private int _locTexelStep = -1;
    private int _locThreshold = -1;
    private bool _blurReady;
    private Font _font;
    private bool _fontLoaded;
    private Texture2D _radial;          // soft white disc fading to clear: the projector glow pool
    private bool _radialReady;

    /// <param name="seed">Fixes the army pairing sequence (the offscreen preview); null = random.</param>
    public MenuBackgroundRenderer(int? seed = null)
    {
        _rng = seed is { } s ? new Random(s) : new Random();
        _skirmish = HoloSkirmish.Random(_rng);
        _engagement = HoloEngagement.Plan(_skirmish, _scene.Footprints);
    }

    /// <summary>Freezes the animation clock at this many seconds (the offscreen preview); null = live.</summary>
    internal float? FixedTime { get; set; }

    /// <summary>The armies currently on the table.</summary>
    public HoloSkirmish Skirmish => _skirmish;

    /// <summary>Deals a new pair of armies onto the table. The renderer calls this each time the menu
    /// comes back into view, so every visit shows a different matchup.</summary>
    public void Reshuffle()
    {
        _skirmish = HoloSkirmish.Random(_rng);
        _engagement = HoloEngagement.Plan(_skirmish, _scene.Footprints);
    }

    /// <summary>Draws the background over the whole screen. Call inside BeginDrawing, before ImGui.</summary>
    public void Draw(int screenW, int screenH, MainMenuLayout layout)
    {
        Prepare(screenW, screenH, layout);
        Present();
    }

    /// <summary>
    /// Renders this frame into the background's own targets. It switches render targets on its own and
    /// leaves the default framebuffer bound, so it must not run inside the caller's BeginTextureMode -
    /// a caller drawing the menu into a texture (the offscreen preview) prepares first, then presents
    /// inside its own target.
    /// </summary>
    public void Prepare(int screenW, int screenH, MainMenuLayout layout)
    {
        if (screenW <= 0 || screenH <= 0) return;
        EnsureInit();
        if (!_haveTargets || screenW != _w || screenH != _h) Resize(screenW, screenH);
        RenderFrame(layout, FixedTime ?? (float)Raylib.GetTime());
    }

    /// <summary>Draws the frame <see cref="Prepare"/> rendered over the whole of the current target.</summary>
    public void Present()
    {
        if (_haveTargets) DrawTargetFlipped(_finalRT, new Rectangle(0, 0, _w, _h), Color.White);
    }

    private void EnsureInit()
    {
        if (_triedInit) return;
        _triedInit = true;

        _blur = Raylib.LoadShaderFromMemory(null, BlurFs);
        _blurReady = _blur.Id != 0 && _blur.Id != Rlgl.GetShaderIdDefault();
        if (_blurReady)
        {
            _locTexelStep = Raylib.GetShaderLocation(_blur, "texelStep");
            _locThreshold = Raylib.GetShaderLocation(_blur, "threshold");
        }
        else Console.Error.WriteLine("[menu-background] blur shader failed to compile; drawing without glow");

        string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DejaVuSans.ttf");
        if (File.Exists(fontPath))
        {
            _font = Raylib.LoadFontEx(fontPath, 48, null, 0);
            _fontLoaded = _font.Texture.Id != 0;
            if (_fontLoaded) Raylib.SetTextureFilter(_font.Texture, TextureFilter.Bilinear);
        }

        Image radial = Raylib.GenImageGradientRadial(256, 256, 0f, Color.White, new Color(255, 255, 255, 0));
        _radial = Raylib.LoadTextureFromImage(radial);
        Raylib.UnloadImage(radial);
        Raylib.SetTextureFilter(_radial, TextureFilter.Bilinear);
        _radialReady = _radial.Id != 0;
    }

    private void Resize(int w, int h)
    {
        UnloadTargets();
        _w = w;
        _h = h;
        _ss = h <= 1200 ? 2 : 1;
        _sceneRT = Raylib.LoadRenderTexture(w * _ss, h * _ss);
        _imageRT = Raylib.LoadRenderTexture(w, h);
        _halfA = Raylib.LoadRenderTexture(Math.Max(1, w / 2), Math.Max(1, h / 2));
        _halfB = Raylib.LoadRenderTexture(Math.Max(1, w / 2), Math.Max(1, h / 2));
        _quarterA = Raylib.LoadRenderTexture(Math.Max(1, w / 4), Math.Max(1, h / 4));
        _quarterB = Raylib.LoadRenderTexture(Math.Max(1, w / 4), Math.Max(1, h / 4));
        _finalRT = Raylib.LoadRenderTexture(w, h);
        foreach (RenderTexture2D rt in new[] { _sceneRT, _imageRT, _halfA, _halfB, _quarterA, _quarterB, _finalRT })
        {
            Raylib.SetTextureFilter(rt.Texture, TextureFilter.Bilinear);
            Raylib.SetTextureWrap(rt.Texture, TextureWrap.Clamp);
        }
        _haveTargets = true;
    }

    private void UnloadTargets()
    {
        if (!_haveTargets) return;
        foreach (RenderTexture2D rt in new[] { _sceneRT, _imageRT, _halfA, _halfB, _quarterA, _quarterB, _finalRT })
            Raylib.UnloadRenderTexture(rt);
        _haveTargets = false;
    }

    private void RenderFrame(MainMenuLayout layout, float time)
    {
        ScreenRect region = layout.TableRegion;
        HoloCamera cam = HoloCamera.FitToRegion(new Vector3(0f, 1f, 0f), HoloTableScene.SlabCorners,
            region.X, region.Y, region.W, region.H, _h);
        float px = _h / 1080f;   // authored line widths are 1080p pixels

        // 1. The sharp scene, supersampled. Everything glows, so it all adds.
        Raylib.BeginTextureMode(_sceneRT);
        Raylib.ClearBackground(BaseColor);
        Raylib.BeginBlendMode(BlendMode.Additive);
        DrawGlowPool(cam);
        float pulsePhase = time % PulsePeriod / PulsePeriod;
        DrawScene(cam, px * _ss, pulsePhase);
        DrawUnits(cam, px * _ss);
        DrawEngagement(cam, px * _ss, time, pulsePhase);
        Raylib.EndBlendMode();
        Raylib.EndTextureMode();

        // 2. Down to screen size: a 2:1 bilinear draw averages each 2x2 block.
        Blit(_sceneRT, _imageRT);

        // 3. Glow: blur at half and quarter size.
        if (_blurReady)
        {
            Blit(_imageRT, _halfA);
            BlurInto(_halfA, _halfB, new Vector2(1f, 0f), 1.5f, BloomThreshold);
            BlurInto(_halfB, _halfA, new Vector2(0f, 1f), 1.5f);
            Blit(_halfA, _quarterA);
            for (int pass = 0; pass < 2; pass++)
            {
                BlurInto(_quarterA, _quarterB, new Vector2(1f, 0f), 2f);
                BlurInto(_quarterB, _quarterA, new Vector2(0f, 1f), 2f);
            }
        }

        // 4. Compose: sharp image + glow, then the screen treatments.
        Raylib.BeginTextureMode(_finalRT);
        Raylib.ClearBackground(Color.Black);
        DrawTargetFlipped(_imageRT, new Rectangle(0, 0, _w, _h), Color.White);
        if (_blurReady)
        {
            Raylib.BeginBlendMode(BlendMode.Additive);
            DrawTargetFlipped(_halfA, new Rectangle(0, 0, _w, _h), new Color(255, 255, 255, 210));
            DrawTargetFlipped(_quarterA, new Rectangle(0, 0, _w, _h), new Color(255, 255, 255, 255));
            DrawTargetFlipped(_quarterA, new Rectangle(0, 0, _w, _h), new Color(255, 255, 255, 90));
            Raylib.EndBlendMode();
        }
        DrawScreenTreatments(layout);
        Raylib.EndTextureMode();
    }

    // ---- scene ----

    private static Color Glow(Vector3 rgb, float intensity, byte alpha = 255)
    {
        Vector3 c = Vector3.Clamp(rgb * intensity, Vector3.Zero, Vector3.One) * 255f;
        return new Color((byte)c.X, (byte)c.Y, (byte)c.Z, alpha);
    }

    private static Vector3 TintRgb(EHoloTint tint) => tint switch
    {
        EHoloTint.Foliage => FoliageRgb,
        EHoloTint.RimTop => RimTopRgb,
        EHoloTint.Bracket => BracketRgb,
        EHoloTint.Surface => SurfaceRgb,
        _ => TerrainRgb,
    };

    /// <summary>A faint pool of projector light under the table, so the slab reads as lit from below.</summary>
    private void DrawGlowPool(HoloCamera cam)
    {
        if (!_radialReady) return;
        Vector2 c = cam.Project(Vector3.Zero) * _ss;
        float rw = _w * 0.62f * _ss, rh = _h * 0.72f * _ss;
        Raylib.DrawTexturePro(_radial, new Rectangle(0, 0, _radial.Width, _radial.Height),
            new Rectangle(c.X - rw, c.Y - rh, rw * 2f, rh * 2f), Vector2.Zero, 0f, new Color(5, 13, 24, 255));
    }

    /// <summary>How much brighter a terrain piece glows as the pulse ring passes over it (1 = not at all).</summary>
    private float PulseBoost(int piece, float pulsePhase)
    {
        if (piece < 0) return 1f;
        float d = Vector2.Distance(_scene.PieceCentres[piece], _engagement.PulseCentre);
        float r = pulsePhase * PulseReach;
        return 1f + 1.3f * (1f - pulsePhase) * MathF.Exp(-MathF.Pow((d - r) / 3f, 2f));
    }

    private void DrawScene(HoloCamera cam, float widthScale, float pulsePhase)
    {
        Vector2 P(Vector3 p) => cam.Project(p) * _ss;

        // Fills under lines. Projection can flip a quad's winding, so culling is off for these.
        Rlgl.DisableBackfaceCulling();
        foreach (HoloFill f in _scene.Fills)
        {
            Color col = Glow(TintRgb(f.Tint), f.Intensity * PulseBoost(f.Piece, pulsePhase));
            Vector2 a = P(f.A), b = P(f.B), c = P(f.C), d = P(f.D);
            Raylib.DrawTriangle(a, b, c, col);
            Raylib.DrawTriangle(a, c, d, col);
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();

        foreach (HoloLine l in _scene.Lines)
            Raylib.DrawLineEx(P(l.A), P(l.B), MathF.Max(1f, l.Width * widthScale),
                Glow(TintRgb(l.Tint), l.Intensity * PulseBoost(l.Piece, pulsePhase)));

        Font font = _fontLoaded ? _font : Raylib.GetFontDefault();
        float size = 13f * widthScale;
        foreach (HoloLabel label in _scene.Labels)
        {
            Vector2 at = P(label.At);
            Vector2 extent = Raylib.MeasureTextEx(font, label.Text, size, 0f);
            Raylib.DrawTextEx(font, label.Text, at - extent * 0.5f, size, 0f, Glow(TerrainRgb, 0.8f));
        }
    }

    // ---- units ----

    /// <param name="clipToTable">Skip the arcs that fall off the table (the pulse ring).</param>
    private void GroundCircle(HoloCamera cam, Vector2 centre, float radius, float width, Color col, bool dashed, int segments,
        bool clipToTable = false)
    {
        Vector2 prevWorld = centre + new Vector2(radius, 0f);
        Vector2 prev = cam.Project(new Vector3(prevWorld, 0f)) * _ss;
        for (int i = 1; i <= segments; i++)
        {
            float a = i * 2f * MathF.PI / segments;
            Vector2 world = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            Vector2 next = cam.Project(new Vector3(world, 0f)) * _ss;
            Vector2 mid = (prevWorld + world) * 0.5f;
            bool onTable = MathF.Abs(mid.X) <= HoloTableScene.HalfW && MathF.Abs(mid.Y) <= HoloTableScene.HalfD;
            if ((!dashed || i % 2 == 1) && (!clipToTable || onTable)) Raylib.DrawLineEx(prev, next, width, col);
            prev = next;
            prevWorld = world;
        }
    }

    /// <summary>Each unit: dotted trail, a ring per base (casualties dashed and faint; a vehicle gets an
    /// inner ring and a facing tick), and one flag on the leader with the unit's tag beside it.</summary>
    private void DrawUnits(HoloCamera cam, float ws)
    {
        Vector2 P(Vector3 p) => cam.Project(p) * _ss;
        Font font = _fontLoaded ? _font : Raylib.GetFontDefault();

        foreach (HoloUnit unit in _skirmish.Units)
        {
            Vector3 rgb = unit.Army.Rgb;
            for (int i = 0; i + 1 < unit.Trail.Count; i++)
                for (int k = 0; k < 5; k++)
                {
                    Vector2 at = Vector2.Lerp(unit.Trail[i], unit.Trail[i + 1], k / 5f);
                    Raylib.DrawCircleV(P(new Vector3(at, 0f)), 2f * ws, Glow(rgb, 0.45f));
                }

            int segments = unit.BaseRadius < 1f ? 24 : 48;
            foreach (Vector2 m in unit.Models.Take(unit.Dead))
                GroundCircle(cam, m, unit.BaseRadius, 1.5f * ws, Glow(rgb, 0.3f), dashed: true, segments);
            foreach (Vector2 m in unit.Alive)
            {
                GroundCircle(cam, m, unit.BaseRadius, 2f * ws, Glow(rgb, 0.9f), dashed: false, segments);
                if (unit.IsVehicle)
                {
                    GroundCircle(cam, m, unit.BaseRadius * 0.6f, 1.5f * ws, Glow(rgb, 0.5f), dashed: true, segments);
                    float f = unit.Facing * unit.BaseRadius;
                    Raylib.DrawLineEx(P(new Vector3(m.X, m.Y + f * 0.6f, 0f)), P(new Vector3(m.X, m.Y + f * 1.35f, 0f)),
                        3f * ws, Glow(rgb, 1f));
                }
            }

            Vector2 leader = unit.Leader;
            Vector2 foot = P(new Vector3(leader, 0f));
            Vector2 top = P(new Vector3(leader, unit.IsVehicle ? 5.5f : 4f));
            Raylib.DrawLineEx(foot, top, 2f * ws, Glow(rgb, 0.9f));
            Vector2 tip = top + new Vector2(20f * unit.Facing, 6f) * ws, tail = top + new Vector2(0f, 12f) * ws;
            Rlgl.DisableBackfaceCulling();
            Raylib.DrawTriangle(top, tip, tail, Glow(rgb, 0.55f));
            Rlgl.DrawRenderBatchActive();
            Rlgl.EnableBackfaceCulling();
            Raylib.DrawLineEx(top, tip, 1.5f * ws, Glow(rgb, 1f));
            Raylib.DrawLineEx(tip, tail, 1.5f * ws, Glow(rgb, 1f));

            float size = 15f * ws;
            Vector2 extent = Raylib.MeasureTextEx(font, unit.Tag, size, 0f);
            float x = unit.Facing > 0 ? top.X + 6f * ws : top.X - 6f * ws - extent.X;
            Raylib.DrawTextEx(font, unit.Tag, new Vector2(x, top.Y - 19f * ws), size, 0f, Glow(rgb, 0.9f));
        }
    }

    // ---- the fight ----

    private static float Frac(float x) => x - MathF.Floor(x);

    /// <summary>The pulse ring, tracers along every line of fire, the fire on the burning unit, and the
    /// measuring line - all driven by <paramref name="time"/>.</summary>
    private void DrawEngagement(HoloCamera cam, float ws, float time, float pulsePhase)
    {
        Vector2 P(Vector2 p, float z = 0f) => cam.Project(new Vector3(p, z)) * _ss;
        HoloEngagement e = _engagement;

        // Pulse: a bright leading edge with two fading echoes behind it, dimming as it spreads.
        float r = pulsePhase * PulseReach, fade = 1f - pulsePhase;
        GroundCircle(cam, e.PulseCentre, r, 2.5f * ws, Glow(PulseRgb, 0.9f * fade), false, 128, clipToTable: true);
        GroundCircle(cam, e.PulseCentre, MathF.Max(0f, r - 0.6f), 2f * ws, Glow(PulseRgb, 0.35f * fade), false, 128, clipToTable: true);
        GroundCircle(cam, e.PulseCentre, MathF.Max(0f, r - 1.4f), 1.5f * ws, Glow(PulseRgb, 0.15f * fade), false, 128, clipToTable: true);

        // Tracers: a faint line of fire, two bright dashes travelling down it, a muzzle flash as each leaves.
        for (int i = 0; i < e.Shots.Count; i++)
        {
            HoloShot shot = e.Shots[i];
            Vector2 a = P(shot.From, 0.9f), b = P(shot.To, 0.8f);
            Raylib.DrawLineEx(a, b, 1f * ws, Glow(TracerRgb, 0.18f));
            float phase = Frac(time / TracerCycle + i * 0.37f);
            foreach (float ph in new[] { phase, Frac(phase + 0.5f) })
                if (ph < 0.92f)
                    Raylib.DrawLineEx(Vector2.Lerp(a, b, ph), Vector2.Lerp(a, b, ph + 0.08f), 3f * ws, Glow(TracerRgb, 1f));
            if (phase < 0.12f) Raylib.DrawCircleV(a, 3.5f * ws, Glow(TracerRgb, 0.9f));
        }

        if (e.Burning is not null) DrawFire(P(e.FireAt), cam, e.FireAt, ws, time);
        if (e.Measure is { } m) DrawMeasure(m, cam, ws, time);
    }

    /// <summary>Simple stylised flames: three layers of three flickering tongues, rising embers, and
    /// orange heat rings on the table.</summary>
    private void DrawFire(Vector2 at, HoloCamera cam, Vector2 world, float ws, float time)
    {
        float heat = 0.85f + 0.15f * MathF.Sin(time * 5.3f);
        GroundCircle(cam, world, 2.0f, 2f * ws, Glow(new Vector3(1f, 0.45f, 0.15f), 0.6f * heat), false, 48);
        GroundCircle(cam, world, 3.0f, 2f * ws, Glow(new Vector3(1f, 0.45f, 0.15f), 0.35f * heat), false, 48);

        var layers = new (Vector3 Rgb, float Height, float Width, float Intensity)[]
        {
            (new(1.0f, 0.25f, 0.08f), 50f, 30f, 0.75f),
            (new(1.0f, 0.55f, 0.12f), 36f, 21f, 0.9f),
            (new(1.0f, 0.90f, 0.50f), 20f, 10f, 0.9f),
        };
        Rlgl.DisableBackfaceCulling();
        for (int k = 0; k < layers.Length; k++)
        {
            var (rgb, height, width, intensity) = layers[k];
            Color col = Glow(rgb, intensity);
            for (int j = 0; j < 3; j++)
            {
                float ox = (j - 1) * width * 0.55f * ws;
                float h = height * ws * (0.8f + 0.25f * MathF.Sin(time * 9f + j * 2.1f + k * 1.3f));
                float sway = MathF.Sin(time * 7f + j * 1.7f + k) * 4f * ws;
                float w = width * ws;
                Vector2 baseC = at + new Vector2(ox, 0f);
                Vector2[] tongue =
                {
                    baseC + new Vector2(-w * 0.5f, 0f), baseC + new Vector2(-w * 0.2f, -h * 0.55f),
                    baseC + new Vector2(sway, -h), baseC + new Vector2(w * 0.25f, -h * 0.5f), baseC + new Vector2(w * 0.5f, 0f),
                };
                for (int t = 1; t + 1 < tongue.Length; t++)
                    Raylib.DrawTriangle(tongue[0], tongue[t], tongue[t + 1], col);
            }
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();

        for (int k = 0; k < 12; k++)
        {
            float life = Frac(time * 0.6f + k * 0.083f);
            Vector2 ember = at + new Vector2(MathF.Sin(k * 12.9f) * 30f + MathF.Sin(time * 2f + k) * 4f, -(50f + life * 70f)) * ws;
            Raylib.DrawCircleV(ember, (1.4f + k % 3 * 0.5f) * ws, Glow(new Vector3(1f, 0.6f, 0.2f), 0.8f * (1f - life)));
        }
    }

    /// <summary>The measuring line draws itself out base to base, shows its distance, holds, then fades.</summary>
    private void DrawMeasure(HoloMeasure m, HoloCamera cam, float ws, float time)
    {
        float tc = time % MeasureCycle;
        if (tc > MeasureFade) return;
        float reach = MathF.Min(1f, tc / MeasureDraw);
        float alpha = tc <= MeasureHold ? 1f : 1f - (tc - MeasureHold) / (MeasureFade - MeasureHold);

        Vector2 a = cam.Project(new Vector3(m.From, 0.05f)) * _ss, b = cam.Project(new Vector3(m.To, 0.05f)) * _ss;
        Color col = Glow(MeasureRgb, alpha);
        const int dashes = 30;
        for (int i = 0; i < dashes; i += 2)
        {
            float s0 = i / (float)dashes, s1 = (i + 1) / (float)dashes;
            if (s0 >= reach) break;
            Raylib.DrawLineEx(Vector2.Lerp(a, b, s0), Vector2.Lerp(a, b, MathF.Min(s1, reach)), 2f * ws, col);
        }
        Raylib.DrawCircleV(a, 3f * ws, col);
        if (reach < 1f) return;
        Raylib.DrawCircleV(b, 3f * ws, col);

        Font font = _fontLoaded ? _font : Raylib.GetFontDefault();
        string text = $"{m.Inches:0.0}\"";
        float size = 19f * ws;
        Vector2 extent = Raylib.MeasureTextEx(font, text, size, 0f);
        Vector2 mid = (a + b) * 0.5f + new Vector2(14f * ws, -extent.Y * 0.5f);
        var box = new Rectangle(mid.X - 6f * ws, mid.Y - 3f * ws, extent.X + 12f * ws, extent.Y + 6f * ws);
        Raylib.DrawRectangleLinesEx(box, MathF.Max(1f, ws), Glow(MeasureRgb, 0.7f * alpha));
        Raylib.DrawTextEx(font, text, mid, size, 0f, Glow(new Vector3(0.95f, 1f, 1f), alpha));
    }

    // ---- post ----

    /// <summary>Scanlines, a vignette, and the darkened band behind the menu panel.</summary>
    private void DrawScreenTreatments(MainMenuLayout layout)
    {
        var scan = new Color(0, 0, 0, 28);
        for (int y = 0; y < _h; y += 3)
            Raylib.DrawRectangle(0, y, _w, 1, scan);

        // Vignette as four soft edge gradients (they overlap into darker corners on their own).
        int edge = (int)(_h * 0.22f);
        var dark = new Color(0, 0, 0, 120);
        var clear = new Color(0, 0, 0, 0);
        Raylib.DrawRectangleGradientV(0, 0, _w, edge, dark, clear);
        Raylib.DrawRectangleGradientV(0, _h - edge, _w, edge, clear, dark);
        Raylib.DrawRectangleGradientH(0, 0, edge, _h, dark, clear);
        Raylib.DrawRectangleGradientH(_w - edge, 0, edge, _h, clear, dark);

        // Behind the panel: solid dark up to just past it, then ramping clear across the gap to the table.
        float s = layout.Scale;
        int solidTo = (int)(layout.Panel.Right - 90f * s);
        int rampTo = (int)(layout.TableRegion.X + 60f * s);
        var shade = new Color(0, 0, 0, 166);
        Raylib.DrawRectangle(0, 0, solidTo, _h, shade);
        Raylib.DrawRectangleGradientH(solidTo, 0, Math.Max(1, rampTo - solidTo), _h, shade, new Color(0, 0, 0, 0));
    }

    private void BlurInto(RenderTexture2D src, RenderTexture2D dst, Vector2 dir, float spread, float threshold = 0f)
    {
        Raylib.BeginTextureMode(dst);
        Raylib.ClearBackground(Color.Black);
        Raylib.BeginShaderMode(_blur);
        Vector2 step = new(dir.X * spread / src.Texture.Width, dir.Y * spread / src.Texture.Height);
        Raylib.SetShaderValue(_blur, _locTexelStep, step, ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_blur, _locThreshold, threshold, ShaderUniformDataType.Float);
        DrawTargetFlipped(src, new Rectangle(0, 0, dst.Texture.Width, dst.Texture.Height), Color.White);
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
    }

    private static void Blit(RenderTexture2D src, RenderTexture2D dst)
    {
        Raylib.BeginTextureMode(dst);
        Raylib.ClearBackground(Color.Black);
        DrawTargetFlipped(src, new Rectangle(0, 0, dst.Texture.Width, dst.Texture.Height), Color.White);
        Raylib.EndTextureMode();
    }

    /// <summary>Render textures are stored bottom-up; a negative source height draws them upright.</summary>
    private static void DrawTargetFlipped(RenderTexture2D rt, Rectangle dest, Color tint) =>
        Raylib.DrawTexturePro(rt.Texture, new Rectangle(0, 0, rt.Texture.Width, -rt.Texture.Height),
            dest, Vector2.Zero, 0f, tint);

    public void Dispose()
    {
        UnloadTargets();
        if (_triedInit)
        {
            if (_blurReady) Raylib.UnloadShader(_blur);
            if (_fontLoaded) Raylib.UnloadFont(_font);
            if (_radialReady) Raylib.UnloadTexture(_radial);
        }
    }
}
