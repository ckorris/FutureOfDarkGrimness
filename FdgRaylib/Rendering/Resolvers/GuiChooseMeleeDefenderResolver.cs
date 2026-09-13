using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// The melee "which unit do I fight" picker (#237). With two or more valid defenders it behaves exactly
/// like the inherited cancellable unit picker: button list, canvas rings, click-to-select, Back. With
/// exactly ONE valid defender it renders a pre-selected confirm card instead - "Charge X?" with a
/// primary Charge button (Enter-bound) and a Back button - so the common single-target charge is one
/// click or one key. The pick still always renders: this prompt is the charge flow's only Back button
/// (#202), so it must never auto-resolve, only pre-select.
/// </summary>
public class GuiChooseMeleeDefenderResolver : GuiCancellableUnitSelectionResolver,
    IStageResolver<ChooseMeleeDefenderRequest, CancellableResult<DataBinding<UnitData>>>
{
    public Task<CancellableResult<DataBinding<UnitData>>> Resolve(ChooseMeleeDefenderRequest request)
        => base.Resolve(request);

    public override void Draw(int screenW, int screenH)
    {
        CancellableSelectionRequest<UnitData>? request;
        TaskCompletionSource<CancellableResult<DataBinding<UnitData>>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        if (request.ValidOptions.Count != 1)
        {
            base.Draw(screenW, screenH);
            return;
        }

        DrawTargetRings();

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##MeleeConfirmBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoBackground);

        var sole   = request.ValidOptions[0];
        float pad   = 16f;
        // #298: the card's button and its text steps come from the font, not from flat pixel counts.
        float lineH = ImGui.GetTextLineHeight();
        float rowH  = ResolverPanelLayout.OptionRowHeight();
        float dw = ResolverPanelLayout.W;   // dock into the right-column resolver panel
        float dh = ResolverPanelLayout.H;
        float dx = ResolverPanelLayout.X;
        float dy = ResolverPanelLayout.Y;

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##MeleeConfirmCard", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.None);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted($"Charge {sole.Name}?");
        ImGui.PopTextWrapPos();

        float noteY = pad + lineH * 1.6f;
        ImGui.SetCursorPos(new Vector2(pad, noteY));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.60f, 0.60f, 0.66f, 1f));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted("Only one enemy unit is in range.");
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();

        float btnW = dw - pad * 2;
        float btnY = noteY + lineH * 2.4f;
        ImGui.SetCursorPos(new Vector2(pad, btnY));
        if (ResolverButtons.Primary("Charge!", new Vector2(btnW, rowH)))
        {
            Complete(tcs, new Selected<DataBinding<UnitData>>(sole.Option));
            ImGui.EndChild();
            ImGui.End();
            return;
        }

        // #248: Backspace backs out of the confirm card too (Esc is reserved for the in-game menu),
        // applied once after drawing so a same-frame Back click can't double-resolve the TCS.
        ImGui.SetCursorPos(new Vector2(pad, btnY + rowH + 8f));
        bool cancelled = ResolverButtons.Deemphasized("Back  (Backspace)",
            new Vector2(btnW, ResolverPanelLayout.ActionRowHeight()));
        if (ResolverHotkeys.IsBackPressed())
            cancelled = true;
        if (cancelled)
            Complete(tcs, new Cancelled<DataBinding<UnitData>>());

        ImGui.EndChild();
        ImGui.End();

        // Mirror the base Draw: hover state only lives for the frame GetHoverLabel set it.
        _hoveredValidRef = null;
    }
}
