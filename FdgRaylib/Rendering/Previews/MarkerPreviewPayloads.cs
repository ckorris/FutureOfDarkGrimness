using FDG;

namespace FdgRaylib.Rendering.Previews;

// #277: the "cursor-following marker" preview vocabulary for the two placement resolvers that put
// non-model objects on the table (objective markers, terrain footprints). Unlike the ghost-path
// family there is no base/ghost cache split: the whole preview is ONE ghost shape at the mouse
// position (committed markers/terrain land in synced table state and are drawn by the renderer on
// every client already), and a full payload is a few hundred bytes - resending it at the
// publisher's 10 Hz stays well under the movement family's accepted envelope. One "marker" slot,
// latest-wins. Floats are table inches quantized via PreviewQuantize.
//
// The terrain footprint crosses the wire as flat primitives (circles + quads, rotation baked into
// the corner points) rather than the engine's polymorphic IZone tree - the preview channel never
// deserializes polymorphic types (#186 discipline). MarkerFootprints.Flatten does the conversion
// source-side from ZoneExtensions.Primitives(), whose only leaf kinds are circles, rects, and
// rotated rects.

public static class MarkerPreviewSlots
{
    public const string Marker = "marker";
}

public sealed record MarkerPoint(float X, float Z);

public sealed record MarkerCircle(float X, float Z, float Radius);

/// <summary>A convex quad in world inches (a rect or rotated rect from the template). Always four
/// corners; presenters skip malformed counts - the channel is cosmetic.</summary>
public sealed record MarkerQuad(IReadOnlyList<MarkerPoint> Corners);

/// <summary>The objective ghost the placing player is hovering/confirming: marker number, center,
/// and the radii to draw (sent so the presenter needs no copy of the placer's UI constants).
/// <paramref name="Pending"/> = frozen at a click awaiting Confirm; <paramref name="Valid"/>
/// mirrors the placer's green/red legality outline.</summary>
public sealed record ObjectiveMarkerPreview(int MarkerNumber, float X, float Z,
    float BaseRadiusInches, float SeizureRadiusInches, bool Pending, bool Valid);

/// <summary>The terrain ghost being placed: the template's footprint flattened to world-space
/// primitives (rotation already applied), tinted by <paramref name="TerrainType"/>
/// (ETerrainType flags as an int for version-skew tolerance).</summary>
public sealed record TerrainFootprintPreview(int TerrainType,
    IReadOnlyList<MarkerCircle> Circles, IReadOnlyList<MarkerQuad> Quads,
    bool Pending, bool Valid);

/// <summary>Source-side flattener: engine zone tree -> wire primitives.</summary>
public static class MarkerFootprints
{
    public static (IReadOnlyList<MarkerCircle> Circles, IReadOnlyList<MarkerQuad> Quads) Flatten(IZone shape)
    {
        var circles = new List<MarkerCircle>();
        var quads = new List<MarkerQuad>();
        foreach (IZone prim in shape.Primitives())
        {
            switch (prim)
            {
                case CircularZone c:
                    circles.Add(new MarkerCircle(PreviewQuantize.Inches(c.Center.X),
                        PreviewQuantize.Inches(c.Center.Y), PreviewQuantize.Inches(c.Radius)));
                    break;

                case RectangularZone r:
                    quads.Add(Quad(r, angleDegrees: 0f, pivot: default));
                    break;

                case RotatedZoneWrapper w when w.Inner is RectangularZone rr:
                    quads.Add(Quad(rr, w.AngleDegrees, w.Pivot));
                    break;

                // Primitives() yields nothing else today; a future leaf kind is simply not
                // previewed until a case is added (same stance as ZoneRenderer's default arm).
            }
        }
        return (circles, quads);
    }

    private static MarkerQuad Quad(RectangularZone r, float angleDegrees, Float2 pivot)
    {
        Float2[] corners =
        {
            new(r.Left, r.Bottom),
            new(r.Right, r.Bottom),
            new(r.Right, r.Top),
            new(r.Left, r.Top),
        };
        var points = new List<MarkerPoint>(4);
        foreach (Float2 corner in corners)
        {
            Float2 world = ZoneExtensions.RotateAround(corner, pivot, angleDegrees);
            points.Add(new MarkerPoint(PreviewQuantize.Inches(world.X), PreviewQuantize.Inches(world.Y)));
        }
        return new MarkerQuad(points);
    }
}
