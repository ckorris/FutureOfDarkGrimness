using System;
using System.Collections.Generic;
using System.IO;
using FDG;
using Raylib_cs;

namespace FdgRaylib.Rendering.TacticalOverlay;

/// <summary>
/// Offscreen GPU-vs-CPU field diff (#162 perf rework). Run with <c>--field-harness</c>: opens a HIDDEN
/// GL window (no visible window, no input -- safe to run unattended), renders the GPU field for
/// synthetic scenes, reads the composite back, and pixel-diffs it against the CPU reference produced by
/// the exact same inputs through <see cref="OpportunityFieldBuilder"/> + <see cref="PolarSightMap"/> +
/// <see cref="FieldCompositor"/>. The CPU picture is the hand-verified reference implementation, so a
/// clean diff here is the machine-checkable stand-in for eyeballing the GPU path.
///
/// Artifacts (cpu/gpu PNGs per scene, and a diff PNG on mismatch) are written to
/// <c>FieldHarnessOut/</c> under the working directory for visual inspection.
///
/// Exit code 0 = all scenes pass; 1 = any scene over tolerance or a probe failure.
/// </summary>
internal static class FieldHarness
{
    private const float Tpi = TacticalOverlayConfig.TexelsPerInch;
    private static readonly int W = (int)MathF.Ceiling(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES * Tpi);
    private static readonly int H = (int)MathF.Ceiling(GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES * Tpi);

    // Per-channel byte tolerance (CPU rounds float->byte once; GPU rounds in the raster) and the share
    // of union pixels allowed to exceed it. The residual is EDGE noise -- disc/fan rasterization, the
    // 2-texel band rings, and the cover crosshatch mesh (which doubles the line-edge count). It stays
    // well under 2%; a gross divergence (wrong blend, flipped orientation, misplaced pattern) is 10x+
    // this and still caught.
    private const int ChannelTolerance = 8;
    private const float MaxMismatchFraction = 0.02f;

    private sealed record Scene(string Name, List<FieldTargetModel> Targets, List<BandSpec> Bands,
        float ShooterRadius, List<ITerrain> Terrain);

    public static int Run()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(320, 200, "field-harness");

        string outDir = Path.Combine(Environment.CurrentDirectory, "FieldHarnessOut");
        Directory.CreateDirectory(outDir);

        var gpu = new GpuFieldRenderer(W, H, Tpi);
        bool ok = gpu.TryInit();
        Console.WriteLine($"[harness] grid {W}x{H} @ {Tpi} tpi, GPU init: {(ok ? "ok" : "FAILED")}");
        if (!ok) { Raylib.CloseWindow(); return 1; }

        int failures = 0;
        foreach (Scene scene in BuildScenes())
            if (!RunScene(gpu, scene, outDir)) failures++;

