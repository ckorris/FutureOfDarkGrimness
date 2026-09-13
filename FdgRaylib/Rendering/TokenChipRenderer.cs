using System.Numerics;
using FDG;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// App-side rendering of the engine's token display data (#151 Step 2). Turns a unit's / model's tokens
/// into little colored shapes on the table canvas, plus the hover-tooltip lines. The engine
/// (<see cref="TokenDisplay"/>) decides what a token MEANS (valence, prominence, name, hover text); this
/// class decides what it LOOKS LIKE — the palette, the deterministic hash → color/shape, the layout.
///
/// Color encodes <b>valence</b> (good/bad/neutral for the bearer): vivid cool = positive, vivid warm =
/// negative, muted hue = neutral. Shape distinguishes one token from another. Both are derived from a
/// STABLE hash of the token's display id (so "Shielded" looks the same every run) unless the rule authored
/// an override. Hashing uses FNV-1a, NOT <c>string.GetHashCode</c> (which is randomized per process).
/// </summary>
public static class TokenChipRenderer
{
    private const uint FnvOffset = 2166136261u;
    private const uint FnvPrime = 16777619u;
    private const uint ShapeSeed = 0x9E3779B1u; // decorrelate shape choice from color choice

    // Curated palettes, indexed by a hash of the display id. Positive = vivid cool, Negative = vivid warm,
    // Neutral = muted hue (deliberately NOT gray — gray-on-gray is the worst to tell apart, and Neutral is
    // the default valence). See #151 decisions.
    private static readonly (byte R, byte G, byte B)[] PositiveBand =
        { (70, 190, 90), (80, 140, 240), (170, 100, 220), (60, 190, 190) };

    private static readonly (byte R, byte G, byte B)[] NegativeBand =
        { (224, 64, 64), (240, 140, 40), (232, 200, 56), (236, 110, 180) };

    private static readonly (byte R, byte G, byte B)[] NeutralBand =
        { (150, 150, 162), (150, 162, 140), (166, 150, 135), (150, 145, 168), (138, 160, 164) };

    private static readonly ETokenShape[] Shapes =
    {
        ETokenShape.Circle, ETokenShape.Square, ETokenShape.Triangle, ETokenShape.Diamond,
        ETokenShape.Pentagon, ETokenShape.Hexagon, ETokenShape.Star, ETokenShape.Cross,
    };

    // --- Resolution ------------------------------------------------------------------------------------

    /// <summary>
    /// The visible tokens on a container, resolved to display info and sorted for a stable on-canvas order
    /// (first-class first, then negative → neutral → positive, then by id hash so position doesn't jump).
    /// Invisible tokens are dropped unless <paramref name="showInvisible"/> (the dev toggle).
    /// <para><paramref name="bearer"/> is the unit the container belongs to (the unit itself, or the owner
    /// of the model for a model-scoped container). The engine needs it to hide a bookkeeping token no rule
    /// on that unit reads — see <c>TokenDefinition.VisibleOnlyWhenRead</c>. Pass it whenever it is known;
    /// omitting it just leaves such tokens visible.</para>
    /// </summary>
    public static List<TokenDisplayInfo> ResolveVisible(ITokenContainer tokens, IRuleResolver? rules,
        bool isModelScoped, bool showInvisible, IUnit? bearer = null)
    {
        var list = new List<TokenDisplayInfo>();
        foreach (Token token in tokens.GetAllTokens())
        {
            TokenDisplayInfo info = TokenDisplay.Resolve(token, rules, isModelScoped, bearer);
            if (info.Prominence == ETokenProminence.Invisible && !showInvisible) continue;
            list.Add(info);
        }

        list.Sort((a, b) =>
        {
            int byTier = SortTier(a).CompareTo(SortTier(b));
            if (byTier != 0) return byTier;
            int byValence = ValenceRank(a.Valence).CompareTo(ValenceRank(b.Valence));
            if (byValence != 0) return byValence;
            return Hash(a.DisplayId, FnvOffset).CompareTo(Hash(b.DisplayId, FnvOffset));
        });

        return list;
    }

