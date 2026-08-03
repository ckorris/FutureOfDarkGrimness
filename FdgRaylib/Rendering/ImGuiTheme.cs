using System.Numerics;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// A single "Dark Grimness" ImGui theme applied once at startup, so every screen (menu, lobby, army
/// builder, log, tooltips) shares one look instead of the default battleship-gray. The palette is a cool
/// charcoal/steel base with a single warm amber accent for interactive highlights (hover, active,
/// selection, checkmarks) so controls read against the cold background.
///
/// Call <see cref="Apply"/> after <c>rlImGui.Setup</c> but BEFORE <c>ScaleAllSizes</c>, so the rounding
/// and border sizes set here are scaled for the display along with the rest of the style. Colors are
/// unaffected by <c>ScaleAllSizes</c>. Screens that push their own local colors still override as before.
/// </summary>
public static class ImGuiTheme
{
    // Base palette (cool charcoal -> steel).
    private static readonly Vector4 Ink        = R(15, 17, 20);    // darkest wells (title, scrollbar bg)
    private static readonly Vector4 Panel       = R(26, 29, 34);   // window body
    private static readonly Vector4 PanelRaised = R(34, 38, 44);   // buttons, headers
    private static readonly Vector4 Frame        = R(21, 24, 28);   // input fields
    private static readonly Vector4 FrameHover   = R(33, 37, 43);
    private static readonly Vector4 FrameActive  = R(40, 45, 52);
    private static readonly Vector4 Steel        = R(64, 71, 82);   // borders, grips, separators
    private static readonly Vector4 SteelDim     = R(44, 49, 57);
    private static readonly Vector4 Text         = R(224, 227, 231);
    private static readonly Vector4 TextDim      = R(126, 132, 142);

    // Blue accent + a muted "pressed" variant.
    private static readonly Vector4 Accent     = R(66, 135, 224);
    private static readonly Vector4 AccentHot   = R(96, 165, 245);
    private static readonly Vector4 AccentDim   = R(34, 60, 100);

    // Shared bits screens reach for directly (not part of the ImGui color table), kept here so every
    // screen pulls one source of truth instead of redefining them.

    // Light-blue accent for section/column headers and dialog titles — the "this is a header" blue used
    // across the lobby and the Host/Client dialogs.
    public static readonly Vector4 HeaderAccent = new(0.50f, 0.73f, 1.0f, 1f);

    // The theme's blue accent, exposed for surfaces that paint their own draw-list shapes in it —
    // the army list overlay's stat pills (#329) match the printed Army Forge pills with this.
    public static readonly Vector4 AccentBlue = Accent;

    // The darkest well tone, for the value half of a two-tone pill (#329).
    public static readonly Vector4 InkWell = Ink;

    // Opaque panel fill for modal dialogs that float above a dimmed backdrop (Host/Client). Matches the
    // lobby's panel tone (the theme window body) so the dialogs read as the same surface, not a blue slab.
    public static readonly Vector4 DialogPanelBg = Panel;

