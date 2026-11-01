using System.Numerics;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>The main menu's three button tiers (#448): big primaries (Host/Solo, Join), solid secondaries
/// (Army Forge, Combat Calculator, Load Game), and outline-only "ghost" tertiaries (Options, Quit).</summary>
internal enum EHoloTier
{
    Primary,
    Secondary,
    Tertiary,
}

/// <summary>
/// The holographic command-panel look shared by the main menu and the dialogs that open over it (#448):
/// a translucent dark panel with a thin cyan border and bright corner brackets, square buttons in three
/// tiers, and a matching modal style for the privacy notice and the Options popup. Colours come from the
/// signed-off mockup (<c>docs/menu-background/mockup-terrain.jpg</c>).
/// </summary>
internal static class HoloUi
{
    private static Vector4 Rgba(int r, int g, int b, float a) => new(r / 255f, g / 255f, b / 255f, a);

    public static readonly Vector4 Cyan = Rgba(90, 200, 255, 1f);
    public static readonly Vector4 TitleText = Rgba(225, 245, 255, 1f);
    public static readonly Vector4 ButtonText = Rgba(235, 248, 255, 1f);
    public static readonly Vector4 GhostText = Rgba(175, 210, 235, 0.92f);

    private static readonly Vector4 PanelFill = Rgba(4, 12, 22, 0.59f);
    private static readonly Vector4 ModalFill = Rgba(4, 12, 22, 0.94f);
    private static readonly Vector4 TitleBar = Rgba(12, 44, 76, 0.95f);

    private static readonly Vector4 SolidIdle = Rgba(10, 40, 70, 0.45f);
    private static readonly Vector4 SolidHovered = Rgba(40, 140, 210, 0.60f);
    private static readonly Vector4 SolidActive = Rgba(60, 170, 240, 0.75f);
    private static readonly Vector4 GhostIdle = Rgba(10, 40, 70, 0.16f);
    private static readonly Vector4 GhostHovered = Rgba(30, 100, 160, 0.38f);
    private static readonly Vector4 GhostActive = Rgba(50, 140, 210, 0.55f);

    private static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

    private static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    /// <summary>The translucent panel behind the menu buttons, border and brackets included.</summary>
    public static void DrawPanel(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
    {
        dl.AddRectFilled(min, max, U32(PanelFill));
        dl.AddRect(min, max, U32(WithAlpha(Cyan, 0.35f)), 0f, ImDrawFlags.None, MathF.Max(1f, scale));
        DrawCornerBrackets(dl, min, max, scale);
    }

    /// <summary>Bright L-shaped brackets on the four corners of a rectangle.</summary>
    public static void DrawCornerBrackets(ImDrawListPtr dl, Vector2 min, Vector2 max, float scale)
    {
        float len = 24f * scale, th = MathF.Max(2f, 3f * scale);
        uint col = U32(Cyan);
        foreach (var (corner, sx, sy) in new[]
                 {
                     (min, 1f, 1f), (new Vector2(max.X, min.Y), -1f, 1f),
                     (new Vector2(min.X, max.Y), 1f, -1f), (max, -1f, -1f),
                 })
        {
            dl.AddLine(corner, corner + new Vector2(len * sx, 0f), col, th);
            dl.AddLine(corner, corner + new Vector2(0f, len * sy), col, th);
        }
    }

    /// <summary>
    /// One command-panel button at the cursor. Primaries and secondaries are solid and left-aligned with
    /// a chevron on hover; tertiaries are ghosts (faint fill, dimmer text, centred). The caller sets the
    /// font and its scale first. <paramref name="centered"/> forces centred text on any tier (dialog OK /
    /// Back buttons).
    /// </summary>
    public static bool Button(string label, Vector2 size, EHoloTier tier, UiButton.EClass sound, float scale,
        bool centered = false)
    {
        bool ghost = tier == EHoloTier.Tertiary;
        bool center = centered || ghost;

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, center ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(22f * scale, ImGui.GetStyle().FramePadding.Y));
        ImGui.PushStyleColor(ImGuiCol.Border, WithAlpha(Cyan, ghost ? 0.43f : 0.59f));
        ImGui.PushStyleColor(ImGuiCol.Text, ghost ? GhostText : ButtonText);

        bool clicked = ghost
            ? UiButton.Tinted(label, size, sound, GhostIdle, GhostHovered, GhostActive)
            : UiButton.Tinted(label, size, sound, SolidIdle, SolidHovered, SolidActive);

        if (ImGui.IsItemHovered())
        {
            // A hovered button lights its whole frame, not just its fill.
            var dl = ImGui.GetWindowDrawList();
            Vector2 min = ImGui.GetItemRectMin(), max = ImGui.GetItemRectMax();
            dl.AddRect(min, max, U32(WithAlpha(Cyan, 0.9f)), 0f, ImDrawFlags.None, MathF.Max(1f, 2f * scale));
            if (!center)
            {
                float fontSize = ImGui.GetFontSize();
                Vector2 chevron = ImGui.CalcTextSize(">");
                dl.AddText(new Vector2(max.X - 22f * scale - chevron.X, (min.Y + max.Y - fontSize) * 0.5f),
                    U32(ButtonText), ">");
            }
        }

        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
        return clicked;
    }

    /// <summary>Push before <c>BeginPopupModal</c> (and pop straight after it) so a dialog over the menu
    /// wears the panel look: dark translucent fill, thin cyan border, a navy title bar, square corners and
    /// roomier padding.</summary>
    public static void PushModalStyle(float scale)
    {
        ImGui.PushStyleColor(ImGuiCol.PopupBg, ModalFill);
        ImGui.PushStyleColor(ImGuiCol.Border, WithAlpha(Cyan, 0.55f));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, TitleBar);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, TitleBar);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20f, 16f) * MathF.Max(1f, scale));
    }

    public static void PopModalStyle()
    {
        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(2);
    }

    /// <summary>Corner brackets on the current window, drawn unclipped so they sit on its border.</summary>
    public static void DrawWindowBrackets(float scale)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRectFullScreen();
        Vector2 pos = ImGui.GetWindowPos();
        DrawCornerBrackets(dl, pos, pos + ImGui.GetWindowSize(), scale);
        dl.PopClipRect();
    }

    /// <summary>The menu's scale factor for a screen (1.0 at 1920x1080), for dialogs sized off the screen.</summary>
    public static float ScaleFor(int screenW, int screenH) => MathF.Min(screenW / 1920f, screenH / 1080f);
}
