using System.Diagnostics;
using FdgRaylib.Config;
using Raylib_cs;
using rlImGui_cs;

namespace FdgRaylib.Rendering.MenuBackground;

/// <summary>
/// <c>--menu-preview &lt;out.png&gt; [WxH] [menu|privacy|options] [seed] [t=SECONDS]</c> (#448): renders the main menu - the
/// war table plus the real ImGui menu, fonts and theme - into an image from a hidden window, then exits.
/// For checking the look on a given screen size without opening (or driving) a visible window, the same
/// way <c>--field-harness</c> checks the tactical overlay. Clicks nothing and saves no settings.
///
/// <para>With <c>bench</c> it then times the live background (Animated, running clock) and the Still
/// setting over a few hundred frames and prints milliseconds per frame - a cost check (slice 5).</para>
/// </summary>
internal static class MenuPreview
{
    /// <param name="time">Battle time to freeze at, seconds (see <see cref="HoloDirector"/>).</param>
    public static int Run(string outPath, int width, int height, string state, int seed, float time, bool bench = false)
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(width, height, "menu-preview");
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        RaylibRenderer.SetUpImGui(h);

        var menu = new MainMenuScreen();
        menu.OpenForPreview(state);
        var background = new MenuBackgroundRenderer(seed) { FixedTime = time };
        RenderTexture2D target = Raylib.LoadRenderTexture(w, h);

        // A few frames: ImGui sizes auto-fit popups from the previous frame's content.
        for (int frame = 0; frame < 4; frame++)
        {
            Raylib.BeginDrawing();
            background.Prepare(w, h, MainMenuLayout.Compute(w, h));   // its own passes, outside our target
            Raylib.BeginTextureMode(target);
            Raylib.ClearBackground(new Color(30, 30, 30, 255));
            background.Present();
            rlImGui.Begin();
            menu.Draw(w, h);
            rlImGui.End();
            Raylib.EndTextureMode();
            Raylib.EndDrawing();
        }

        if (bench)
        {
            // In memory only - the user's saved setting is never touched (nothing calls Persist here).
            EMenuBackground saved = ViewSettings.MenuBackground;
            background.FixedTime = null;
            foreach (EMenuBackground mode in new[] { EMenuBackground.Animated, EMenuBackground.Still })
            {
                ViewSettings.MenuBackground = mode;
                Console.WriteLine($"[menu-preview] bench {mode}: {Bench(background, w, h):0.00} ms/frame");
            }
            ViewSettings.MenuBackground = saved;
        }

        Image image = Raylib.LoadImageFromTexture(target.Texture);
        Raylib.ImageFlipVertical(ref image);
        bool ok = Raylib.ExportImage(image, outPath);
        Raylib.UnloadImage(image);

        Raylib.UnloadRenderTexture(target);
        background.Dispose();
        rlImGui.Shutdown();
        Raylib.CloseWindow();
        HoloSkirmish skirmish = background.Skirmish;
        Console.WriteLine(ok
            ? $"[menu-preview] wrote {outPath} ({w}x{h}, {state}, seed {seed}, t={time}s: {skirmish.Near.Abbr} vs {skirmish.Far.Abbr})"
            : $"[menu-preview] FAILED to write {outPath}");
        return ok ? 0 : 1;
    }

    /// <summary>Average wall-clock milliseconds for one background frame (prepare + present) in the
    /// hidden window, after a short warm-up. Each frame ends by drawing the finished picture into a 1x1
    /// target and reading that one pixel back: the read has to wait for the whole pipeline, so the number
    /// is the real GPU cost, not just command submission - without a full-screen copy dwarfing it.</summary>
    private static double Bench(MenuBackgroundRenderer background, int w, int h)
    {
        const int warmup = 30, frames = 240;
        var layout = MainMenuLayout.Compute(w, h);
        RenderTexture2D probe = Raylib.LoadRenderTexture(1, 1);
        var sw = new Stopwatch();
        for (int i = 0; i < warmup + frames; i++)
        {
            if (i == warmup) sw.Start();
            Raylib.BeginDrawing();
            background.Prepare(w, h, layout);
            Raylib.BeginTextureMode(probe);
            background.Present();
            Raylib.EndTextureMode();
            Raylib.EndDrawing();
            Image pixel = Raylib.LoadImageFromTexture(probe.Texture);   // sync point
            Raylib.UnloadImage(pixel);
        }
        double ms = sw.Elapsed.TotalMilliseconds / frames;
        Raylib.UnloadRenderTexture(probe);
        return ms;
    }
}
