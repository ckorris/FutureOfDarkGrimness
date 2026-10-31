namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Whether placement/movement resolvers act on the whole unit at once (Group) or one model
/// at a time (Single).
/// </summary>
public enum FormationMode { Group, Single }

/// <summary>
/// Shared, session-lived preference for Group vs Single placement. A single instance is created
/// in <c>ResolverRegistryFactory.BuildGui</c> and handed to both the deployment and movement
/// resolvers, so flipping the mode in one stage carries to the other. Defaults to Group; the last
/// choice is remembered for the duration of the game (not persisted across app restarts).
/// </summary>
public sealed class FormationModeState
{
    public FormationMode Mode { get; set; } = FormationMode.Group;

    public bool IsGroup => Mode == FormationMode.Group;

    public void Toggle() => Mode = IsGroup ? FormationMode.Single : FormationMode.Group;
}
