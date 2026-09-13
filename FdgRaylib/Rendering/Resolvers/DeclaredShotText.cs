namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #371: the words the shooting panel uses under Declare First, where answering the weapon request AIMS
/// a weapon and queues it instead of firing it.
///
/// <para>Every string here exists to stop the mode reading as a malfunction. A player presses the commit
/// button, the weapon vanishes from the list, and the same panel comes back with one fewer weapon - which
/// looks exactly like a dropped click unless the button says "Declare", the shots already aimed stay on
/// screen, and the exit says "Done declaring" rather than "Done shooting".</para>
///
/// <para>ImGui-free so the wording is unit-tested directly (same convention as
/// <see cref="PlacementPanelLayout"/> and <see cref="ModelRoster"/>); the resolver owns the pixels.</para>
/// </summary>
internal static class DeclaredShotText
{
    /// <summary>Heading over the declared block. Says "in order" because the queue fires in declaration
    /// order, which is what decides whose shots are lost when a target dies partway through.</summary>
    public const string DeclaredHeading = "Declared - fires in this order";

    /// <summary>First line of a declared row: "2. 3x Heavy Rifle". Numbered from 1, in firing order.</summary>
    public static string WeaponLine(int order, int copies, string weaponName) =>
        $"{order}. {copies}x {weaponName}";

    /// <summary>Second line of a declared row - the unit the shots are owed to.</summary>
    public static string TargetLine(string targetName) => $"Shooting at {targetName}";

    /// <summary>The primary commit button. "Declare" in Declare First, because the dice do not roll
    /// until the whole unit has been aimed; the plain "Fire!" would be a lie for every weapon but the
    /// last, and the player cannot tell which one that is.</summary>
    public static string CommitLabel(bool declareFirst) => declareFirst ? "Declare" : "Fire!";

    /// <summary>Tooltip on the commit button, spelling out the bargain the mode makes. Named on the
    /// button rather than buried in the lobby, since this is where the irreversible click happens.</summary>
    public static string CommitTooltip(bool declareFirst) => declareFirst
        ? "Aim this weapon at the selected target.\n" +
          "Nothing is rolled until every weapon has been aimed, and shots aimed at a unit\n" +
          "that an earlier weapon wipes out are lost."
        : "Fire this weapon at the selected target.";

    /// <summary>The exit button. Under Declare First it stops DECLARING - what is already aimed still
    /// gets rolled - so calling it "Done shooting" would promise the wrong thing.</summary>
    public static string StopLabel(bool declareFirst) => declareFirst ? "Done declaring" : "Done shooting";

    /// <summary>
    /// Title of the exit confirmation. The <c>###</c> suffix pins the ImGui popup ID while the visible
    /// half changes with the mode - a popup whose ID moved between OpenPopup and BeginPopupModal would
    /// simply never appear.
    /// </summary>
    public static string StopTitle(bool declareFirst) =>
        (declareFirst ? "Stop declaring targets?" : "End the shoot action?") + "###RangedStopConfirm";

    /// <summary>Confirmation button inside that popup.</summary>
    public static string StopConfirmLabel(bool declareFirst) =>
        declareFirst ? "Stop declaring" : "End the shoot";

    /// <summary>What the player is giving up by taking that exit. Under Declare First the declared shots
    /// are NOT given up - they still roll - and saying so is the whole reason the sentence differs.</summary>
    public static string StopWarning(bool declareFirst, int declaredCount)
    {
        if (!declareFirst) return "Ending the shoot action now gives up those shots for this turn.";

        string kept = declaredCount switch
        {
            0 => "Nothing has been aimed yet, so this unit will not shoot at all.",
            1 => "The one weapon already aimed still fires.",
            _ => $"The {declaredCount} weapons already aimed still fire.",
        };
        return $"Declaring nothing further gives up those shots for this turn. {kept}";
    }
}
