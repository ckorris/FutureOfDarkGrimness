using Raylib_cs;
using rlImGui_cs;

namespace FdgRaylib.Rendering.MenuBackground;

/// <summary>
/// <c>--menu-preview &lt;out.png&gt; [WxH] [menu|privacy|options]</c> (#448): renders the main menu - the
/// war table plus the real ImGui menu, fonts and theme - into an image from a hidden window, then exits.
/// For checking the look on a given screen size without opening (or driving) a visible window, the same
/// way <c>--field-harness</c> checks the tactical overlay. Clicks nothing and saves no settings.
/// </summary>
internal static class MenuPreview
{
    public static int Run(string outPath, int width, int height, string state)
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
        Raylib.InitWindow(width, height, "menu-preview");
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        RaylibRenderer.SetUpImGui(h);

        var menu = new MainMenuScreen();
        menu.OpenForPreview(state);
        var background = new MenuBackgroundRenderer();
        RenderTexture2D target = Raylib.LoadRenderTexture(w, h);

        // A few frames: ImGui sizes auto-fit popups from the previous frame's content.
        for (int frame = 0; frame < 4; frame++)
        {
            Raylib.BeginDrawing();
            Raylib.BeginTextureMode(target);
            Raylib.ClearBackground(new Color(30, 30, 30, 255));
            background.Draw(w, h, MainMenuLayout.Compute(w, h));
            rlImGui.Begin();
            menu.Draw(w, h);
            rlImGui.End();
            Raylib.EndTextureMode();
            Raylib.EndDrawing();
        }

        Image image = Raylib.LoadImageFromTexture(target.Texture);
        Raylib.ImageFlipVertical(ref image);
        bool ok = Raylib.ExportImage(image, outPath);
        Raylib.UnloadImage(image);

        Raylib.UnloadRenderTexture(target);
        background.Dispose();
        rlImGui.Shutdown();
        Raylib.CloseWindow();
        Console.WriteLine(ok ? $"[menu-preview] wrote {outPath} ({w}x{h}, {state})" : $"[menu-preview] FAILED to write {outPath}");
        return ok ? 0 : 1;
    }
}
