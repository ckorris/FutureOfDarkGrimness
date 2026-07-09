using System;
using System.Numerics;
using FDG;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// An ad-hoc ruler: hold Ctrl and drag on the table to measure inches between two points, snapping each
/// end to the nearest model base. The ruler shows only while Ctrl or the left button is still held --
/// releasing both makes it vanish. (Ctrl rather than Alt because most Linux window managers grab
/// Alt+drag to move the window, so the app never sees the gesture.)
///
/// <para><b>Resolver coexistence (Model A).</b> Measuring is a modifier gesture, not a mode, so it shares
/// no input with resolvers: a plain click still drives whatever resolver is active. The one precedence
/// rule is that while Ctrl is held over the table this overlay sets <c>io.WantCaptureMouse = true</c>, and
/// every table-click consumer (the hit tester and the canvas resolvers) already skips when that flag is
/// set -- so a Ctrl-drag can never double as a place/move/select. Because the ruler holds no game state
/// and is tied to no request, a resolver appearing (or the turn changing) mid-measure can't corrupt it,
/// and it can't corrupt the resolver.</para>
///
/// Drawn on the background draw list, so it sits over the table but under ImGui windows and never grabs
/// the mouse itself.
/// </summary>
public class MeasurementOverlay
{
    private ITableState? _tableState;
    private float _scale;
    private int   _originX, _originY;
    private float _tableH;

    private bool _measuring;
    private bool _hasRuler;
    private MeasurePoint _anchor;
    private MeasurePoint _cursor;

    // Snap a ruler end to a model whose centre is within (base radius + this) of the cursor, in inches.
    private const float SnapMarginInches = 0.35f;

    private static readonly uint LineColor  = U32(0.60f, 0.90f, 1.00f, 0.95f); // cyan
    private static readonly uint DotColor   = U32(0.85f, 0.96f, 1.00f, 1.00f);
    private static readonly uint TextColor  = U32(0.90f, 0.97f, 1.00f, 1.00f);
    private static readonly uint Shadow     = U32(0f, 0f, 0f, 0.75f);

    public void Attach(ITableState tableState) => _tableState = tableState;

    public void Reset()
    {
        _measuring = false;
        _hasRuler  = false;
    }

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale   = scale;
        _originX = originX;
        _originY = originY;
        _tableH  = tableH;
    }

    /// <summary>
    /// Must run BEFORE the hit tester and resolvers each frame, so the WantCaptureMouse override lands
    /// before they read it.
    /// </summary>
    public void Draw(int screenW, int screenH)
    {
        if (_tableState == null) return;

        var io = ImGui.GetIO();
        bool overPanel = io.WantCaptureMouse;          // an ImGui window owns the mouse (measured before our override)
        // Robust modifier read: the legacy io.KeyCtrl isn't populated by every backend, so also check the keys.
        bool ctrl     = io.KeyCtrl || ImGui.IsKeyDown(ImGuiKey.LeftCtrl) || ImGui.IsKeyDown(ImGuiKey.RightCtrl);
        bool leftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);

        // Begin a measurement on Ctrl+click over the table.
        if (ctrl && !overPanel && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _anchor    = Snap(io.MousePos);
            _cursor    = _anchor;
            _measuring = true;
            _hasRuler  = true;
        }
        // Track the moving end while dragging (button held) with Ctrl.
        else if (_measuring && leftDown && !overPanel)
        {
            _cursor = Snap(io.MousePos);
        }

        // The ruler lives only while a button is still held; the instant BOTH Ctrl and the left button
        // are released, it disappears (no lingering / no Esc-to-clear).
        if (!ctrl && !leftDown)
        {
            _measuring = false;
            _hasRuler  = false;
        }

        // Suppress table clicks while the Ctrl gesture is active over the table.
        if (ctrl && !overPanel)
            io.WantCaptureMouse = true;

        if (_hasRuler)
            DrawRuler();
    }

    private void DrawRuler()
    {
        var dl = ImGui.GetBackgroundDrawList();
        Vector2 a = ToPixel(_anchor);
        Vector2 b = ToPixel(_cursor);

        // Main line + a small perpendicular tick at each end.
        dl.AddLine(a, b, LineColor, 2f);
        Vector2 dir = b - a;
        float len = dir.Length();
        if (len > 0.001f)
        {
            Vector2 perp = new Vector2(-dir.Y, dir.X) / len * 6f;
            dl.AddLine(a - perp, a + perp, LineColor, 2f);
            dl.AddLine(b - perp, b + perp, LineColor, 2f);
        }
        dl.AddCircleFilled(a, 3.5f, DotColor);
        dl.AddCircleFilled(b, 3.5f, DotColor);

        // Distances: centre-to-centre always; base-to-base too when both ends snapped to a model.
        float dx = _anchor.XIn - _cursor.XIn;
        float dz = _anchor.ZIn - _cursor.ZIn;
        float centerIn = MathF.Sqrt(dx * dx + dz * dz);

        string label;
        if (_anchor.Snapped && _cursor.Snapped)
        {
            float edge = MathF.Max(0f, centerIn - _anchor.RadiusIn - _cursor.RadiusIn);
            label = $"{edge:0.0}\" edge / {centerIn:0.0}\" ctr";
        }
        else
        {
            label = $"{centerIn:0.0}\"";
        }

        Vector2 mid  = (a + b) * 0.5f;
        Vector2 size = ImGui.CalcTextSize(label);
        var at = new Vector2(mid.X - size.X * 0.5f, mid.Y - size.Y - 8f);
        dl.AddText(at + new Vector2(1, 1), Shadow, label);
        dl.AddText(at, TextColor, label);
    }

    // Nearest living, placed model whose base (plus a small margin) contains the cursor; else the raw point.
    private MeasurePoint Snap(Vector2 mouse)
    {
        float xIn = (mouse.X - _originX) / _scale;
        float zIn = _tableH - (mouse.Y - _originY) / _scale;

        IModel? best = null;
        float bestD = float.MaxValue;
        foreach (IUnit unit in _tableState!.Units.Objects)
        {
            // Off-table units (Ambush reserve, embarked, flown-off Aircraft) park at the origin.
            if (!unit.GetIsOnBattlefield()) continue;

            foreach (IModel m in unit.Models)
            {
                if (!m.GetIsAlive()) continue;
                Position p = m.Position;
                if (p.x == 0f && p.z == 0f) continue;

                float ex = xIn - p.x, ez = zIn - p.z;
                float d = MathF.Sqrt(ex * ex + ez * ez);
                if (d <= m.BaseRadiusInches + SnapMarginInches && d < bestD)
                {
                    bestD = d;
                    best  = m;
                }
            }
        }

        if (best != null)
        {
            Position c = best.Position;
            return new MeasurePoint { XIn = c.x, ZIn = c.z, RadiusIn = best.BaseRadiusInches, Snapped = true };
        }
        return new MeasurePoint { XIn = xIn, ZIn = zIn, RadiusIn = 0f, Snapped = false };
    }

    private Vector2 ToPixel(MeasurePoint p) =>
        new(_originX + p.XIn * _scale, _originY + (_tableH - p.ZIn) * _scale);

    private struct MeasurePoint
    {
        public float XIn, ZIn, RadiusIn;
        public bool  Snapped;
    }

    private static uint U32(float r, float g, float b, float a) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));
}
