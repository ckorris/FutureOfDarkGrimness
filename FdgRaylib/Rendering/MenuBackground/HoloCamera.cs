using System.Numerics;

namespace FdgRaylib.Rendering.MenuBackground;

/// <summary>
/// The camera over the main menu's holographic war table (#448): a look-at camera tilted down at the
/// table, fitted so the whole slab fills a screen region as tightly as it can, with a lens shift so that
/// region need not be centred on the screen (the menu panel takes the left side).
///
/// <para>Pure maths, no Raylib, so the fit is unit-testable. World axes: X along the table's long side,
/// Y away from the viewer, Z up; one unit is one inch. The background projects every 3D point through
/// <see cref="Project"/> and draws plain 2D lines, which keeps line widths exact on every GPU (wide GL
/// lines are not portable) and lets text and later overlays line up with the terrain for free.</para>
/// </summary>
internal readonly struct HoloCamera
{
    public readonly Vector3 Eye;
    public readonly Vector3 Forward;
    public readonly Vector3 Right;
    public readonly Vector3 Up;

    /// <summary>Screen point the optical axis passes through (the lens shift).</summary>
    public readonly Vector2 Principal;

    /// <summary>Screen pixels per unit of tan(angle off the axis): half the screen height over
    /// tan(fov / 2).</summary>
    public readonly float PixelsPerTan;

    private HoloCamera(Vector3 eye, Vector3 forward, Vector3 right, Vector3 up, Vector2 principal, float pixelsPerTan)
    {
        Eye = eye;
        Forward = forward;
        Right = right;
        Up = up;
        Principal = principal;
        PixelsPerTan = pixelsPerTan;
    }

    /// <summary>Depth of <paramref name="p"/> along the view axis; only points with a positive depth are
    /// in front of the camera.</summary>
    public float Depth(Vector3 p) => Vector3.Dot(p - Eye, Forward);

    public Vector2 Project(Vector3 p)
    {
        Vector3 rel = p - Eye;
        float depth = Vector3.Dot(rel, Forward);
        return new Vector2(
            Principal.X + Vector3.Dot(rel, Right) / depth * PixelsPerTan,
            Principal.Y - Vector3.Dot(rel, Up) / depth * PixelsPerTan);
    }

    /// <summary>
    /// The camera looking at <paramref name="target"/> from <paramref name="pitchDeg"/> above the table
    /// and <paramref name="yawDeg"/> round from straight-on, pulled back just far enough that every point
    /// in <paramref name="mustShow"/> lands inside the region, then shifted so their bounding box is
    /// centred in it.
    /// </summary>
    public static HoloCamera FitToRegion(Vector3 target, IReadOnlyList<Vector3> mustShow,
        float regionX, float regionY, float regionW, float regionH, float screenH,
        float pitchDeg = 47f, float yawDeg = -16f, float fovDeg = 40f)
    {
        float pitch = pitchDeg * MathF.PI / 180f;
        float yaw = yawDeg * MathF.PI / 180f;
        float pixelsPerTan = screenH * 0.5f / MathF.Tan(fovDeg * MathF.PI / 360f);
        var back = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch), MathF.Sin(pitch));

        HoloCamera At(float distance, Vector2 principal)
        {
            Vector3 eye = target + back * distance;
            Vector3 forward = Vector3.Normalize(target - eye);
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
            Vector3 up = Vector3.Cross(right, forward);
            return new HoloCamera(eye, forward, right, up, principal, pixelsPerTan);
        }

        (float minX, float minY, float maxX, float maxY)? Bounds(HoloCamera cam)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (Vector3 p in mustShow)
            {
                if (cam.Depth(p) < 1f) return null;   // behind (or grazing) the lens: too close
                Vector2 s = cam.Project(p);
                minX = MathF.Min(minX, s.X); maxX = MathF.Max(maxX, s.X);
                minY = MathF.Min(minY, s.Y); maxY = MathF.Max(maxY, s.Y);
            }
            return (minX, minY, maxX, maxY);
        }

        bool Fits(float distance) =>
            Bounds(At(distance, Vector2.Zero)) is { } b && b.maxX - b.minX <= regionW && b.maxY - b.minY <= regionH;

        // Pulling back only ever shrinks the picture, so the closest distance that fits is a bisection.
        float lo = 1f, hi = 10000f;
        for (int i = 0; i < 60; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (Fits(mid)) hi = mid; else lo = mid;
        }

        var bounds = Bounds(At(hi, Vector2.Zero))!.Value;
        var shift = new Vector2(
            regionX + regionW * 0.5f - (bounds.minX + bounds.maxX) * 0.5f,
            regionY + regionH * 0.5f - (bounds.minY + bounds.maxY) * 0.5f);
        return At(hi, shift);
    }
}
