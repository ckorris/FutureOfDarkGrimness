using System;
using System.Collections.Generic;
using FDG;
using FDG.Presentation;
using FDG.Presentation.Beats;
using Raylib_cs;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Draws the live <see cref="DiceRolledBeat"/> panels as a lower-third caption STACK docked to the
/// bottom-center of the table viewport (#245, #327) — the subtitle convention: the action plays out on
/// the table while the numbers narrate from the caption zone, never covering the units or the
/// concurrent attack animation (#238). Each panel is: a standalone <b>target badge</b> (the success
/// threshold, e.g. "4+", big enough to read before the dice settle) over the roll's category word
/// (ATTACK / SAVE / CAST, matching the panel's accent stripe), a <b>header</b> with the roll's purpose
/// (<see cref="DiceRolledBeat.Label"/>), an optional dim <b>context</b> line (who's rolling at
/// whom), the dice themselves, optional <b>modifier chips</b> ("Quality 4+ | Stealth -1") and gold
/// <b>proc chips</b> ("Furious +2 on 6s") — top-face successes get a gold rim when procs fired —
/// and a <b>result line</b> with the settled outcome (<see cref="DiceRolledBeat.ResultSummary"/>).
/// Beats carrying chips arrive pre-stretched by the engine so there is time to read them.
///
/// <para>#327: a panel outlives its beat, so several are legible at once. The OLDEST keeps the bottom
/// anchor and newer ones stack above it, each dimmed by depth so the newest reads loudest; hovering
/// the stack freezes the timers and restores every panel to full strength. When the stack would not fit
/// the vertical budget the oldest panels are dropped from the layout, never the newest — the top of the
/// stack is the one furthest from the anchor, so it is the one at risk of running off screen.</para>
///
/// <para>If an attack animation's screen bounds still reach the strip (units fighting at the
/// bottom table edge), the panel fades to a ghost instead of moving — consistent anchor, graceful
/// degradation.</para>
///
/// <para>Two vocabularies keyed off the roller mode:</para>
/// <list type="bullet">
/// <item><b>Realistic</b> — the actual dice as pip faces; successes in the roll's category accent
/// (ember / green / arcane / amethyst, matching the stripe and badge word), failures gray; a brief
/// "settle" tumble at the start reads as rolling.</item>
/// <item><b>Probabilistic</b> — no discrete dice exist (fractional), so a labeled success bar, its
/// filled portion in the same category accent.</item>
/// </list>
///
/// <para>A <see cref="RollOffBeat"/> is a panel on the same stack (#327), drawn as a labelled name+die
/// list instead of a caption. It already shared this caption zone — placement continuity beat
/// stakes-based prominence at the 2026-07-18 playtest — but it used to draw itself there independently,
/// so the first-turn roll-off landed on top of the still-lingering objective-count roll at game start.
/// On the stack it queues above whatever is still up.</para>
/// </summary>
public static class DiceOverlay
{
    private const float FlickerEnd = 0.3f; // fraction of the beat spent "rolling" before faces lock; rest lingers settled
    private const float TumbleHz   = 9f;   // face-change rate while rolling (per-frame swaps strobe)

    // Every panel dimension below is a design number run through Sc() once, so the whole strip —
    // type, dice, chips, padding, badge — grows or shrinks together from this single knob.
    private const float PanelScale = 1.25f;

    private static int Sc(int v) => (int)MathF.Round(v * PanelScale);

    private static readonly int HeaderSize   = Sc(22);
    private static readonly int ContextSize  = Sc(18);
    private static readonly int ResultSize   = Sc(20);
    private static readonly int BadgeSize    = Sc(40);   // the standalone target number
    private static readonly int BadgePad     = Sc(10);
    private static readonly int CategorySize = Sc(12);   // the ATTACK / SAVE / CAST word under the badge
    private static readonly int ChipSize     = Sc(16);
    private static readonly int ChipPadX     = Sc(7);
    private static readonly int ChipPadY     = Sc(3);
    private static readonly int ChipGap      = Sc(6);
    private static readonly int PanelPad     = Sc(16);
    private static readonly int RowGap       = Sc(8);
    private static readonly int ColGap       = Sc(16);
    private static readonly int DieSize      = Sc(44);
    private static readonly int DieGap       = Sc(8);
    private static readonly int DieMin       = Sc(16);   // floor when a huge pool has to shrink
    private static readonly int SideReserve  = Sc(260);  // width kept clear for the badge column + margins
    private const           int BottomMargin = 18;       // dock gap, not panel geometry — unscaled

