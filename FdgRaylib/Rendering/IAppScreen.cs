namespace FdgRaylib.Rendering;

public interface IAppScreen
{
    void Draw(int screenW, int screenH);

    /// <summary>
    /// Called by <see cref="RaylibRenderer.NavigateTo"/> every time this screen becomes the current one.
    /// Screens whose fields are seeded from state that can change while they're off screen (the
    /// host/join dialogs and the saved player name, #310) re-read it here; everything else ignores it.
    /// </summary>
    void OnShown() { }

    /// <summary>
    /// True for screens drawn over the main menu's holographic war table (#448): the menu itself and the
    /// Host/Join dialogs that open from it. The renderer paints the table before the screen's ImGui pass.
    /// </summary>
    bool ShowsMenuBackground => false;
}
