using System.Numerics;
using FdgRaylib.Config;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// Borderless-windowed fullscreen on/off (#310): F11 and the Options "Fullscreen" checkbox both go through
/// here, and either one remembers the choice in <see cref="UserConfig.StartFullscreen"/>.
/// <para>Requests are deferred to the top of the next frame (<see cref="Update"/>) so a click inside the
/// Options panel never resizes the window in the middle of an ImGui frame.</para>
/// <para>Leaving fullscreen does not trust raylib's own restore: on Cinnamon/GNOME X11 the window manager
/// keeps the window pinned to the monitor (see <see cref="X11WindowState"/>), so the state is cleared and
/// the pre-fullscreen size and position are put back explicitly. Harmless where raylib's restore works.</para>
/// </summary>
public static class WindowMode
{
    private const int DefaultWidth = 1280;
    private const int DefaultHeight = 720;

    private static bool _toggleRequested;
    private static int _restoreWidth = DefaultWidth;
    private static int _restoreHeight = DefaultHeight;
    private static Vector2? _restorePosition;

    public static bool IsFullscreen => Raylib.IsWindowState(ConfigFlags.BorderlessWindowMode);

    /// <summary>Flip the mode at the start of the next frame and remember it.</summary>
    public static void RequestToggle() => _toggleRequested = true;

    /// <summary>Once per frame, before drawing: F11 or a pending <see cref="RequestToggle"/>.</summary>
    public static void Update()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.F11)) _toggleRequested = true;
        if (!_toggleRequested) return;
        _toggleRequested = false;

        Set(!IsFullscreen);
        UserConfig.Current.StartFullscreen = IsFullscreen;
        UserConfig.Save();
    }

    /// <summary>Startup, right after InitWindow: apply the saved preference without re-saving it. The
    /// window has not been placed by the WM yet, so its position is not remembered - leaving fullscreen
    /// later centres the window on its monitor instead.</summary>
    public static void ApplyAtStartup(bool fullscreen)
    {
        if (!fullscreen || IsFullscreen) return;
        _restorePosition = null;
        Raylib.ToggleBorderlessWindowed();
    }

    /// <summary>Enter or leave borderless fullscreen; a no-op if already in that mode.</summary>
    public static void Set(bool fullscreen)
    {
        if (fullscreen == IsFullscreen) return;

        if (fullscreen)
        {
            _restoreWidth = Raylib.GetScreenWidth();
            _restoreHeight = Raylib.GetScreenHeight();
            _restorePosition = Raylib.GetWindowPosition();
            Raylib.ToggleBorderlessWindowed();
            return;
        }

        Raylib.ToggleBorderlessWindowed();
        X11WindowState.ClearFullscreen();
        Raylib.SetWindowSize(_restoreWidth, _restoreHeight);
        if (_restorePosition is { } p)
        {
            Raylib.SetWindowPosition((int)p.X, (int)p.Y);
        }
        else
        {
            int monitor = Raylib.GetCurrentMonitor();
            Vector2 origin = Raylib.GetMonitorPosition(monitor);
            Raylib.SetWindowPosition(
                (int)origin.X + (Raylib.GetMonitorWidth(monitor) - _restoreWidth) / 2,
                (int)origin.Y + (Raylib.GetMonitorHeight(monitor) - _restoreHeight) / 2);
        }
    }
}