    private static readonly int RollOffNameSize = Sc(22); // competitor names on a roll-off panel

    private const float OverlapDim = 0.35f; // ghost alpha while the attack animation overlaps the strip

    // #327 stack geometry.
    private const int StackGap    = 8;   // between panels
    private const int StackTopMin = 56;  // the stack never climbs over the status HUD strip
    // Depth dim, counted from the newest panel down: the current roll reads at full strength and the
    // history behind it steps back a little further each time. Hovering the stack restores every panel
    // to full — that is the "let me re-read that" affordance. The last entry repeats for deeper stacks.
    private static readonly float[] DepthAlpha = { 1f, 0.62f, 0.42f };

    private static readonly Color Panel    = new(20, 20, 24, 210);
    private static readonly Color BadgeBg  = new(42, 38, 26, 230);
    private static readonly Color Success  = new(60, 170, 70, 255);   // roll-offs only — see AccentFor
    private static readonly Color Fail     = new(110, 110, 110, 255);
    private static readonly Color Rolling  = new(225, 225, 225, 255);
    private static readonly Color Header   = new(235, 235, 235, 255);
    private static readonly Color Result   = new(255, 225, 150, 255); // gold — the settled "what it means"
    private static readonly Color Hint     = new(170, 170, 175, 255); // dim — the "..." while rolling
    private static readonly Color Tie      = new(228, 200, 60, 255);  // yellow — tied for the win (re-rolls)

    // #245 category accents: the edge stripe + badge word color-code what the roll is FOR. The word
    // is the redundant channel (color alone would fail a colorblind glance). Successful dice wear the
    // same accent, so the stripe, the badge word and the settled faces all say one thing; failures
    // stay neutral gray, which is what actually carries the pass/fail read.
    // Hues sit roughly 70-90 degrees apart so no two are confusable in a 4px stripe or a small die.
    private static readonly Color OffenseAccent = new(215, 95, 60, 255);   // ember    — attacks
    private static readonly Color DefenseAccent = new(80, 190, 95, 255);   // green    — saves
    private static readonly Color MagicAccent   = new(85, 170, 225, 255);  // arcane   — cast rolls
    private static readonly Color MiscAccent    = new(160, 115, 210, 255); // amethyst — morale, terrain, objectives

    private static readonly Color ChipBg     = new(45, 45, 52, 230);
    private static readonly Color ChipText   = new(210, 210, 215, 255);
    private static readonly Color ProcChipBg = new(58, 48, 24, 230);

    // Per-panel smoothing state, keyed by beat reference and pruned each frame. Render-thread only.
    // The overlap ghosting was already eased; #327 made it per-panel (panels at different heights
    // overlap the attack differently, and one shared value had them fighting over it) and added the
    // depth dim, which likewise eases — a panel stepping straight from full to dim the instant the next
    // roll appears reads as a flicker rather than as history receding.
    private sealed class PanelFx
    {
        public float Dim = 1f;
        public float Depth = -1f;   // negative until the panel's first frame, which seeds it
    }

    private static readonly Dictionary<PresentationBeat, PanelFx> _fx = new();
    private static double _lastDrawTime;

