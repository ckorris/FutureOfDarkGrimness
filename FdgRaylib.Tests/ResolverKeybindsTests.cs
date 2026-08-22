using FdgRaylib.Rendering.Resolvers;
using ImGuiNET;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #295 — resolver-wide bindings live in one table so a rebind is a one-line edit and every advertised
/// label follows. These pin the bindings themselves (Space joined Enter on Confirm when single-model
/// switching moved onto a direct click) and, more importantly, that the UI text is DERIVED from the key
/// list rather than hand-written beside it -- the failure mode this table exists to prevent is a button
/// still saying "(Enter)" after the keys changed.
///
/// <para>Key-state reads (<c>IsPressed</c>) need a live ImGui frame, so they belong to hand-verification;
/// what is testable here is the table's content and the text it generates.</para>
/// </summary>
[TestFixture]
public class ResolverKeybindsTests
{
    [Test]
    public void Confirm_BindsEnterKeypadEnterAndSpace()
    {
        Assert.That(ResolverKeybinds.Confirm.Keys,
            Is.EquivalentTo(new[] { ImGuiKey.Enter, ImGuiKey.KeypadEnter, ImGuiKey.Space }));
    }

    [Test]
    public void Confirm_AdvertisesBothKeysToThePlayer()
    {
        Assert.That(ResolverKeybinds.Confirm.Hint, Is.EqualTo("Enter/Space"));
        Assert.That(ResolverKeybinds.Confirm.Parenthetical, Is.EqualTo("(Enter/Space)"));
    }

    [Test]
    public void Back_BindsBackspaceOnly()
    {
        // Esc is deliberately absent: it opens the in-game menu, so reaching Options mid-plan must not
        // also discard the plan (#248 playtest feedback).
        Assert.That(ResolverKeybinds.Back.Keys, Is.EquivalentTo(new[] { ImGuiKey.Backspace }));
        Assert.That(ResolverKeybinds.Back.Keys, Has.No.Member(ImGuiKey.Escape));
        Assert.That(ResolverKeybinds.Back.Parenthetical, Is.EqualTo("(Backspace)"));
    }

    [Test]
    public void ConfirmAndBack_DoNotShareAKey()
    {
        // Space is only free for Confirm because model-switching moved onto a click (#295); a future
        // binding that quietly reuses a key would make one of the two panels' hints a lie.
        Assert.That(ResolverKeybinds.Confirm.Keys.Intersect(ResolverKeybinds.Back.Keys), Is.Empty);
    }

    [Test]
    public void HintText_IsAsciiOnly()
    {
        // The ImGui font atlas bakes Basic Latin + Latin-1 only; anything above U+00FF renders as '?'.
        foreach (string text in new[]
                 {
                     ResolverKeybinds.Confirm.Hint, ResolverKeybinds.Confirm.Parenthetical,
                     ResolverKeybinds.Back.Hint, ResolverKeybinds.Back.Parenthetical,
                 })
            Assert.That(text, Is.All.LessThan((char)128), $"non-ASCII in \"{text}\"");
    }
}
