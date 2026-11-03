namespace FdgRaylib.Rendering.Previews;

/// <summary>Wire-float hygiene shared by every preview source (#280). All payload floats are table
/// inches quantized to 0.01" - sub-pixel at any realistic zoom, and coarse enough that the
/// publisher's serialize-and-compare dedup absorbs sub-0.01" mouse jitter.</summary>
public static class PreviewQuantize
{
    /// <summary>Below the 0.01" wire quantization step, so a ghost omitted as "equal to its
    /// committed endpoint" really is indistinguishable from it.</summary>
    public const float GhostEpsilonInches = 0.005f;

    public static float Inches(float v) => MathF.Round(v * 100f) / 100f;
}