    /// <summary>
    /// Draws every live roll panel (#327) — dice rolls and roll-offs alike — taking the stack OLDEST
    /// FIRST exactly as <c>PresentationPlayer.GetRollStack</c> returns it, and returns the screen bounds
    /// the stack occupies so the caller can hit-test the pointer for hover-freeze. Zero-size when nothing
    /// is up.
    /// </summary>
    /// <param name="hovered">Frozen under the pointer: every panel draws at full strength, so the
    /// history can be read instead of merely glimpsed.</param>
    public static Rectangle DrawStack(
        IReadOnlyList<(PresentationBeat beat, float progress, float alpha)> stack,
        int areaWidth, int screenH, Rectangle? avoid, bool hovered)
    {
        if (stack.Count == 0)
        {
            _fx.Clear();
            return new Rectangle(0, 0, 0, 0);
        }

        // Measure first: panels are anchored from the BOTTOM up, so each one's Y depends on the heights
        // of everything below it.
        var layouts = new PanelLayout[stack.Count];
        for (int i = 0; i < stack.Count; i++) layouts[i] = Measure(stack[i].beat, areaWidth);

        // Trim the OLDEST panels while the stack overflows its vertical budget. The newest sits highest,
        // so an overflow always threatens the panel that matters most — the trim has to come off the
        // anchored end. The last panel is never dropped, however tall it is.
        int totalH = 0;
        for (int i = 0; i < layouts.Length; i++) totalH += layouts[i].PanelH + (i > 0 ? StackGap : 0);
        int budget = screenH - BottomMargin - StackTopMin;
        int first = 0;
        while (first < layouts.Length - 1 && totalH > budget)
        {
            totalH -= layouts[first].PanelH + StackGap;
            first++;
        }

        float dt = FrameDelta();
        PruneFx(stack);

        int left = int.MaxValue, right = 0, top = int.MaxValue;
        int bottom = screenH - BottomMargin;    // the anchor: the oldest drawn panel's bottom edge
        int stackBottom = bottom;
        for (int i = first; i < stack.Count; i++)
        {
            PanelLayout layout = layouts[i];
            int panelY = bottom - layout.PanelH;
            int panelX = (areaWidth - layout.PanelW) / 2;

            // Depth counted from the TOP of the stack, so the newest roll is always the loudest one
            // regardless of how many are up.
            int depth = stack.Count - 1 - i;
            float depthAlpha = hovered ? 1f : DepthAlpha[Math.Min(depth, DepthAlpha.Length - 1)];

            DrawPanel(stack[i].beat, layout, stack[i].progress, stack[i].alpha,
                panelX, panelY, avoid, depthAlpha, dt);

            left  = Math.Min(left, panelX);
            right = Math.Max(right, panelX + layout.PanelW);
            top   = Math.Min(top, panelY);
            bottom = panelY - StackGap;
        }

        return new Rectangle(left, top, right - left, stackBottom - top);
    }

    /// <summary>
    /// Everything about a panel that has to be known before it can be positioned. Measuring is split
    /// from drawing because a bottom-anchored stack has to size every panel before it can place any of
    /// them — and because the settled text is known from the start (the tumble is purely cosmetic), so
    /// a panel is sized for its final content up front and never reflows at the settle instant.
    /// </summary>
    private sealed class PanelLayout
    {
        public bool RollOff;                       // a labelled name+die stack rather than a dice caption
        public int NameColW, RowsH, RowGapUsed, ColGapUsed;  // roll-off
        public bool Probabilistic;
        public readonly List<int> Faces = new();   // realistic: the histogram expanded into single dice
        public int DieSizeUsed, RowW, DiceH;       // realistic
        public int BarW, BarH;                     // probabilistic
        public List<(string Text, int W)>? ModChips;
        public List<(string Text, int W)>? ProcChips;
        public string Header = "", ResultLine = "", Badge = "";
        public int BadgeW, BadgeColH, ContentW, ContentH, PanelW, PanelH;
    }

    private static PanelLayout Measure(PresentationBeat beat, int areaWidth) => beat switch
    {
        RollOffBeat rollOff => MeasureRollOff(rollOff),
        DiceRolledBeat dice => MeasureDice(dice, areaWidth),
        _ => new PanelLayout(),
    };

