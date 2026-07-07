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
        // The real judgement is how the field reads OVER the grass, not on black -- composite the GPU
        // picture onto the mat colour (with a faint inch grid), then overlay the vector band rings the
        // in-game DrawFieldRings draws on top (they aren't baked into the texture), for eyeballing.
        byte maxV = 0;
        foreach (BandSpec bs in scene.Bands) if (bs.Value > maxV) maxV = bs.Value;
        var rings = new List<List<Float2>>();
        for (byte v = 1; v <= maxV; v++)
            rings.AddRange(MarchingSquares.Extract(band, v, 1f / Tpi));
        WriteOnMat(Path.Combine(outDir, $"{scene.Name}-onmat.png"), gpuPx, flipRows ? H : 0, rings);

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

    // Straight-alpha field composited over the mat colour + a faint 6" grid, then the vector band rings
    // overlaid -- what the player sees.
    private static void WriteOnMat(string path, byte[] rgba, int flipH, List<List<Float2>> rings)
    {
        (byte r, byte g, byte b) mat = (40, 100, 40);
        (byte r, byte g, byte b) grid = (33, 85, 33);
        int gridStepTexels = (int)MathF.Round(6f * Tpi);

        var buf = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        {
            int srcY = flipH > 0 ? flipH - 1 - y : y;
            for (int x = 0; x < W; x++)
            {
                bool onGrid = (x % gridStepTexels == 0) || (y % gridStepTexels == 0);
                (byte br, byte bg, byte bb) = onGrid ? grid : mat;

                int si = (srcY * W + x) * 4, di = (y * W + x) * 4;
                float a = rgba[si + 3] / 255f;
                buf[di]     = (byte)(rgba[si]     * a + br * (1f - a));
                buf[di + 1] = (byte)(rgba[si + 1] * a + bg * (1f - a));
                buf[di + 2] = (byte)(rgba[si + 2] * a + bb * (1f - a));
                buf[di + 3] = 255;
            }
        }

        (byte r, byte g, byte b) ring = TacticalOverlayConfig.AccentPalette[0];
        foreach (List<Float2> poly in rings)
            for (int i = 0; i < poly.Count - 1; i++)
                PlotLine(buf, WorldToImage(poly[i]), WorldToImage(poly[i + 1]), ring);

        ExportRgba(path, buf);
    }

    private static (int x, int y) WorldToImage(Float2 w) =>
        ((int)(w.X * Tpi), (int)((GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES - w.Y) * Tpi));

    // Simple 1px Bresenham line into the RGBA buffer (harness preview only).
    private static void PlotLine(byte[] buf, (int x, int y) a, (int x, int y) b, (byte r, byte g, byte b) col)
    {
        int x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y;
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            if ((uint)x0 < (uint)W && (uint)y0 < (uint)H)
            {
                int di = (y0 * W + x0) * 4;
                buf[di] = col.r; buf[di + 1] = col.g; buf[di + 2] = col.b; buf[di + 3] = 255;
            }
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
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
