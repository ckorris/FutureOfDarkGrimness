using System;
using System.Collections.Generic;

namespace FdgRaylib.Rendering.TacticalOverlay;

/// <summary>
/// The fidelity sampler (spec section 6): a debug toggle that walks a coarse grid and, per channel,
/// compares the field's CLAIMED state (read from the mask) against the RULES TRUTH (a live rules call
/// for a hypothetical model at that point), tallying mismatches. Edge-texel noise is expected;
/// systematic disagreement (a whole shadow wrong, missing base-radius inflation) is a bug. This is how
/// the field's geometry gets verified against the real engine functions -- it is the one tool allowed to
/// read the field, precisely because its job is to catch the field lying.
///
/// Generic: the controller supplies channels (claim + truth predicates); the sampler only iterates,
/// compares, and tallies. So it works unchanged as band/LoS/cover channels come online in P3/P4.
/// </summary>
internal sealed class FidelitySampler
{
    public bool Enabled;

    public readonly record struct Channel(string Name, Func<float, float, bool> Claim, Func<float, float, bool> Truth);
    public readonly record struct Mismatch(float X, float Z, int ChannelIndex);

    public sealed class Report
    {
        public int SampleCount;
        public readonly List<(string name, int mismatches)> PerChannel = new();
        public readonly List<Mismatch> Points = new();

        public float MismatchPercent(int totalChannels)
        {
            if (SampleCount == 0 || totalChannels == 0) return 0f;
            int total = 0;
            foreach (var (_, m) in PerChannel) total += m;
            return 100f * total / (SampleCount * totalChannels);
        }
    }

    /// <summary>
    /// Samples a grid of <paramref name="spacingInches"/> over [0,tableW]x[0,tableH] and reports, per
    /// channel, the mismatched points. Cheap: at ~2" spacing that's ~36x24 = 864 samples x a few rules
    /// calls -- a debug-only cost, and only while the toggle is on.
    /// </summary>
    public Report Run(float tableW, float tableH, float spacingInches, IReadOnlyList<Channel> channels)
    {
        var report = new Report();
        var counts = new int[channels.Count];

        int n = 0;
        for (float z = spacingInches * 0.5f; z < tableH; z += spacingInches)
        {
            for (float x = spacingInches * 0.5f; x < tableW; x += spacingInches)
            {
                n++;
                for (int c = 0; c < channels.Count; c++)
                {
                    if (channels[c].Claim(x, z) != channels[c].Truth(x, z))
                    {
                        counts[c]++;
                        report.Points.Add(new Mismatch(x, z, c));
                    }
                }
            }
        }

        report.SampleCount = n;
        for (int c = 0; c < channels.Count; c++)
            report.PerChannel.Add((channels[c].Name, counts[c]));
        return report;
    }
}