    /// <summary>
    /// A roll-off's own shape: the label over one row per competitor (name on the left, that
    /// competitor's die on the right). No badge column and no chips — there is no threshold to show,
    /// the highest roll simply wins.
    /// </summary>
    private static PanelLayout MeasureRollOff(RollOffBeat beat)
    {
        var l = new PanelLayout { RollOff = true, Header = beat.Label };

        int count = beat.Entries?.Count ?? 0;
        l.DieSizeUsed = DieSize;
        l.RowGapUsed  = Sc(10);
        l.ColGapUsed  = Sc(18);

        if (beat.Entries != null)
            foreach (RollOffEntry e in beat.Entries)
                l.NameColW = Math.Max(l.NameColW, Raylib.MeasureText(e.Name, RollOffNameSize));

        l.RowsH = count > 0 ? count * l.DieSizeUsed + (count - 1) * l.RowGapUsed : 0;

        int innerW = Math.Max(Raylib.MeasureText(l.Header, HeaderSize),
            l.NameColW + l.ColGapUsed + l.DieSizeUsed);
        l.PanelW = innerW + PanelPad * 2;
        l.PanelH = PanelPad * 2 + HeaderSize + RowGap + l.RowsH;
        return l;
    }

    private static PanelLayout MeasureDice(DiceRolledBeat beat, int areaWidth)
    {
        var l = new PanelLayout
        {
            Probabilistic = beat.Mode == ERandomnessType.Probabilistic,
            Header     = beat.Label,
            ResultLine = ResultText(beat),
            Badge      = $"{beat.SuccessThreshold}+",
        };

        int maxChipRow = areaWidth - SideReserve;
        l.ModChips  = LayoutChips(beat.ModifierTags, maxChipRow);
        l.ProcChips = LayoutChips(beat.ProcTags, maxChipRow);

        if (l.Probabilistic)
        {
            // No discrete dice exist under the probabilistic roller — a success bar stands in for them.
            l.BarW = Math.Min(Sc(360), areaWidth - SideReserve);
            l.BarH = Sc(22);
        }
        else
        {
            // Expand the histogram into individual dice (rounded — realistic counts are whole numbers).
            for (int i = 0; i < beat.FaceCounts.Count; i++)
            {
                int count = (int)MathF.Round(beat.FaceCounts[i]);
                for (int n = 0; n < count; n++) l.Faces.Add(beat.SideMin + i);
            }

            // Size the dice row (shrink the die if there are many).
            l.DieSizeUsed = DieSize;
            if (l.Faces.Count > 0)
            {
                float maxRow = areaWidth - SideReserve; // leave room for the badge column + margins
                if (l.Faces.Count * (l.DieSizeUsed + DieGap) > maxRow)
                    l.DieSizeUsed = Math.Max(DieMin, (int)(maxRow / l.Faces.Count) - DieGap);
            }
            l.RowW  = l.Faces.Count > 0 ? l.Faces.Count * l.DieSizeUsed + (l.Faces.Count - 1) * DieGap : 0;
            l.DiceH = l.Faces.Count > 0 ? l.DieSizeUsed : 0;
        }

        (l.BadgeW, l.BadgeColH) = BadgeColumnSize(l.Badge, beat.Category);

        int chipH = ChipSize + ChipPadY * 2;
        int bodyW = l.Probabilistic ? l.BarW : l.RowW;
        l.ContentW = Math.Max(Raylib.MeasureText(l.Header, HeaderSize),
                     Math.Max(bodyW, Raylib.MeasureText(l.ResultLine, ResultSize)));
        if (beat.Context != null)
            l.ContentW = Math.Max(l.ContentW, Raylib.MeasureText(beat.Context, ContextSize));
        if (l.ModChips != null)  l.ContentW = Math.Max(l.ContentW, ChipsWidth(l.ModChips));
        if (l.ProcChips != null) l.ContentW = Math.Max(l.ContentW, ChipsWidth(l.ProcChips));

        int bodyH = l.Probabilistic ? RowGap + l.BarH : (l.DiceH > 0 ? RowGap + l.DiceH : 0);
        l.ContentH = HeaderSize
            + (beat.Context != null ? RowGap + ContextSize : 0)
            + bodyH
            + (l.ModChips != null ? RowGap + chipH : 0)
            + (l.ProcChips != null ? RowGap + chipH : 0)
            + RowGap + ResultSize;

        l.PanelW = PanelPad + l.BadgeW + ColGap + l.ContentW + PanelPad;
        l.PanelH = PanelPad * 2 + Math.Max(l.ContentH, l.BadgeColH);
        return l;
    }

