using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Shared wheel / R input for the placement and movement overlays (#275), replacing three
/// copy-pasted blocks. Plain Wheel and R / Shift+R rotate (15 deg per notch, unchanged semantics);
/// Ctrl+Wheel is the formation cycle. Ctrl was freed for this by moving the ruler to Alt, and the
/// renderer's Ctrl+wheel zoom yields while a group ghost is active (see
/// <see cref="IFormationWheelConsumer"/>).
/// </summary>
internal static class GroupInput
{
    public static readonly float RotationStep = MathF.PI / 12f; // 15 deg per wheel notch / key press

    /// <summary>Rotation delta (radians) and formation-cycle delta for this frame; zeros when the
    /// UI has mouse/keyboard capture (<paramref name="wantInput"/> false).</summary>
    public static (float rotationDelta, int formationDelta) Read(bool wantInput)
    {
        if (!wantInput) return (0f, 0);
        var io = ImGui.GetIO();
        bool shift = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
        // Legacy io.KeyCtrl isn't populated by every backend, so also check the keys directly.
        bool ctrl = io.KeyCtrl || ImGui.IsKeyDown(ImGuiKey.LeftCtrl) || ImGui.IsKeyDown(ImGuiKey.RightCtrl);

        float rotation = 0f;
        int cycle = 0;
        if (io.MouseWheel != 0f)
        {
            if (ctrl) cycle = io.MouseWheel > 0f ? 1 : -1;
            else rotation += io.MouseWheel > 0f ? RotationStep : -RotationStep;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.R))
            rotation += shift ? RotationStep : -RotationStep;
        return (rotation, cycle);
    }
}

/// <summary>
/// Opt-in for resolvers whose group ghost consumes Ctrl+Wheel as the formation cycle (#275).
/// The renderer's Ctrl+wheel zoom checks <see cref="GuiResolverOverlay.FormationWheelActive"/> and
/// yields while any pending resolver reports true, so cycling never zooms the table underneath.
/// </summary>
public interface IFormationWheelConsumer
{
    /// <summary>True while this resolver's group-formation ghost is live and reading Ctrl+Wheel.</summary>
    bool FormationWheelActive { get; }
}