    private static int SortTier(TokenDisplayInfo i) => i.Prominence == ETokenProminence.FirstClass ? 0 : 1;
    private static int ValenceRank(EValence v) => v switch
    {
        EValence.Negative => 0,
        EValence.Neutral => 1,
        _ => 2,
    };

    public static uint ColorFor(TokenDisplayInfo info)
    {
        (byte r, byte g, byte b) = info.ColorOverride is { } oc
            ? Rgb(oc)
            : info.Valence switch
            {
                EValence.Positive => PositiveBand[Hash(info.DisplayId, FnvOffset) % (uint)PositiveBand.Length],
                EValence.Negative => NegativeBand[Hash(info.DisplayId, FnvOffset) % (uint)NegativeBand.Length],
                _ => NeutralBand[Hash(info.DisplayId, FnvOffset) % (uint)NeutralBand.Length],
            };
        return U32(r, g, b);
    }

    public static ETokenShape ShapeFor(TokenDisplayInfo info) =>
        info.ShapeOverride ?? Shapes[Hash(info.DisplayId, ShapeSeed) % (uint)Shapes.Length];

    // --- Canvas drawing --------------------------------------------------------------------------------

    private const float NormalRadius = 9f;
    private const float FirstClassRadius = 14f;
    private const float Gap = 4f;

    /// <summary>
    /// Draws a single horizontal, centered row of chips. <paramref name="centerX"/> is the row's centre;
    /// chips hang from <paramref name="topY"/> downward. Returns the row height (0 if no chips), so the
    /// caller can stack the unit name above it.
    /// </summary>
    public static float DrawChipRow(ImDrawListPtr dl, IReadOnlyList<TokenDisplayInfo> tokens,
        float centerX, float topY)
    {
        if (tokens.Count == 0) return 0f;

        float maxR = tokens.Any(t => t.Prominence == ETokenProminence.FirstClass)
            ? FirstClassRadius : NormalRadius;

        float totalW = 0f;
        foreach (TokenDisplayInfo t in tokens) totalW += 2f * RadiusOf(t) + Gap;
        totalW -= Gap;

        float x = centerX - totalW * 0.5f;
        float centerY = topY + maxR;

        foreach (TokenDisplayInfo info in tokens)
        {
            float r = RadiusOf(info);
            var center = new Vector2(x + r, centerY);
            DrawShape(dl, ShapeFor(info), center, r, ColorFor(info), OutlineColor);

            string? overlay = OverlayText(info);
            if (overlay != null) DrawCenteredText(dl, overlay, center);

            x += 2f * r + Gap;
        }

        return 2f * maxR;
    }

    /// <summary>Width a chip row will occupy (0 if no chips) — the row is centred on the caller's x.</summary>
    public static float RowWidth(IReadOnlyList<TokenDisplayInfo> tokens)
    {
        if (tokens.Count == 0) return 0f;

        float totalW = 0f;
        foreach (TokenDisplayInfo t in tokens) totalW += 2f * RadiusOf(t) + Gap;
        return totalW - Gap;
    }

    /// <summary>Height a chip row will occupy (0 if no chips) — lets the caller stack the unit name above it.</summary>
    public static float RowHeight(IReadOnlyList<TokenDisplayInfo> tokens) =>
        tokens.Count == 0
            ? 0f
            : 2f * (tokens.Any(t => t.Prominence == ETokenProminence.FirstClass) ? FirstClassRadius : NormalRadius);

    private static float RadiusOf(TokenDisplayInfo i) =>
        i.Prominence == ETokenProminence.FirstClass ? FirstClassRadius : NormalRadius;

    // First-class tokens carry a glyph (placeholder for bespoke art, #053 pattern): Spell tokens show the
    // count, others their initial. Stacked normal tokens show their count.
    private static string? OverlayText(TokenDisplayInfo i)
    {
        if (i.Prominence == ETokenProminence.FirstClass)
            return i.DisplayId == TokenType.SPELL_TOKENS_ID
                ? i.Count.ToString()
                : i.Name.Length > 0 ? i.Name[..1] : "?";

        return i.Count > 1 ? i.Count.ToString() : null;
    }

    private static readonly uint OutlineColor = U32(0, 0, 0, 200);
    private static readonly uint TextColor = U32(255, 255, 255, 255);
    private static readonly uint TextShadow = U32(0, 0, 0, 220);