    private static void DrawPanel(PresentationBeat beat, PanelLayout l, float progress, float alpha,
        int panelX, int panelY, Rectangle? avoid, float depthAlpha, float dt)
    {
        var panelRect = new Rectangle(panelX, panelY, l.PanelW, l.PanelH);
        float a = alpha * PanelFxFor(beat, panelRect, avoid, depthAlpha, dt);
        if (a <= 0.02f) return;

        if (beat is RollOffBeat rollOff) { DrawRollOffPanel(rollOff, l, progress, panelX, panelY, a); return; }
        if (beat is not DiceRolledBeat dice) return;

        // Probabilistic rolls have no rolling phase: there are no faces to tumble, so show the result at
        // once.
        bool settled = l.Probabilistic || progress >= FlickerEnd;
        bool procsFired = l.ProcChips != null;
        int chipH = ChipSize + ChipPadY * 2;

        Raylib.DrawRectangleRounded(panelRect, 0.18f, 6, Faded(Panel, a));
        DrawAccentStripe(panelX, panelY, l.PanelH, dice.Category, a);
        DrawBadgeColumn(panelX + PanelPad, panelY + (l.PanelH - l.BadgeColH) / 2, l.BadgeW, l.Badge,
            dice.Category, a);

        // Content column, centered within its own span (the badge offsets it from the panel center).
        int contentX = panelX + PanelPad + l.BadgeW + ColGap;
        int y = panelY + (l.PanelH - l.ContentH) / 2;
        DrawCenteredIn(l.Header, contentX, l.ContentW, y, HeaderSize, Faded(Header, a));
        y += HeaderSize;

        if (dice.Context != null)
        {
            y += RowGap;
            DrawCenteredIn(dice.Context, contentX, l.ContentW, y, ContextSize, Faded(Hint, a));
            y += ContextSize;
        }

        if (l.Probabilistic)
        {
            y += RowGap;
            int barX = contentX + (l.ContentW - l.BarW) / 2;
            Raylib.DrawRectangle(barX, y, l.BarW, l.BarH, Faded(Fail, a));
            float frac = dice.Total > 0f ? dice.Successes / dice.Total : 0f;
            Raylib.DrawRectangle(barX, y, (int)(l.BarW * Math.Clamp(frac, 0f, 1f)), l.BarH,
                Faded(AccentFor(dice.Category), a));
            Raylib.DrawRectangleLines(barX, y, l.BarW, l.BarH, Faded(Color.Black, a));
            y += l.BarH;
        }
        else if (l.DiceH > 0)
        {
            y += RowGap;
            Color successFill = AccentFor(dice.Category);
            int rowX = contentX + (l.ContentW - l.RowW) / 2;
            for (int i = 0; i < l.Faces.Count; i++)
            {
                int x = rowX + i * (l.DieSizeUsed + DieGap);
                int shownFace;
                Color fill, pip;
                if (settled)
                {
                    shownFace = l.Faces[i];
                    bool success = shownFace >= dice.SuccessThreshold;
                    fill = success ? successFill : Fail;
                    pip = Color.White;
                }
                else
                {
                    shownFace = TumbleFace(i, dice.SideMin, dice.SideMax);
                    fill = Rolling;
                    pip = new Color(30, 30, 30, 255);
                }
                DrawDie(x, y, l.DieSizeUsed, shownFace, fill, pip, a);
                // A top-face success with a proc riding it gets a gold rim — "that 6 did something".
                if (settled && procsFired && shownFace == dice.SideMax && shownFace >= dice.SuccessThreshold)
                    Raylib.DrawRectangleRoundedLines(
                        new Rectangle(x - 2, y - 2, l.DieSizeUsed + 4, l.DieSizeUsed + 4),
                        0.22f, 6, Faded(Result, a));
            }
            y += l.DieSizeUsed;
        }

        if (l.ModChips != null)
        {
            y += RowGap;
            DrawChips(l.ModChips, contentX, l.ContentW, y, ChipBg, ChipText, border: null, a);
            y += chipH;
        }
        if (l.ProcChips != null)
        {
            y += RowGap;
            DrawChips(l.ProcChips, contentX, l.ContentW, y, ProcChipBg, Result, border: Result, a);
            y += chipH;
        }

        y += RowGap;
        DrawCenteredIn(settled ? l.ResultLine : "...", contentX, l.ContentW, y, ResultSize,
            Faded(settled ? Result : Hint, a));
    }

