using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiChooseDeploymentZoneResolver
    : IStageResolver<ChooseDeploymentZoneRequest, DataBinding<RectangularZone>>, IGuiResolver
{
    private readonly object _lock = new();
    private ChooseDeploymentZoneRequest? _request;
    private TaskCompletionSource<DataBinding<RectangularZone>>? _tcs;

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<DataBinding<RectangularZone>> Resolve(ChooseDeploymentZoneRequest request)
    {
        var tcs = new TaskCompletionSource<DataBinding<RectangularZone>>();
        lock (_lock) { _tcs = tcs; _request = request; }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        ChooseDeploymentZoneRequest? request;
        TaskCompletionSource<DataBinding<RectangularZone>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##DeployZoneBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoBackground);

        int availCount = request.AvailableZones.Count;
        int takenCount = request.UnavailableZones.Count;
        float rowH   = 36f;
        float pad    = 16f;
        float headerH = 56f;
        float dw = MathF.Min(screenW * 0.45f, 560f);
        float dh = MathF.Min(headerH + pad + (availCount + takenCount) * rowH + pad * 2, screenH * 0.80f);
        float dx = (screenW - dw) * 0.5f;
        float dy = (screenH - dh) * 0.5f;

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##DeployZoneDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted("Choose Deployment Zone");
        ImGui.Spacing();
        ImGui.TextUnformatted($"Deploy within {GameWideConstants.DEPLOYMENT_DISTANCE_INCHES:F0}\" of your table edge.");
        ImGui.PopTextWrapPos();

        float btnW  = dw - pad * 2;
        float listY = pad + headerH;
        for (int i = 0; i < availCount; i++)
        {
            var zb   = request.AvailableZones[i];
            var zone = zb.GetValue();
            string label = $"Zone {i + 1}  (X {zone.Left:F0}\"–{zone.Right:F0}\", Z {zone.Bottom:F0}\"–{zone.Top:F0}\")##{i}";
            ImGui.SetCursorPos(new Vector2(pad, listY + i * rowH));
            if (ImGui.Button(label, new Vector2(btnW, rowH - 4f)))
                Complete(tcs, zb);
        }

        if (takenCount > 0)
        {
            float takenStart = listY + availCount * rowH;
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1f));
            for (int i = 0; i < takenCount; i++)
            {
                var zone  = request.UnavailableZones[i].GetValue();
                string label = $"Zone — (X {zone.Left:F0}\"–{zone.Right:F0}\", Z {zone.Bottom:F0}\"–{zone.Top:F0}\") [taken]##{availCount + i}";
                ImGui.SetCursorPos(new Vector2(pad, takenStart + i * rowH));
                ImGui.BeginDisabled(true);
                ImGui.Button(label, new Vector2(btnW, rowH - 4f));
                ImGui.EndDisabled();
            }
            ImGui.PopStyleColor();
        }

        ImGui.EndChild();
        ImGui.End();
    }

    private void Complete(TaskCompletionSource<DataBinding<RectangularZone>> tcs, DataBinding<RectangularZone> zone)
    {
        lock (_lock) { _request = null; _tcs = null; }
        tcs.SetResult(zone);
    }
}
