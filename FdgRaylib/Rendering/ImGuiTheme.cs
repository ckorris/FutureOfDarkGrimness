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
    // Base palette (cool charcoal -> steel). #398: every background tone is exactly 10/255 lighter than
    // it originally was - enough to stop each panel reading as the same black rectangle, and to leave a
    // shade BELOW the body tone for showing something switched off, without turning the grimdark grey.
    // (A first pass lifted them ~3x that far and was too much; the owner asked for 10.) Relative order
    // is unchanged, so every colour below still means what it meant. Text tones are the originals: the
    // backgrounds moved a hair, so their contrast did not need buying back.
    private static readonly Vector4 Ink        = R(25, 27, 30);    // darkest wells (title, scrollbar bg)
    private static readonly Vector4 Panel       = R(36, 39, 44);   // window body
    private static readonly Vector4 PanelRaised = R(44, 48, 54);   // buttons, headers
    private static readonly Vector4 Frame        = R(31, 34, 38);   // input fields
    private static readonly Vector4 FrameHover   = R(43, 47, 53);
    private static readonly Vector4 FrameActive  = R(50, 55, 62);
    private static readonly Vector4 Steel        = R(74, 81, 92);   // borders, grips, separators
    private static readonly Vector4 SteelDim     = R(54, 59, 67);
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

    // #398: amber means DAMAGE, wherever damage is shown - the wounds figure, the spent portion of a
    // wound meter, a wounded unit's pill. The army list already used this family for a wounded Tough
    // pill; this is the same idea given one name, bright enough to carry large text on the charcoal.
    public static readonly Vector4 DamageAmber = new(0.95f, 0.72f, 0.30f, 1f);

    // #227's hero gold, shared so a hero reads the same in the printed list and in the calculator.
    public static readonly Vector4 HeroGold = new(1f, 0.85f, 0.3f, 1f);

    // #292's rule blue, now the ONE colour a special rule's name is drawn in, wherever it appears: the
    // calculator's weapon sublines, the Forge's weapon table, a unit's rule list, an upgrade label. It
    // used to be this blue in the middle column, white in the side columns' weapon table and grey in
    // their rule lists - three colours for one kind of thing, so nothing taught the reader that an
    // underlined blue word is a rule they can hover.
    public static readonly Vector4 RuleBlue = new(0.45f, 0.80f, 0.90f, 1f);

    /// <summary>The same blue for a rule on something switched off - an out-of-range weapon. Darkened
    /// rather than greyed, so it still reads as a rule name.</summary>
    public static readonly Vector4 RuleBlueDim = new(0.28f, 0.50f, 0.58f, 1f);

    // Button colours by ACTION CLASS, pushed by UiButton so the class a call site picked is visible on
    // screen: neutral grey undoes (Back, Cancel, Quit), muted blue goes somewhere (Choose unit, Load
    // list, Swap), full accent commits (LAUNCH, CREATE). Telling them apart was guesswork when all
    // three were the same grey.
    public static readonly Vector4 ButtonBack        = R(64, 70, 80);
    public static readonly Vector4 ButtonBackHovered = R(84, 91, 102);
    public static readonly Vector4 ButtonGo          = R(44, 64, 96);
    public static readonly Vector4 ButtonGoHovered   = R(62, 88, 126);
    public static readonly Vector4 ButtonCommit      = Accent;
    public static readonly Vector4 ButtonCommitHover = AccentHot;
    public static readonly Vector4 ButtonPressed     = AccentDim;

    /// <summary>A tab nobody is on: darker than the body it sits above, so the accent-blue selected tab
    /// is the only lit thing in the strip.</summary>
    public static readonly Vector4 TabIdle = R(28, 31, 36);

    /// <summary>The brighter accent, for a selected tab or a hovered commit.</summary>
    public static readonly Vector4 AccentBright = AccentHot;

    /// <summary>
    /// Behind a table row that is switched off - an out-of-range weapon. A shade DARKER than the body,
    /// which is what "recessed" has to mean now the body itself is lighter; paired with the disabled
    /// alpha on its text it is unmistakable at a glance, which greyed text alone was not.
    /// </summary>
    public static readonly Vector4 DisabledRowBg = R2(10, 11, 13, 0.45f);

    // #398: the three bands of the calculator's distance track - every weapon reaches this far, only
    // some do, none do. Traffic-light reading, but darkened well below the text and the slider grab that
    // sit on top of them: these are a BACKGROUND, and a saturated green would fight the numbers for
    // attention when the answer is simply "yes, everything can shoot".
    public static readonly Vector4 RangeAllZone  = R(24, 58, 30);
    public static readonly Vector4 RangeSomeZone = R(74, 60, 16);
    public static readonly Vector4 RangeNoneZone = R(78, 26, 26);

    // Opaque panel fill for modal dialogs that float above a dimmed backdrop (Host/Client). Matches the
    // lobby's panel tone (the theme window body) so the dialogs read as the same surface, not a blue slab.
    public static readonly Vector4 DialogPanelBg = Panel;

    /// <summary>
    /// Background for the table hover tooltips (unit, terrain, and the unit-picker's). A rule-heavy unit's
    /// tooltip is tall enough to blanket the board under the cursor, so it paints on a translucent well
    /// rather than the near-opaque <c>PopupBg</c> the menus use — the models and terrain it covers stay
    /// readable through it. Same Ink tone, so it is the theme's popup, just thinner.
    /// </summary>
    public static readonly Vector4 TooltipBg = A(Ink, 0.72f);

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
        // Buttons sit clearly above the window body so they read as controls, not flat panels. This is
        // the unclassed default; UiButton pushes a role colour over it (see ButtonBack/Go/Commit).
        c[(int)ImGuiCol.Button]                = R(58, 65, 75);
        c[(int)ImGuiCol.ButtonHovered]         = R(72, 81, 93);
        c[(int)ImGuiCol.ButtonActive]          = AccentDim;
        c[(int)ImGuiCol.Header]                = SteelDim;
        c[(int)ImGuiCol.HeaderHovered]         = R(62, 68, 77);
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
        c[(int)ImGuiCol.TableHeaderBg]         = R(45, 53, 67); // subtle blue band -- header text carries the emphasis
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

    /// <summary>
    /// <c>ImGui.BeginTooltip</c> on the translucent <see cref="TooltipBg"/> well. The color has to be
    /// pushed BEFORE Begin — ImGui paints a window's background during Begin, so a push afterwards would
    /// land a frame's worth of nothing. Pair with <see cref="EndTranslucentTooltip"/>.
    ///
    /// Only PopupBg is pushed: text, separators and the border keep full alpha, so the tooltip reads
    /// exactly as before against whatever it now shows through.
    /// </summary>
    public static void BeginTranslucentTooltip()
    {
        ImGui.PushStyleColor(ImGuiCol.PopupBg, TooltipBg);
        ImGui.BeginTooltip();
    }

    /// <summary>Closes a <see cref="BeginTranslucentTooltip"/> and pops its pushed background.</summary>
    public static void EndTranslucentTooltip()
    {
        ImGui.EndTooltip();
        ImGui.PopStyleColor();
    }

    // 0-255 RGB -> opaque Vector4.
    private static Vector4 R(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);

    // 0-255 RGB + explicit alpha.
    private static Vector4 R2(int r, int g, int b, float a) => new(r / 255f, g / 255f, b / 255f, a);

    // Recolor with a new alpha.
    private static Vector4 A(Vector4 v, float a) => new(v.X, v.Y, v.Z, a);
}