    /// <summary>
    /// Draws a <see cref="RollOffBeat"/> panel — each competitor's name on the left, its die on the
    /// right — so it's clear who's rolling against whom. The sole highest roller's die turns green
    /// (Won); a shared highest turns yellow (TiedForWin) and the engine emits a fresh beat for the
    /// run-off. Dice tumble for the first fraction of the beat, then settle to the rolled face + colour.
    ///
    /// <para>#327: this is a panel ON THE STACK, not a separate overlay. It used to draw itself at the
    /// bottom anchor independently, which put the first-turn roll-off straight over the still-lingering
    /// objective-count roll at game start. Sharing the stack means it queues above whatever is still up,
    /// and it inherits the stack's fade, depth dim and hover-freeze instead of carrying its own
    /// envelope.</para>
    /// </summary>
    private static void DrawRollOffPanel(RollOffBeat beat, PanelLayout l, float progress,
        int panelX, int panelY, float a)
    {
        if (beat.Entries == null || beat.Entries.Count == 0) return;

        bool settled = progress >= FlickerEnd;

        Raylib.DrawRectangleRounded(new Rectangle(panelX, panelY, l.PanelW, l.PanelH), 0.12f, 6,
            Faded(Panel, a));
        DrawCenteredIn(l.Header, panelX, l.PanelW, panelY + PanelPad, HeaderSize, Faded(Header, a));

        int rowTop = panelY + PanelPad + HeaderSize + RowGap;
        int nameX  = panelX + PanelPad;
        int dieX   = panelX + PanelPad + l.NameColW + l.ColGapUsed;
        for (int i = 0; i < beat.Entries.Count; i++)
        {
            RollOffEntry e = beat.Entries[i];
            int rowY = rowTop + i * (l.DieSizeUsed + l.RowGapUsed);
            Raylib.DrawText(e.Name, nameX, rowY + (l.DieSizeUsed - RollOffNameSize) / 2, RollOffNameSize,
                Faded(Header, a));

            int face;
            Color fill, pip;
            if (settled)
            {
                face = e.Roll;
                fill = e.Result switch
                {
                    ERollOffResult.Won        => Success,
                    ERollOffResult.TiedForWin => Tie,
                    _                         => Fail,
                };
                pip = Color.White;
            }
            else
            {
                face = TumbleFace(i, 1, 6);
                fill = Rolling;
                pip  = new Color((byte)30, (byte)30, (byte)30, (byte)255);
            }
            DrawDie(dieX, rowY, l.DieSizeUsed, face, fill, pip, a);
        }
    }

    // The settled result line: the stage-supplied summary, or a generic successes/total fallback.
    private static string ResultText(DiceRolledBeat beat) =>
        beat.ResultSummary ?? $"{beat.Successes:0.##} / {beat.Total:0.##}";

    // ---------------- #245 category + badge column ----------------

    private static Color AccentFor(ERollBeatCategory category) => category switch
    {
        ERollBeatCategory.Offense => OffenseAccent,
        ERollBeatCategory.Defense => DefenseAccent,
        ERollBeatCategory.Magic   => MagicAccent,
        _                         => MiscAccent,
    };

    private static string CategoryWord(ERollBeatCategory category) => category switch
    {
        ERollBeatCategory.Offense => "ATTACK",
        ERollBeatCategory.Defense => "SAVE",
        ERollBeatCategory.Magic   => "CAST",
        _                         => "",
    };

    // A thin colored stripe down the panel's left edge — the peripheral-glance channel for the
    // roll's category (the badge word is the redundant, colorblind-safe one).
    private static void DrawAccentStripe(int panelX, int panelY, int panelH, ERollBeatCategory category, float a)
    {
        Raylib.DrawRectangle(panelX, panelY + 6, 4, panelH - 12, Faded(AccentFor(category), a));
    }

