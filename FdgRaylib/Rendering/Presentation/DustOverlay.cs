using System;
using System.Collections.Generic;
using System.Numerics;
using FDG;
using FDG.Presentation.Beats;
using Raylib_cs;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// #399 - draws the active <see cref="UnitArrivedBeat"/>: a kicked-up dust cloud over every model of a
/// unit that just came on from reserve. An ambush is the one thing in the game that appears out of
/// nothing, and without a visual it simply WAS not there and then WAS - the banner said so, but the
/// table did not.
///
/// <para>Two layers per model, both from <see cref="DustCloudPlan"/>: a ground ring that snaps out and
/// dies in the first fraction (the kick), and a body of puffs that bloom, drift outward and thin
/// (the cloud). The cloud is drawn OVER the models - they are already placed and already drawn by the
/// time the beat plays, so the dust settling is what reveals them.</para>
///
/// <para>Inch-to-pixel matches <see cref="SpellOverlay"/>, and like it the sub-timing is the front
/// end's own business inside the envelope the engine paced.</para>
/// </summary>
public static class DustOverlay
{
    // Warm pale grey - dirt lifted off a battlefield, not smoke. Kept off the team colours on purpose:
    // the cloud says "something arrived here", and whose it is, is the banner's job.
    private static readonly Color DustCore = new(206, 196, 176, 255);
    private static readonly Color DustEdge = new(150, 140, 122, 255);
    private static readonly Color RingCol  = new(226, 216, 196, 255);

    // The alpha a fully-bloomed puff reaches. Well under opaque: the point is to veil the model for a
    // moment, not to hide the board.
    private const float PuffPeakAlpha = 0.62f;
    private const float RingPeakAlpha = 0.75f;

    /// <summary>
    /// Draws the cloud for <paramref name="beat"/> at <paramref name="progress"/> (0..1). Screen
    /// mapping matches the model renderer: <paramref name="originX"/>/<paramref name="originY"/> is the
    /// table's top-left corner in pixels, <paramref name="tableH"/> its height in inches (z is flipped).
    /// </summary>
    public static void Draw(UnitArrivedBeat beat, float progress, float scale, int originX, int originY,
        float tableH)
    {
        if (beat.Models.Count == 0) return;

        // "One model radius" of world space, the same stand-in SpellOverlay uses - the beat carries
        // landing spots, not base sizes, and a cloud does not need to trace a footprint.
        const float modelRadiusInches = 0.55f;

        for (int i = 0; i < beat.Models.Count; i++)
        {
            float t = DustCloudPlan.Staggered(progress, i, beat.Models.Count);
            if (t <= 0f) continue;

            Vector2 at = ToPixel(beat.Models[i].Position, scale, originX, originY, tableH);

            float ringAlpha = DustCloudPlan.RingAlpha(t);
            if (ringAlpha > 0f)
            {
                float r = DustCloudPlan.RingRadiusInches(t, modelRadiusInches) * scale;
                // Thickness tracks the scale so the ring does not vanish when the table is zoomed out.
                float thickness = MathF.Max(1.5f, scale * 0.08f);
                Raylib.DrawRing(at, MathF.Max(0f, r - thickness), r, 0f, 360f, 32,
                    Fade(RingCol, ringAlpha * RingPeakAlpha));
            }

            IReadOnlyList<DustCloudPlan.Puff> puffs =
                DustCloudPlan.Puffs(t, modelRadiusInches, seed: i);
            foreach (DustCloudPlan.Puff puff in puffs)
            {
                var c = new Vector2(at.X + puff.OffsetXInches * scale,
                                    at.Y - puff.OffsetZInches * scale);   // table z is screen-up
                float r = MathF.Max(1f, puff.RadiusInches * scale);

                // Darker rim under a paler core: two flat circles read as volume without a gradient.
                Raylib.DrawCircleV(c, r, Fade(DustEdge, puff.Alpha * PuffPeakAlpha * 0.7f));
                Raylib.DrawCircleV(c, r * 0.62f, Fade(DustCore, puff.Alpha * PuffPeakAlpha));
            }
        }
    }

    private static Color Fade(Color c, float alpha) =>
        new(c.R, c.G, c.B, (byte)Math.Clamp(alpha * 255f, 0f, 255f));

    private static Vector2 ToPixel(Position pos, float scale, int originX, int originY, float tableH) =>
        new(originX + pos.x * scale, originY + (tableH - pos.z) * scale);
}