    private static void DrawCenteredText(ImDrawListPtr dl, string s, Vector2 center)
    {
        Vector2 size = ImGui.CalcTextSize(s);
        var pos = new Vector2(center.X - size.X * 0.5f, center.Y - size.Y * 0.5f);
        dl.AddText(pos + new Vector2(1, 1), TextShadow, s);
        dl.AddText(pos, TextColor, s);
    }

    private static void DrawShape(ImDrawListPtr dl, ETokenShape shape, Vector2 c, float r, uint fill, uint outline)
    {
        switch (shape)
        {
            case ETokenShape.Circle:
                dl.AddCircleFilled(c, r, fill);
                dl.AddCircle(c, r, outline);
                break;

            case ETokenShape.Square:
                dl.AddRectFilled(c - new Vector2(r, r), c + new Vector2(r, r), fill);
                dl.AddRect(c - new Vector2(r, r), c + new Vector2(r, r), outline);
                break;

            case ETokenShape.Triangle:
                Tri(dl, c, r, fill, outline, pointUp: true);
                break;

            case ETokenShape.Diamond:
                Diamond(dl, c, r, fill, outline);
                break;

            case ETokenShape.Pentagon:
                dl.AddNgonFilled(c, r, fill, 5);
                dl.AddNgon(c, r, outline, 5);
                break;

            case ETokenShape.Hexagon:
                dl.AddNgonFilled(c, r, fill, 6);
                dl.AddNgon(c, r, outline, 6);
                break;

            case ETokenShape.Star:
                // Hexagram (two overlapping triangles) — both convex, so it renders reliably.
                Tri(dl, c, r, fill, outline, pointUp: true);
                Tri(dl, c, r, fill, outline, pointUp: false);
                break;

            case ETokenShape.Cross:
                float t = r * 0.42f;
                dl.AddRectFilled(new Vector2(c.X - t, c.Y - r), new Vector2(c.X + t, c.Y + r), fill);
                dl.AddRectFilled(new Vector2(c.X - r, c.Y - t), new Vector2(c.X + r, c.Y + t), fill);
                break;
        }
    }

    private static void Tri(ImDrawListPtr dl, Vector2 c, float r, uint fill, uint outline, bool pointUp)
    {
        float s = pointUp ? -1f : 1f;
        var p1 = new Vector2(c.X, c.Y + s * r);
        var p2 = new Vector2(c.X - r * 0.866f, c.Y - s * r * 0.5f);
        var p3 = new Vector2(c.X + r * 0.866f, c.Y - s * r * 0.5f);
        dl.AddTriangleFilled(p1, p2, p3, fill);
        dl.AddTriangle(p1, p2, p3, outline);
    }

    private static void Diamond(ImDrawListPtr dl, Vector2 c, float r, uint fill, uint outline)
    {
        var top = new Vector2(c.X, c.Y - r);
        var right = new Vector2(c.X + r, c.Y);
        var bottom = new Vector2(c.X, c.Y + r);
        var left = new Vector2(c.X - r, c.Y);
        dl.AddQuadFilled(top, right, bottom, left, fill);
        dl.AddQuad(top, right, bottom, left, outline);
    }

    // --- Hashing & color helpers -----------------------------------------------------------------------

    private static uint Hash(string s, uint seed)
    {
        uint h = seed;
        foreach (char ch in s)
        {
            h ^= ch;
            h *= FnvPrime;
        }
        return h;
    }

    private static uint U32(byte r, byte g, byte b, byte a = 255) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(r / 255f, g / 255f, b / 255f, a / 255f));

    private static (byte, byte, byte) Rgb(ETokenColor c) => c switch
    {
        ETokenColor.Red => (224, 64, 64),
        ETokenColor.Orange => (240, 140, 40),
        ETokenColor.Yellow => (232, 200, 56),
        ETokenColor.Pink => (236, 110, 180),
        ETokenColor.Green => (70, 190, 90),
        ETokenColor.Blue => (80, 140, 240),
        ETokenColor.Purple => (170, 100, 220),
        ETokenColor.Teal => (60, 190, 190),
        _ => (150, 150, 150),
    };
}
