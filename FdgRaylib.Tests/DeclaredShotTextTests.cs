using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #371: the shooting panel's Declare First wording. Pinned because every string here exists to stop a
// working rule reading as a bug - the panel losing a weapon and coming straight back is exactly what a
// dropped click looks like, and only the words tell the two apart.
[TestFixture]
public class DeclaredShotTextTests
{
    [Test]
    public void WeaponLine_NumbersFromOne_AndLeadsWithTheCopyCount()
    {
        Assert.That(DeclaredShotText.WeaponLine(1, 3, "Heavy Rifle"), Is.EqualTo("1. 3x Heavy Rifle"));
        Assert.That(DeclaredShotText.WeaponLine(2, 1, "Pistol"), Is.EqualTo("2. 1x Pistol"));
    }

    [Test]
    public void TargetLine_NamesTheUnitTheShotsAreOwedTo()
    {
        Assert.That(DeclaredShotText.TargetLine("Ork Boyz"), Is.EqualTo("Shooting at Ork Boyz"));
    }

    // The single most important string of the set: "Fire!" on a button that does not fire is what makes
    // the mode feel broken.
    [Test]
    public void CommitLabel_SaysDeclareOnlyInDeclareFirst()
    {
        Assert.That(DeclaredShotText.CommitLabel(declareFirst: true), Is.EqualTo("Declare"));
        Assert.That(DeclaredShotText.CommitLabel(declareFirst: false), Is.EqualTo("Fire!"));
    }

    [Test]
    public void StopLabel_SaysDeclaringRatherThanShooting_InDeclareFirst()
    {
        Assert.That(DeclaredShotText.StopLabel(declareFirst: true), Is.EqualTo("Done declaring"));
        Assert.That(DeclaredShotText.StopLabel(declareFirst: false), Is.EqualTo("Done shooting"));
    }

    // The ### half is the popup's ImGui ID. If it moved with the mode, OpenPopup and BeginPopupModal
    // would disagree and the confirmation would simply never appear.
    [Test]
    public void StopTitle_ChangesItsLabelButNeverItsImGuiId()
    {
        string declaring = DeclaredShotText.StopTitle(declareFirst: true);
        string shooting  = DeclaredShotText.StopTitle(declareFirst: false);

        Assert.That(declaring, Does.StartWith("Stop declaring targets?"));
        Assert.That(shooting, Does.StartWith("End the shoot action?"));
        Assert.That(declaring.Split("###")[1], Is.EqualTo(shooting.Split("###")[1]),
            "the ID half must be identical or the modal never opens");
    }

    // Under Declare First the declared shots are NOT given up - they still roll. Promising otherwise
    // would talk a player out of an exit that costs them nothing.
    [Test]
    public void StopWarning_InDeclareFirst_SaysTheDeclaredShotsStillFire()
    {
        Assert.That(DeclaredShotText.StopWarning(declareFirst: true, declaredCount: 1),
            Does.Contain("The one weapon already aimed still fires."));
        Assert.That(DeclaredShotText.StopWarning(declareFirst: true, declaredCount: 3),
            Does.Contain("The 3 weapons already aimed still fire."));
    }

    [Test]
    public void StopWarning_InDeclareFirstWithNothingAimed_SaysTheUnitWillNotShoot()
    {
        Assert.That(DeclaredShotText.StopWarning(declareFirst: true, declaredCount: 0),
            Does.Contain("will not shoot at all"));
    }

    [Test]
    public void StopWarning_InOneAtATime_IsUnchanged()
    {
        Assert.That(DeclaredShotText.StopWarning(declareFirst: false, declaredCount: 0),
            Is.EqualTo("Ending the shoot action now gives up those shots for this turn."));
    }

    // CLAUDE.md: the ImGui font atlas bakes Basic Latin + Latin-1 only, so anything above U+00FF renders
    // as '?' in game. Cheap to state once for a class that is nothing but user-facing strings.
    [Test]
    public void EveryString_IsAscii()
    {
        string[] all =
        {
            DeclaredShotText.DeclaredHeading,
            DeclaredShotText.WeaponLine(1, 2, "Rifle"),
            DeclaredShotText.TargetLine("Boyz"),
            DeclaredShotText.CommitLabel(true), DeclaredShotText.CommitLabel(false),
            DeclaredShotText.CommitTooltip(true), DeclaredShotText.CommitTooltip(false),
            DeclaredShotText.StopLabel(true), DeclaredShotText.StopLabel(false),
            DeclaredShotText.StopTitle(true), DeclaredShotText.StopTitle(false),
            DeclaredShotText.StopConfirmLabel(true), DeclaredShotText.StopConfirmLabel(false),
            DeclaredShotText.StopWarning(true, 0), DeclaredShotText.StopWarning(true, 1),
            DeclaredShotText.StopWarning(true, 4), DeclaredShotText.StopWarning(false, 0),
        };

        foreach (string text in all)
        {
            Assert.That(text.All(c => c <= ''), Is.True, $"non-ASCII in: {text}");
        }
    }
}