    private static (int Width, int Height) BadgeColumnSize(string badge, ERollBeatCategory category)
    {
        string word = CategoryWord(category);
        int badgeW = Raylib.MeasureText(badge, BadgeSize) + BadgePad * 2;
        int width  = Math.Max(badgeW, word.Length > 0 ? Raylib.MeasureText(word, CategorySize) : 0);
        int height = BadgeSize + BadgePad * 2 + (word.Length > 0 ? 4 + CategorySize : 0);
        return (width, height);
    }

    // The big standalone target number ("4+") — readable at a glance before the dice settle, and
    // still there afterwards so the settled faces can be interpreted without re-reading anything —
    // with the roll's category word beneath it in the accent color.
    private static void DrawBadgeColumn(int x, int y, int colW, string badge, ERollBeatCategory category, float a)
    {
        int badgeW = Raylib.MeasureText(badge, BadgeSize) + BadgePad * 2;
        int badgeH = BadgeSize + BadgePad * 2;
        int chipX  = x + (colW - badgeW) / 2;
        var rect = new Rectangle(chipX, y, badgeW, badgeH);
        Raylib.DrawRectangleRounded(rect, 0.25f, 6, Faded(BadgeBg, a));
        Raylib.DrawRectangleRoundedLines(rect, 0.25f, 6, Faded(Result, a));
        int tw = Raylib.MeasureText(badge, BadgeSize);
        Raylib.DrawText(badge, chipX + (badgeW - tw) / 2, y + (badgeH - BadgeSize) / 2, BadgeSize, Faded(Result, a));

        string word = CategoryWord(category);
        if (word.Length > 0)
            DrawCenteredIn(word, x, colW, y + badgeH + 4, CategorySize, Faded(AccentFor(category), a));
    }

    // ---------------- #245 info chips ----------------

    // Measures the chips for one row, truncating with a "+N" chip if the row would exceed maxWidth.
    // Null when there is nothing to show — the caller then reserves no row at all.
    private static List<(string Text, int W)>? LayoutChips(IReadOnlyList<string>? tags, int maxWidth)
    {
        if (tags == null || tags.Count == 0) return null;

        var chips = new List<(string, int)>();
        int used = 0;
        for (int i = 0; i < tags.Count; i++)
        {
            int w = Raylib.MeasureText(tags[i], ChipSize) + ChipPadX * 2;
            int gap = chips.Count > 0 ? ChipGap : 0;
            // Keep room for a potential "+N" tail chip when more tags follow.
            int tailReserve = i < tags.Count - 1 ? 50 : 0;
            if (used + gap + w + tailReserve > maxWidth && chips.Count > 0)
            {
                string more = $"+{tags.Count - i}";
                chips.Add((more, Raylib.MeasureText(more, ChipSize) + ChipPadX * 2));
                return chips;
            }
            chips.Add((tags[i], w));
            used += gap + w;
        }
        return chips;
    }

    private static int ChipsWidth(List<(string Text, int W)> chips)
    {
        int w = 0;
        for (int i = 0; i < chips.Count; i++) w += (i > 0 ? ChipGap : 0) + chips[i].W;
        return w;
    }

    private static void DrawChips(List<(string Text, int W)> chips, int x, int width, int y,
        Color bg, Color text, Color? border, float a)
    {
        int chipH = ChipSize + ChipPadY * 2;
        int cx = x + (width - ChipsWidth(chips)) / 2;
        foreach ((string chipText, int w) in chips)
        {
            var rect = new Rectangle(cx, y, w, chipH);
            Raylib.DrawRectangleRounded(rect, 0.35f, 6, Faded(bg, a));
            if (border.HasValue)
                Raylib.DrawRectangleRoundedLines(rect, 0.35f, 6, Faded(border.Value, a * 0.8f));
            Raylib.DrawText(chipText, cx + ChipPadX, y + ChipPadY, ChipSize, Faded(text, a));
            cx += w + ChipGap;
        }
    }

    // ---------------- shared drawing ----------------