    public static void Apply()
    {
        var style = ImGui.GetStyle();

        style.WindowRounding    = 5f;
        style.ChildRounding     = 5f;
        style.FrameRounding     = 3f;
        style.PopupRounding     = 4f;
        style.ScrollbarRounding = 3f;
        style.GrabRounding      = 3f;
        style.TabRounding       = 4f;
        style.WindowBorderSize  = 1f;
        style.FrameBorderSize   = 1f;
        style.PopupBorderSize   = 1f;

        var c = style.Colors;
        c[(int)ImGuiCol.Text]                  = Text;
        c[(int)ImGuiCol.TextDisabled]          = TextDim;
        c[(int)ImGuiCol.WindowBg]              = Panel;
        c[(int)ImGuiCol.ChildBg]               = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.PopupBg]               = A(Ink, 0.98f);
        c[(int)ImGuiCol.Border]                = A(Steel, 0.50f);
        c[(int)ImGuiCol.BorderShadow]          = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.FrameBg]               = Frame;
        c[(int)ImGuiCol.FrameBgHovered]        = FrameHover;
        c[(int)ImGuiCol.FrameBgActive]         = FrameActive;
        c[(int)ImGuiCol.TitleBg]               = Ink;
        c[(int)ImGuiCol.TitleBgActive]         = PanelRaised;
        c[(int)ImGuiCol.TitleBgCollapsed]      = A(Ink, 0.75f);
        c[(int)ImGuiCol.MenuBarBg]             = Panel;
        c[(int)ImGuiCol.ScrollbarBg]           = A(Ink, 0.60f);
        c[(int)ImGuiCol.ScrollbarGrab]         = SteelDim;
        c[(int)ImGuiCol.ScrollbarGrabHovered]  = Steel;
        c[(int)ImGuiCol.ScrollbarGrabActive]   = Accent;
        c[(int)ImGuiCol.CheckMark]             = AccentHot;
        c[(int)ImGuiCol.SliderGrab]            = Accent;
        c[(int)ImGuiCol.SliderGrabActive]      = AccentHot;
        // Buttons sit clearly above the window body so they read as controls, not flat panels.
        c[(int)ImGuiCol.Button]                = R(48, 55, 65);
        c[(int)ImGuiCol.ButtonHovered]         = R(62, 71, 83);
        c[(int)ImGuiCol.ButtonActive]          = AccentDim;
        c[(int)ImGuiCol.Header]                = SteelDim;
        c[(int)ImGuiCol.HeaderHovered]         = R(52, 58, 67);
        c[(int)ImGuiCol.HeaderActive]          = AccentDim;
        c[(int)ImGuiCol.Separator]             = A(Steel, 0.50f);
        c[(int)ImGuiCol.SeparatorHovered]      = Accent;
        c[(int)ImGuiCol.SeparatorActive]       = AccentHot;
        c[(int)ImGuiCol.ResizeGrip]            = A(Steel, 0.40f);
        c[(int)ImGuiCol.ResizeGripHovered]     = A(Accent, 0.70f);
        c[(int)ImGuiCol.ResizeGripActive]      = Accent;
        c[(int)ImGuiCol.Tab]                   = Ink;
        c[(int)ImGuiCol.TabHovered]            = AccentDim;
        c[(int)ImGuiCol.TabSelected]           = PanelRaised;
        c[(int)ImGuiCol.TabDimmed]             = Ink;
        c[(int)ImGuiCol.TabDimmedSelected]     = SteelDim;
        c[(int)ImGuiCol.PlotLines]             = TextDim;
        c[(int)ImGuiCol.PlotLinesHovered]      = AccentHot;
        c[(int)ImGuiCol.PlotHistogram]         = Accent;
        c[(int)ImGuiCol.PlotHistogramHovered]  = AccentHot;
        c[(int)ImGuiCol.TableHeaderBg]         = R(35, 43, 57); // subtle blue band -- header text carries the emphasis
        c[(int)ImGuiCol.TableBorderStrong]     = Steel;
        c[(int)ImGuiCol.TableBorderLight]      = SteelDim;
        c[(int)ImGuiCol.TableRowBg]            = new Vector4(0, 0, 0, 0);
        c[(int)ImGuiCol.TableRowBgAlt]         = A(Steel, 0.06f);
        c[(int)ImGuiCol.TextSelectedBg]        = A(Accent, 0.35f);
        c[(int)ImGuiCol.DragDropTarget]        = AccentHot;
        c[(int)ImGuiCol.NavCursor]             = Accent;
        c[(int)ImGuiCol.NavWindowingHighlight] = A(AccentHot, 0.70f);
        c[(int)ImGuiCol.NavWindowingDimBg]     = R2(20, 20, 24, 0.20f);
        c[(int)ImGuiCol.ModalWindowDimBg]      = R2(10, 11, 13, 0.55f);
    }

    // 0-255 RGB -> opaque Vector4.
    private static Vector4 R(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    // 0-255 RGB + explicit alpha.
    private static Vector4 R2(int r, int g, int b, float a) => new(r / 255f, g / 255f, b / 255f, a);

    // Recolor with a new alpha.
    private static Vector4 A(Vector4 v, float a) => new(v.X, v.Y, v.Z, a);
}
