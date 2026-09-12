using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #398: a source lint, because the bug it guards cannot be caught any other way.
///
/// <para><c>ImGui.Text</c>, <c>TextColored</c> and <c>TextDisabled</c> take a printf FORMAT string.
/// The Combat Calculator prints percentages ("40.7%") and book-authored names, so a '%' reaching one of
/// those calls is read as a conversion specifier and ImGui prints whatever sits next on the varargs
/// stack. It did: the round-5 percentage columns rendered as "6.8??" trailed by fragments of unrelated
/// engine strings. Nothing about the VALUES was wrong, so no view-model test could see it, and the
/// drawing itself is ImGui and cannot be asserted - but the call can be forbidden where it is unsafe.
/// </para>
///
/// <para>These files print report figures and book text, so they use <c>UiText</c> (which wraps
/// <c>TextUnformatted</c>, no format pass) instead. Adding a new drawing call here that reaches for the
/// format version fails this test with the reason.</para>
/// </summary>
[TestFixture]
public class CalculatorTextSafetyTests
{
    private static readonly string[] Guarded =
    {
        "FdgRaylib/Rendering/CombatCalculatorScreen.cs",
        "FdgRaylib/Rendering/ForgeUnitDetail.cs",
        "FdgRaylib/Rendering/RuleTextFlow.cs",
        "FdgRaylib/Rendering/UiChrome.cs",
        // The screens that print BOOK- or PLAYER-authored text: a unit out of an imported book, an army
        // the player named themselves. No bundled book carries a '%' today, but a list saved as
        // "50% Mechanised" is one keystroke away and would print stack garbage in the lobby.
        "FdgRaylib/Rendering/ArmyForgeScreen.cs",
        "FdgRaylib/Rendering/ArmyBuilderScreen.cs",
        "FdgRaylib/Rendering/ArmyListOverlay.cs",
        "FdgRaylib/Rendering/LobbyScreen.cs",
    };

    // Every ImGui entry point that runs its argument through printf. TextUnformatted is the exception,
    // and is what UiText uses.
    private static readonly string[] FormatCalls =
    {
        "ImGui.Text(",
        "ImGui.TextColored(",
        "ImGui.TextDisabled(",
        "ImGui.TextWrapped(",
        "ImGui.LabelText(",
        "ImGui.BulletText(",
        "ImGui.SetTooltip(",
    };

    [Test]
    public void ThePanesThatPrintPercentagesNeverUseAFormatStringApi()
    {
        if (RepoRoot() is not { } root)
        {
            Assert.Ignore("Source tree not reachable from the test output; nothing to lint.");
            return;
        }

        var offenders = new List<string>();
        foreach (string relative in Guarded)
        {
            string path = Path.Combine(root, relative);
            Assert.That(File.Exists(path), Is.True, $"{relative} has moved - update this lint");

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;

                foreach (string call in FormatCalls.Where(call => line.Contains(call, StringComparison.Ordinal)))
                    offenders.Add($"{relative}:{i + 1} uses {call}");
            }
        }

        Assert.That(offenders, Is.Empty,
            "These take a printf format string, so a '%' in the text prints stack garbage. "
            + "Use UiText.Colored / UiText.Disabled / ImGui.TextUnformatted instead:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Walks up from the test binary to the checkout, or null when the sources are not there.</summary>
    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "FdgRaylib", "Rendering"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