        gpu.Dispose();
        Raylib.CloseWindow();
        Console.WriteLine(failures == 0
            ? "[harness] ALL SCENES PASS"
            : $"[harness] {failures} scene(s) FAILED -- see {outDir}");
        return failures == 0 ? 0 : 1;
    }

    private static List<Scene> BuildScenes()
    {
        ITerrain Block(IZone z) => new TerrainData(ETerrainType.Blocking, z);
        ITerrain Cover(IZone z) => new TerrainData(ETerrainType.Cover, z);

        // Bands: shortest range = highest value (innermost), as the controller builds them.
        List<BandSpec> twoBands = new()
        {
            new BandSpec(24f, 1, "24\""),
            new BandSpec(12f, 2, "12\""),
        };

        var targetsA = new List<FieldTargetModel> { new(20f, 20f, 0.5f), new(23f, 21f, 0.5f) };

        return new List<Scene>
        {
            new("bands-only", targetsA, twoBands, 0.5f, new List<ITerrain>()),

            new("blocking-wall", targetsA, twoBands, 0.5f, new List<ITerrain>
            {
                Block(new RectangularZone(30f, 32f, 12f, 28f)),
            }),

            new("cover-wall", targetsA, twoBands, 0.5f, new List<ITerrain>
            {
                Cover(new RectangularZone(10f, 12f, 12f, 28f)),
            }),

            new("mixed-rotated", new List<FieldTargetModel> { new(36f, 24f, 0.5f), new(39f, 22f, 0.8f), new(34f, 27f, 0.5f) },
                twoBands, 0.55f, new List<ITerrain>
            {
                Block(new RotatedZoneWrapper(new RectangularZone(42f, 50f, 23f, 25f), 30f, new Float2(46f, 24f))),
                Block(new CircularZone(30f, 18f, 0.55f)),   // a model base
                Block(new CircularZone(29f, 30f, 0.55f)),
                Cover(new RectangularZone(24f, 28f, 20f, 29f)),
            }),
        };
    }

    private static bool RunScene(GpuFieldRenderer gpu, Scene scene, string outDir)
    {
        (byte r, byte g, byte b) accent = TacticalOverlayConfig.AccentPalette[0];

        // ---- CPU reference: the exact in-game pipeline ----------------------------------------------
        var band = new FieldMask(W, H, Tpi);
        OpportunityFieldBuilder.Build(band, scene.Targets, scene.ShooterRadius, scene.Bands);

        var maps = new List<PolarSightMap>();
        bool anySightPiece = scene.Terrain.Exists(t =>
            t.TerrainType.HasFlag(ETerrainType.Blocking) || t.TerrainType.HasFlag(ETerrainType.Cover));
        if (anySightPiece)
            foreach (FieldTargetModel t in scene.Targets)
                maps.Add(PolarSightMap.Build(new Position(t.X, t.Z), scene.Terrain, TacticalOverlayConfig.PolarBuckets));

        var shadow = new FieldMask(W, H, Tpi);
        var cover = new FieldMask(W, H, Tpi);
        var swCpu = System.Diagnostics.Stopwatch.StartNew();
        PolarSightMap.ClassifyInto(band, shadow, cover, maps);

        using var cpu = new FieldCompositor(W, H);
        cpu.Compose(band, shadow, cover, accent, alphaScale: 1f);
        swCpu.Stop();
        byte[] cpuPx = cpu.Pixels;   // straight alpha, row 0 = image top

        // ---- GPU render + readback ------------------------------------------------------------------
        var swGpu = System.Diagnostics.Stopwatch.StartNew();
        gpu.Rebuild(scene.Targets, scene.ShooterRadius, scene.Bands, maps, accent, alphaScale: 1f);
        swGpu.Stop();   // command submission; readback below forces completion for the diff
        Image img = Raylib.LoadImageFromTexture(gpu.CompositeTexture);
        byte[] gpuPx = ExtractRgba(img, out bool flipRows);
        Raylib.UnloadImage(img);

        // ---- Diff (both sides straight alpha) --------------------------------------------------------
        long union = 0, mism = 0;
        var diffPx = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        {
            int gpuY = flipRows ? H - 1 - y : y;
            for (int x = 0; x < W; x++)
            {
                int ci = (y * W + x) * 4;
                int gi = (gpuY * W + x) * 4;

                byte ca = cpuPx[ci + 3];
                byte ga = gpuPx[gi + 3];
                if (ca == 0 && ga == 0) continue;
                union++;

                bool bad = Math.Abs(cpuPx[ci] - gpuPx[gi]) > ChannelTolerance
                        || Math.Abs(cpuPx[ci + 1] - gpuPx[gi + 1]) > ChannelTolerance
                        || Math.Abs(cpuPx[ci + 2] - gpuPx[gi + 2]) > ChannelTolerance
                        || Math.Abs(ca - ga) > ChannelTolerance;
                if (bad)
                {
                    mism++;
                    diffPx[ci] = 255; diffPx[ci + 3] = 255;   // red marker in diff image
                }
            }
        }

        float frac = union > 0 ? (float)mism / union : (cpuOrGpuEmptyMismatch());
        bool probesOk = union > 0;
        bool pass = probesOk && frac <= MaxMismatchFraction;

        Console.WriteLine($"[harness] {scene.Name,-14} union {union,8}  mismatch {mism,7} ({frac:P2})  " +
                          $"cpu {swCpu.Elapsed.TotalMilliseconds,5:0.0}ms  gpu-submit {swGpu.Elapsed.TotalMilliseconds,5:0.0}ms  " +
                          $"-> {(pass ? "PASS" : "FAIL")}");

        WritePng(Path.Combine(outDir, $"{scene.Name}-cpu.png"), cpuPx, premultiply: false);
        WritePng(Path.Combine(outDir, $"{scene.Name}-gpu.png"), gpuPx, premultiply: false, flipRows ? H : 0);
        if (!pass)
            WritePng(Path.Combine(outDir, $"{scene.Name}-diff.png"), diffPx, premultiply: false);
        // The real judgement is how the field reads OVER the grass, not on black -- render the ACTUAL GPU
        // output (DrawComposite fill + DrawRings screen-space rings) over the mat + a faint grid, at 1
        // texel = 1 px, so the previewed rings ARE the in-game GPU rings.
        RenderTexture2D scratch = Raylib.LoadRenderTexture(W, H);
        Raylib.BeginTextureMode(scratch);
        Raylib.ClearBackground(new Color(40, 100, 40, 255));
        int gridStep = (int)MathF.Round(6f * Tpi);
        var gridCol = new Color(33, 85, 33, 255);
        for (int gx = 0; gx < W; gx += gridStep) Raylib.DrawLine(gx, 0, gx, H, gridCol);
        for (int gy = 0; gy < H; gy += gridStep) Raylib.DrawLine(0, gy, W, gy, gridCol);
        gpu.DrawComposite(0, 0, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES, Tpi);
        gpu.DrawRings(0, 0, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES, Tpi);
        Raylib.EndTextureMode();
        Image onmatImg = Raylib.LoadImageFromTexture(scratch.Texture);
        byte[] onmatPx = ExtractRgba(onmatImg, out _);
        Raylib.UnloadImage(onmatImg);
        Raylib.UnloadRenderTexture(scratch);
        WritePng(Path.Combine(outDir, $"{scene.Name}-onmat.png"), onmatPx, premultiply: false, H);

        return pass;

        float cpuOrGpuEmptyMismatch() => 1f; // both empty on a scene that must draw = fail loudly
    }

    /// <summary>
    /// Copies the readback image to a plain RGBA byte array. Render-target textures read back
    /// bottom-up relative to the draw space, so the caller diffs with flipped rows; the asymmetric
    /// scenes make any orientation error show up as a gross (>>tolerance) diff rather than passing
    /// silently.
    /// </summary>
    private static unsafe byte[] ExtractRgba(Image img, out bool flipRows)
    {
        if (img.Width != W || img.Height != H)
            throw new InvalidOperationException($"readback {img.Width}x{img.Height}, expected {W}x{H}");
        if (img.Format != PixelFormat.UncompressedR8G8B8A8)
            Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);

        var px = new byte[W * H * 4];
        fixed (byte* dst = px)
        {
            Buffer.MemoryCopy(img.Data, dst, px.Length, px.Length);
        }

        flipRows = true;
        return px;
    }

    private static void WritePng(string path, byte[] rgba, bool premultiply, int flipH = 0)
    {
        var buf = new byte[rgba.Length];
        for (int y = 0; y < H; y++)
        {
            int srcY = flipH > 0 ? flipH - 1 - y : y;
            for (int x = 0; x < W; x++)
            {
                int si = (srcY * W + x) * 4, di = (y * W + x) * 4;
                byte a = rgba[si + 3];
                if (premultiply)
                {
                    buf[di] = (byte)(rgba[si] * a / 255);
                    buf[di + 1] = (byte)(rgba[si + 1] * a / 255);
                    buf[di + 2] = (byte)(rgba[si + 2] * a / 255);
                }
                else
                {
                    buf[di] = rgba[si]; buf[di + 1] = rgba[si + 1]; buf[di + 2] = rgba[si + 2];
                }
                buf[di + 3] = a;
            }
        }
        ExportRgba(path, buf);
    }

    private static unsafe void ExportRgba(string path, byte[] buf)
    {
        fixed (byte* p = buf)
        {
            var img = new Image
            {
                Data = p,
                Width = W,
                Height = H,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR8G8B8A8,
            };
            Raylib.ExportImage(img, path);
        }
    }
}
