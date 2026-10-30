using System.Numerics;
using FdgRaylib.Config;
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
/// pipeline runs every frame, since everything animates - the fight and scan line (slice 3), and the drifting,
/// directed camera and the battle timeline of <see cref="HoloDirector"/> (slice 4) - a couple of
/// thousand lines and five small blur passes. If the blur shader will not compile the table
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

    // Animation timings, seconds. Tracers cross their line once per TracerCycle; the measuring line
    // draws, holds, fades, rests. (The scan line's timing lives in HoloDirector.)
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

    private readonly Random _rng;
    private HoloTableScene _scene;
    private HoloSkirmish _skirmish;
    private HoloEngagement _engagement;
    private readonly HoloClock _clock = new();
    private int _battleIndex;           // alternates the camera's close-up between the fight and the measure
    private bool _stale = true;         // the cached frame no longer matches (new pairing, size or setting)
    private EMenuBackground _lastMode;

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
        (_scene, _skirmish, _engagement, _) = HoloBattle.Generate(_rng);
    }

    /// <summary>Freezes the animation clock at this many seconds (the offscreen preview); null = live.</summary>
    internal float? FixedTime { get; set; }

    /// <summary>Holds the picture still (a dialog is open over the menu). The clock stops and the last
    /// frame is reused, so a paused table costs nothing and resumes exactly where it was.</summary>
    public bool Paused { get; set; }

    /// <summary>The armies currently on the table.</summary>
    public HoloSkirmish Skirmish => _skirmish;

    /// <summary>Deals a new battle onto the table and starts it from the top. The
    /// renderer calls this each time the menu comes back into view; a battle that runs its full length
    /// deals the next one itself.</summary>
    public void Reshuffle()
    {
        DealNewPairing();
        _clock.RestartBattle();
    }

    /// <summary>A whole new battle: fresh terrain, pairing, positions, routes and shooting.</summary>
    private void DealNewPairing()
    {
        (_scene, _skirmish, _engagement, _) = HoloBattle.Generate(_rng);
        _battleIndex++;
        _stale = true;
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
        EMenuBackground mode = ViewSettings.MenuBackground;
        if (mode != _lastMode) { _lastMode = mode; _stale = true; }
        if (mode == EMenuBackground.Off || screenW <= 0 || screenH <= 0) return;
        EnsureInit();
        if (!_haveTargets || screenW != _w || screenH != _h) Resize(screenW, screenH);

        // Which moment to draw. The preview freezes one; Still shows the signed-off overview (drift at
        // its centre, everyone arrived and fighting); Animated runs the table's own clock.
        float wall, battle;
        bool live = false;
        if (FixedTime is { } fixedTime)
            (wall, battle) = (fixedTime, fixedTime);
        else if (mode == EMenuBackground.Still)
            (wall, battle) = (0f, HoloDirector.StillMoment);
        else
        {
            if (_clock.Tick(Raylib.GetFrameTime(), Paused)) DealNewPairing();
            (wall, battle) = (_clock.Wall, _clock.Battle);
            live = !Paused;
        }

        // Only a running table needs a new frame every frame; otherwise the cached one stands until
        // something it shows changes.
        if (live || _stale || FixedTime is not null)
        {
            RenderFrame(layout, wall, battle);
            _stale = false;
        }
    }

    /// <summary>Draws the frame <see cref="Prepare"/> rendered over the whole of the current target -
    /// or, with the background switched off, plain dark.</summary>
    public void Present()
    {
        if (ViewSettings.MenuBackground == EMenuBackground.Off)
            Raylib.DrawRectangle(0, 0, Raylib.GetRenderWidth(), Raylib.GetRenderHeight(), BaseColor);
        else if (_haveTargets)
            DrawTargetFlipped(_finalRT, new Rectangle(0, 0, _w, _h), Color.White);
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
        _stale = true;
    }

    private void UnloadTargets()
    {
        if (!_haveTargets) return;
        foreach (RenderTexture2D rt in new[] { _sceneRT, _imageRT, _halfA, _halfB, _quarterA, _quarterB, _finalRT })
            Raylib.UnloadRenderTexture(rt);
        _haveTargets = false;
    }

    /// <param name="time">Wall time - the camera drift and the flicker of fire and tracers.</param>
    /// <param name="battle">Seconds into the current pairing's battle (<see cref="HoloDirector"/>).</param>
    private void RenderFrame(MainMenuLayout layout, float time, float battle)
    {
        float presence = HoloDirector.Presence(battle);
        // A hologram stutters as it resolves: while fading, every other 1/17s frame drops a third.
        if (presence < 1f && Frac(time * 17f) < 0.5f) presence *= 0.65f;

        HoloCamera cam = DirectedCamera(layout.TableRegion, time, battle);
        float px = _h / 1080f;   // authored line widths are 1080p pixels

        // 1. The sharp scene, supersampled. Everything glows, so it all adds.
        Raylib.BeginTextureMode(_sceneRT);
        Raylib.ClearBackground(BaseColor);
        Raylib.BeginBlendMode(BlendMode.Additive);
        DrawGlowPool(cam);
        float? sweepX = HoloDirector.SweepX(battle);
        DrawScene(cam, px * _ss, sweepX);
        DrawUnits(cam, px * _ss, battle, presence);
        DrawEngagement(cam, px * _ss, time, battle, sweepX, presence);
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

    /// <summary>
    /// This frame's camera: the slow drift round the table, fitted to show the whole slab, blended toward
    /// a close framing of one of the battle's fights as the director calls for it (a different fight each
    /// battle when there are several).
    /// </summary>
    private HoloCamera DirectedCamera(ScreenRect region, float time, float battle)
    {
        float yaw = HoloDirector.YawDeg(time), pitch = HoloDirector.PitchDeg(time);
        HoloCamera overview = HoloCamera.FitToRegion(new Vector3(0f, 1f, 0f), HoloTableScene.SlabCorners,
            region.X, region.Y, region.W, region.H, _h, pitch, yaw);
        float w = HoloDirector.FocusWeight(battle);
        if (w <= 0f || _engagement.FocusBox(_battleIndex) is not { } focusBox) return overview;

        var (centre, half) = focusBox;
        var box = new List<Vector3>();
        foreach (float sx in new[] { -1f, 1f })
        foreach (float sy in new[] { -1f, 1f })
        foreach (float z in new[] { 0f, 6f })
            box.Add(new Vector3(centre.X + sx * half.X, centre.Y + sy * half.Y, z));
        HoloCamera focus = HoloCamera.FitToRegion(new Vector3(centre, 0f), box,
            region.X, region.Y, region.W, region.H, _h, pitch, yaw);
        return HoloCamera.Blend(overview, focus, w);
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

    /// <summary>How much brighter something at <paramref name="x"/> glows as the scan line passes it
    /// (1 = not at all): a soft three-inch band either side of the line.</summary>
    private static float SweepBoost(float x, float? sweepX) =>
        sweepX is { } s ? 1f + 1.3f * MathF.Exp(-MathF.Pow((x - s) / 3f, 2f)) : 1f;

    /// <summary>Lines the scan lights up: terrain, and the table lines that run ACROSS the sweep (grid
    /// lines and ticks of constant X), which it lights one at a time. Lines along the sweep would light
    /// end to end at once, so they are left alone, as is the rim.</summary>
    private static bool SweepLights(HoloLine l) =>
        l.Piece >= 0 || (l.Tint == EHoloTint.Terrain && l.A.X == l.B.X);

    private void DrawScene(HoloCamera cam, float widthScale, float? sweepX)
    {
        Vector2 P(Vector3 p) => cam.Project(p) * _ss;

        // Fills under lines. Projection can flip a quad's winding, so culling is off for these.
        Rlgl.DisableBackfaceCulling();
        foreach (HoloFill f in _scene.Fills)
        {
            float boost = f.Piece >= 0 ? SweepBoost((f.A.X + f.B.X + f.C.X + f.D.X) / 4f, sweepX) : 1f;
            Color col = Glow(TintRgb(f.Tint), f.Intensity * boost);
            Vector2 a = P(f.A), b = P(f.B), c = P(f.C), d = P(f.D);
            Raylib.DrawTriangle(a, b, c, col);
            Raylib.DrawTriangle(a, c, d, col);
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();

        foreach (HoloLine l in _scene.Lines)
            Raylib.DrawLineEx(P(l.A), P(l.B), MathF.Max(1f, l.Width * widthScale),
                Glow(TintRgb(l.Tint), l.Intensity * (SweepLights(l) ? SweepBoost((l.A.X + l.B.X) * 0.5f, sweepX) : 1f)));

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

    private void GroundCircle(HoloCamera cam, Vector2 centre, float radius, float width, Color col, bool dashed, int segments)
    {
        Vector2 prev = cam.Project(new Vector3(centre.X + radius, centre.Y, 0f)) * _ss;
        for (int i = 1; i <= segments; i++)
        {
            float a = i * 2f * MathF.PI / segments;
            Vector2 next = cam.Project(new Vector3(centre.X + radius * MathF.Cos(a), centre.Y + radius * MathF.Sin(a), 0f)) * _ss;
            if (!dashed || i % 2 == 1) Raylib.DrawLineEx(prev, next, width, col);
            prev = next;
        }
    }

    /// <summary>Each unit: dotted trail, a ring per base (casualties dashed and faint; a vehicle gets an
    /// inner ring and a facing tick), and one flag on the leader with the unit's tag beside it.</summary>
    private void DrawUnits(HoloCamera cam, float ws, float battle, float presence)
    {
        Vector2 P(Vector3 p) => cam.Project(p) * _ss;
        Font font = _fontLoaded ? _font : Raylib.GetFontDefault();
        float advance = HoloDirector.Advance(battle);

        foreach (HoloUnit unit in _skirmish.Units)
        {
            Vector3 rgb = unit.Army.Rgb;
            Color Lit(float k) => Glow(rgb, k * presence);
            Vector2 offset = unit.OffsetAt(advance);

            // The trail is history: only the stretch already walked is dotted in.
            float walked = 0f, total = 0f;
            for (int i = 0; i + 1 < unit.Trail.Count; i++) total += Vector2.Distance(unit.Trail[i], unit.Trail[i + 1]);
            for (int i = 0; i + 1 < unit.Trail.Count; i++)
            {
                float leg = Vector2.Distance(unit.Trail[i], unit.Trail[i + 1]);
                for (int k = 0; k < 5; k++)
                {
                    if (walked + leg * k / 5f > advance * total) break;
                    Vector2 at = Vector2.Lerp(unit.Trail[i], unit.Trail[i + 1], k / 5f);
                    Raylib.DrawCircleV(P(new Vector3(at, 0f)), 2f * ws, Lit(0.45f));
                }
                walked += leg;
            }

            int segments = unit.BaseRadius < 1f ? 24 : 48;
            foreach (Vector2 m in unit.Models.Take(unit.Dead))
                GroundCircle(cam, m + offset, unit.BaseRadius, 1.5f * ws, Lit(0.3f), dashed: true, segments);
            foreach (Vector2 alive in unit.Alive)
            {
                Vector2 m = alive + offset;
                GroundCircle(cam, m, unit.BaseRadius, 2f * ws, Lit(0.9f), dashed: false, segments);
                if (unit.IsVehicle)
                {
                    GroundCircle(cam, m, unit.BaseRadius * 0.6f, 1.5f * ws, Lit(0.5f), dashed: true, segments);
                    float f = unit.Facing * unit.BaseRadius;
                    Raylib.DrawLineEx(P(new Vector3(m.X, m.Y + f * 0.6f, 0f)), P(new Vector3(m.X, m.Y + f * 1.35f, 0f)),
                        3f * ws, Lit(1f));
                }
            }

            Vector2 leader = unit.Leader + offset;
            Vector2 foot = P(new Vector3(leader, 0f));
            Vector2 top = P(new Vector3(leader, unit.IsVehicle ? 5.5f : 4f));
            Raylib.DrawLineEx(foot, top, 2f * ws, Lit(0.9f));
            Vector2 tip = top + new Vector2(20f * unit.Facing, 6f) * ws, tail = top + new Vector2(0f, 12f) * ws;
            Rlgl.DisableBackfaceCulling();
            Raylib.DrawTriangle(top, tip, tail, Lit(0.55f));
            Rlgl.DrawRenderBatchActive();
            Rlgl.EnableBackfaceCulling();
            Raylib.DrawLineEx(top, tip, 1.5f * ws, Lit(1f));
            Raylib.DrawLineEx(tip, tail, 1.5f * ws, Lit(1f));

            float size = 15f * ws;
            Vector2 extent = Raylib.MeasureTextEx(font, unit.Tag, size, 0f);
            float x = unit.Facing > 0 ? top.X + 6f * ws : top.X - 6f * ws - extent.X;
            Raylib.DrawTextEx(font, unit.Tag, new Vector2(x, top.Y - 19f * ws), size, 0f, Lit(0.9f));
        }
    }

    // ---- the fight ----

    private static float Frac(float x) => x - MathF.Floor(x);

    /// <summary>The scan line, tracers along every line of fire, and the measuring line.</summary>
    private void DrawEngagement(HoloCamera cam, float ws, float time, float battle, float? sweepX, float presence)
    {
        if (battle < HoloDirector.FightsFrom) return;
        Vector2 P(Vector2 p, float z = 0f) => cam.Project(new Vector3(p, z)) * _ss;
        HoloEngagement e = _engagement;

        // Scan line: a bright leading edge across the table's width with two fading echoes behind it,
        // drawn only where it is over the table.
        if (sweepX is { } sx)
            foreach (var (lag, width, intensity) in new[] { (0f, 2.5f, 0.9f), (0.6f, 2f, 0.35f), (1.4f, 1.5f, 0.15f) })
            {
                float x = sx - lag;
                if (MathF.Abs(x) > HoloTableScene.HalfW) continue;
                Raylib.DrawLineEx(P(new Vector2(x, -HoloTableScene.HalfD), 0.02f), P(new Vector2(x, HoloTableScene.HalfD), 0.02f),
                    width * ws, Glow(PulseRgb, intensity * presence));
            }

        // Tracers: a faint line of fire, two bright dashes travelling down it, a muzzle flash as each leaves.
        for (int i = 0; i < e.Shots.Count; i++)
        {
            HoloShot shot = e.Shots[i];
            Vector2 a = P(shot.From, 0.9f), b = P(shot.To, 0.8f);
            Raylib.DrawLineEx(a, b, 1f * ws, Glow(TracerRgb, 0.18f * presence));
            float phase = Frac(time / TracerCycle + i * 0.37f);
            foreach (float ph in new[] { phase, Frac(phase + 0.5f) })
                if (ph < 0.92f)
                    Raylib.DrawLineEx(Vector2.Lerp(a, b, ph), Vector2.Lerp(a, b, ph + 0.08f), 3f * ws, Glow(TracerRgb, presence));
            if (phase < 0.12f) Raylib.DrawCircleV(a, 3.5f * ws, Glow(TracerRgb, 0.9f * presence));
        }

        if (e.Measure is { } m && battle >= HoloDirector.MeasureFrom)
            DrawMeasure(m, cam, ws, battle - HoloDirector.MeasureFrom, presence);
    }

    /// <summary>The measuring line draws itself out base to base, shows its distance, holds, then fades;
    /// <paramref name="time"/> counts from when measuring began.</summary>
    private void DrawMeasure(HoloMeasure m, HoloCamera cam, float ws, float time, float presence)
    {
        float tc = time % MeasureCycle;
        if (tc > MeasureFade) return;
        float reach = MathF.Min(1f, tc / MeasureDraw);
        float alpha = (tc <= MeasureHold ? 1f : 1f - (tc - MeasureHold) / (MeasureFade - MeasureHold)) * presence;

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