    // While tumbling, faces swap at TumbleHz instead of every frame — reads as rolling without
    // strobing. Deterministic per (die, time slice), cosmetic only.
    private static int TumbleFace(int i, int min, int max)
    {
        int phase = (int)(Raylib.GetTime() * TumbleHz);
        uint h = (uint)(i * 73856093) ^ (uint)(phase * 19349663);
        return min + (int)(h % (uint)Math.Max(1, max - min + 1));
    }

    // Seconds since the last stack draw, taken once per frame so every panel eases at the same rate.
    private static float FrameDelta()
    {
        double now = Raylib.GetTime();
        float dt = (float)(now - _lastDrawTime);
        _lastDrawTime = now;
        return dt;
    }

    // Forget the smoothing state of panels that have left the stack, so a beat reference can't pin
    // memory and a new panel never inherits stale easing.
    private static void PruneFx(IReadOnlyList<(PresentationBeat beat, float progress, float alpha)> stack)
    {
        if (_fx.Count <= stack.Count) return;

        var live = new HashSet<PresentationBeat>();
        foreach ((PresentationBeat beat, float _, float _) in stack) live.Add(beat);
        foreach (PresentationBeat gone in new List<PresentationBeat>(_fx.Keys))
            if (!live.Contains(gone)) _fx.Remove(gone);
    }

    /// <summary>
    /// The panel's smoothed display multiplier: the attack-overlap ghost times the depth dim, both eased
    /// toward their targets so neither steps. A long frame gap means the stack just (re)appeared, so both
    /// snap rather than easing from stale state.
    /// </summary>
    private static float PanelFxFor(PresentationBeat beat, Rectangle panel, Rectangle? avoid,
        float depthTarget, float dt)
    {
        if (!_fx.TryGetValue(beat, out PanelFx? fx)) _fx[beat] = fx = new PanelFx();

        bool snap = dt > 0.25f;
        float ease = Math.Clamp(dt * 10f, 0f, 1f);

        float dimTarget = avoid.HasValue && Raylib.CheckCollisionRecs(panel, avoid.Value) ? OverlapDim : 1f;
        fx.Dim = snap ? dimTarget : fx.Dim + (dimTarget - fx.Dim) * ease;
        // A panel seeds its depth on the frame it appears: easing up from nothing would fade it in twice.
        fx.Depth = fx.Depth < 0f || snap ? depthTarget : fx.Depth + (depthTarget - fx.Depth) * ease;

        return fx.Dim * fx.Depth;
    }

    private static Color Faded(Color c, float a) =>
        new(c.R, c.G, c.B, (byte)Math.Clamp(c.A * Math.Clamp(a, 0f, 1f), 0f, 255f));

    private static void DrawCenteredIn(string text, int x, int width, int y, int fontSize, Color color)
    {
        int w = Raylib.MeasureText(text, fontSize);
        Raylib.DrawText(text, x + (width - w) / 2, y, fontSize, color);
    }

    // Standard d6 pip layout on a 3x3 grid (col, row), 0..2.
    private static readonly Dictionary<int, (int, int)[]> PipCells = new()
    {
        [1] = new[] { (1, 1) },
        [2] = new[] { (0, 0), (2, 2) },
        [3] = new[] { (0, 0), (1, 1), (2, 2) },
        [4] = new[] { (0, 0), (2, 0), (0, 2), (2, 2) },
        [5] = new[] { (0, 0), (2, 0), (1, 1), (0, 2), (2, 2) },
        [6] = new[] { (0, 0), (2, 0), (0, 1), (2, 1), (0, 2), (2, 2) },
    };

    private static void DrawDie(int x, int y, int size, int face, Color fill, Color pip, float a)
    {
        Raylib.DrawRectangleRounded(new Rectangle(x, y, size, size), 0.22f, 6, Faded(fill, a));
        Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, size, size), 0.22f, 6, Faded(Color.Black, a));

        if (!PipCells.TryGetValue(face, out var cells)) return;

        float pad = size * 0.24f;
        float step = (size - pad * 2f) / 2f;
        float r = size * 0.085f;
        Color pipFaded = Faded(pip, a);
        foreach (var (col, row) in cells)
        {
            float px = x + pad + col * step;
            float py = y + pad + row * step;
            Raylib.DrawCircle((int)px, (int)py, r, pipFaded);
        }
    }
}
