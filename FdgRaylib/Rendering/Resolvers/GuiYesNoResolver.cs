using System.Numerics;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiYesNoResolver : IStageResolver<YesNoRequest, bool>, IGuiResolver
{
    private readonly object _lock = new();
    private string? _question;
    private TaskCompletionSource<bool>? _tcs;

    public bool HasPendingRequest { get { lock (_lock) return _question != null; } }

    public Task<bool> Resolve(YesNoRequest request)
    {
        var tcs = new TaskCompletionSource<bool>();
        lock (_lock)
        {
            _tcs      = tcs;
            _question = request.QuestionText;
        }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        string? question;
        TaskCompletionSource<bool>? tcs;
        lock (_lock) { question = _question; tcs = _tcs; }
        if (question == null || tcs == null) return;

        // Semi-transparent backdrop
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##YesNoBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoBackground);

        float dw = ResolverPanelLayout.W;   // dock into the right-column resolver panel
        float dh = ResolverPanelLayout.H;
        float dx = ResolverPanelLayout.X;
        float dy = ResolverPanelLayout.Y;

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##YesNoDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        float pad = 16f;

        // Question text (wrapped, centered)
        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted(question);
        ImGui.PopTextWrapPos();

        // Buttons anchored to bottom of dialog. Yes is the primary affirmative (accent + Enter); No recedes.
        float btnW = dw * 0.42f;
        float btnH = ResolverPanelLayout.OptionRowHeight();   // #298: font-relative, was a flat 36px
        float gap  = dw * 0.04f;
        float firstX = (dw - btnW * 2 - gap) * 0.5f;
        float btnY = dh - pad - btnH;

        // #248: Y/N letter keys answer too, and Backspace = No (Esc is reserved for the in-game menu).
        // Results applied once after drawing so same-frame opposite inputs can't double-resolve the
        // TCS (Yes wins the tie, matching button order).
        ImGui.SetCursorPos(new Vector2(firstX, btnY));
        bool yesPressed = ResolverButtons.Primary("Yes [Y]", new Vector2(btnW, btnH))
                          || ResolverHotkeys.IsLetterPressed('Y');

        ImGui.SameLine(0, gap);
        bool noPressed  = ResolverButtons.Deemphasized("No [N] (Backspace)", new Vector2(btnW, btnH))
                          || ResolverHotkeys.IsLetterPressed('N')
                          || ResolverHotkeys.IsBackPressed();

        ImGui.EndChild();
        ImGui.End();

        if (yesPressed) Complete(tcs, true);
        else if (noPressed) Complete(tcs, false);
    }

    private void Complete(TaskCompletionSource<bool> tcs, bool answer)
    {
        lock (_lock) { _question = null; _tcs = null; }
        tcs.SetResult(answer);
    }
}
