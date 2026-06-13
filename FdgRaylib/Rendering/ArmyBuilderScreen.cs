using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using ImGuiNET;
using TinyDialogsNet;

namespace FdgRaylib.Rendering;

public class ArmyBuilderScreen : IAppScreen
{
    public Action? OnBack;

    private readonly ArmyListFile _army = new();

    private static readonly FileFilter ArmyFilter = new(
        $"Army List (*{ArmyListFile.EXTENSION_WITH_PERIOD})",
        new[] { $"*{ArmyListFile.EXTENSION_WITH_PERIOD}" });

    // The picker is derived from the engine catalog plus this army's embedded rules (#059 workstream 6),
    // refreshed each frame in Draw() so loading an army with embedded rules surfaces them immediately.
    private string[] _allNames = Array.Empty<string>();
    private HashSet<string> _numericNames = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (int sel, int val)> _addRuleState = new();

    private void RefreshRuleNames()
    {
        IReadOnlyList<SpecialRuleRegistry.PickerEntry> entries =
            SpecialRuleRegistry.GetPickerEntries(_army.RuleDefinitions);
        _allNames = entries.Select(e => e.Name).ToArray();
        _numericNames = entries.Where(e => e.IsNumeric).Select(e => e.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    // A loaded army may reference a rule the picker no longer offers (e.g. an unimplemented one saved
    // earlier). Keep it visible/selected in that entry's combo so opening the army doesn't silently
    // relabel it; new additions still come only from the offered list.
    private string[] ComboNames(string current) =>
        Array.IndexOf(_allNames, current) >= 0 ? _allNames : _allNames.Append(current).ToArray();

    public void Draw(int screenW, int screenH)
    {
        RefreshRuleNames();

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("Army Builder",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);

        DrawToolbar();
        DrawArmyHeader();
        ImGui.Separator();
        DrawUnits();
        ImGui.Separator();
        DrawAddUnitButton();

        ImGui.End();
    }

    private void Save()
    {
        var (canceled, path) = TinyDialogs.SaveFileDialog("Save Army", "", ArmyFilter);
        if (canceled || string.IsNullOrEmpty(path)) return;

        if (Path.GetExtension(path) != ArmyListFile.EXTENSION_WITH_PERIOD)
            path = Path.ChangeExtension(path, ArmyListFile.EXTENSION_WITH_PERIOD);

        File.WriteAllText(path, JsonSerializer.Serialize(_army, RuleJson.Options));
    }

    private void Load()
    {
        var (canceled, paths) = TinyDialogs.OpenFileDialog("Load Army", "", false, ArmyFilter);
        if (canceled) return;

        string path = paths?.FirstOrDefault() ?? "";
        if (!File.Exists(path)) return;

        var loaded = JsonSerializer.Deserialize<ArmyListFile>(File.ReadAllText(path), RuleJson.Options);
        if (loaded is null) return;

        _army.Units.Clear();
        _army.Name     = loaded.Name;
        _army.Faction  = loaded.Faction;
        _army.PointsLimit = loaded.PointsLimit;
        _army.Units.AddRange(loaded.Units);
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("New"))
            ImGui.OpenPopup("ConfirmNew");

        ImGui.SameLine();
        if (ImGui.Button("Load")) Load();

        ImGui.SameLine();
        if (ImGui.Button("Save")) Save();

        ImGui.SameLine();
        if (ImGui.Button("Back")) OnBack?.Invoke();

        if (ImGui.BeginPopupModal("ConfirmNew", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Clear current army list and start fresh?");
            ImGui.Spacing();

            if (ImGui.Button("Yes", new Vector2(100, 0)))
            {
                _army.Units.Clear();
                _army.Name    = "";
                _army.Faction = "";
                _army.PointsLimit = 0;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(100, 0)))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
        }

        ImGui.Separator();
    }

    private void DrawArmyHeader()
    {
        string name = _army.Name;
        if (ImGui.InputText("List Name", ref name, 64))
            _army.Name = name;

        string faction = _army.Faction;
        if (ImGui.InputText("Faction", ref faction, 64))
            _army.Faction = faction;

        int limit = _army.PointsLimit;
        if (ImGui.InputInt("Points Limit", ref limit))
            _army.PointsLimit = limit;

        ImGui.Text($"Current total: {_army.TotalPoints} pts");
    }

    private void DrawUnits()
    {
        for (int u = 0; u < _army.Units.Count; ++u)
        {
            UnitFileEntry unit = _army.Units[u];
            ImGui.PushID(unit.StableID);

            bool open = ImGui.TreeNodeEx("unit", ImGuiTreeNodeFlags.None,
                $"{unit.Name}, {unit.PointCost} pts.");
            ImGui.SameLine();
            if (ImGui.SmallButton($"X##delunit{u}"))
            {
                _army.Units.RemoveAt(u--);
                ImGui.PopID();
                continue;
            }

            if (open)
            {
                DrawUnitFields(unit, u);
                ImGui.TreePop();
            }

            ImGui.PopID();
        }
    }

    private void DrawUnitFields(UnitFileEntry unit, int idx)
    {
        EditString($"Name##un{idx}", () => unit.Name, s => unit.Name = s, 48);

        int modelCount = unit.ModelCount;
        if (ImGui.InputInt($"Models##mc{idx}", ref modelCount))
            unit.ModelCount = modelCount;

        int quality = unit.Quality;
        if (ImGui.InputInt($"Quality##q{idx}", ref quality))
            unit.Quality = quality;

        int defense = unit.Defense;
        if (ImGui.InputInt($"Defense##d{idx}", ref defense))
            unit.Defense = defense;

        int pointCost = unit.PointCost;
        if (ImGui.InputInt($"Points##pc{idx}", ref pointCost))
            unit.PointCost = pointCost;

        DrawSpecialRuleList(unit.SpecialRules, $"unit{idx}");

        if (ImGui.CollapsingHeader($"Weapons##wp{idx}", ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int w = 0; w < unit.Weapons.Count; ++w)
            {
                WeaponFileEntry weapon = unit.Weapons[w];
                ImGui.PushID(weapon.StableID);

                bool weaponOpen = ImGui.TreeNodeEx("weapon", ImGuiTreeNodeFlags.None,
                    $"{weapon.Quantity}x {weapon.Name}");
                ImGui.SameLine();
                if (ImGui.SmallButton($"X##delw{idx}_{w}"))
                {
                    unit.Weapons.RemoveAt(w--);
                    ImGui.PopID();
                    continue;
                }

                if (weaponOpen)
                {
                    DrawWeaponFields(weapon, idx, w);
                    ImGui.TreePop();
                }

                ImGui.PopID();
            }

            if (ImGui.SmallButton($"Add Weapon##addw{idx}"))
                unit.Weapons.Add(new WeaponFileEntry());
        }
    }

    private void DrawWeaponFields(WeaponFileEntry weapon, int ui, int wi)
    {
        string name = weapon.Name;
        if (ImGui.InputText($"Name##wn{ui}_{wi}", ref name, 32))
            weapon.Name = name;

        int qty = weapon.Quantity;
        if (ImGui.InputInt($"Qty##wq{ui}_{wi}", ref qty))
            weapon.Quantity = Math.Max(1, qty);

        int range = weapon.RangeInches;
        if (ImGui.InputInt($"Range\"##wr{ui}_{wi}", ref range))
            weapon.RangeInches = range;

        int attacks = weapon.Attacks;
        if (ImGui.InputInt($"Attacks##wa{ui}_{wi}", ref attacks))
            weapon.Attacks = attacks;

        int ap = weapon.ArmorPenetration;
        if (ImGui.InputInt($"AP##wap{ui}_{wi}", ref ap))
            weapon.ArmorPenetration = ap;

        DrawSpecialRuleList(weapon.SpecialRules, $"w{ui}_{wi}");
    }

    private void DrawSpecialRuleList(List<SpecialRuleEntry> rules, string id)
    {
        if (!ImGui.CollapsingHeader($"Special Rules##sr_{id}", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        for (int i = 0; i < rules.Count; ++i)
        {
            ImGui.PushID(i);
            DrawRuleLine(rules, i);
            ImGui.PopID();
        }

        string popupId = $"AddRulePopup_{id}";
        if (ImGui.Button($"Add Rule##add_{id}"))
            ImGui.OpenPopup(popupId);

        if (ImGui.BeginPopup(popupId))
        {
            ref var state = ref CollectionsMarshal.GetValueRefOrAddDefault(
                _addRuleState, popupId, out _);
            if (state.sel == 0 && state.val == 0) state = (0, 1);

            if (_allNames.Length == 0) { ImGui.EndPopup(); return; }
            if (state.sel >= _allNames.Length) state.sel = 0;

            ImGui.Combo("Rule", ref state.sel, _allNames, _allNames.Length);

            bool isNumeric = _numericNames.Contains(_allNames[state.sel]);
            if (isNumeric)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(60);
                ImGui.InputInt("##val", ref state.val);
                if (state.val < 1) state.val = 1;
            }

            if (ImGui.Button("Add"))
            {
                rules.Add(isNumeric
                    ? new SpecialRuleEntry_CoreNumeric(_allNames[state.sel], state.val)
                    : new SpecialRuleEntry_Core(_allNames[state.sel]));
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Add Alias"))
            {
                SpecialRuleEntry inner = isNumeric
                    ? new SpecialRuleEntry_CoreNumeric(_allNames[state.sel], state.val)
                    : new SpecialRuleEntry_Core(_allNames[state.sel]);
                rules.Add(new SpecialRuleEntry_Alias("New Alias", inner));
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawRuleLine(List<SpecialRuleEntry> list, int idx)
    {
        SpecialRuleEntry current = list[idx];

        if (current is SpecialRuleEntry_Core core)
        {
            string[] names = ComboNames(core.PrintableName);
            int sel = Math.Max(0, Array.IndexOf(names, core.PrintableName));
            if (ImGui.Combo("##rule", ref sel, names, names.Length))
                list[idx] = _numericNames.Contains(names[sel])
                    ? new SpecialRuleEntry_CoreNumeric(names[sel], 1)
                    : new SpecialRuleEntry_Core(names[sel]);
        }
        else if (current is SpecialRuleEntry_CoreNumeric num)
        {
            string[] names = ComboNames(num.Name);
            int sel = Math.Max(0, Array.IndexOf(names, num.Name));
            if (ImGui.Combo("##rule", ref sel, names, names.Length))
                list[idx] = _numericNames.Contains(names[sel])
                    ? new SpecialRuleEntry_CoreNumeric(names[sel], num.NumericValue)
                    : new SpecialRuleEntry_Core(names[sel]);

            if (list[idx] is SpecialRuleEntry_CoreNumeric n2)
            {
                int v = n2.NumericValue;
                ImGui.SameLine();
                ImGui.SetNextItemWidth(60);
                if (ImGui.InputInt("##val", ref v) && v > 0)
                    list[idx] = n2 with { NumericValue = v };
            }
        }
        else if (current is SpecialRuleEntry_Alias alias)
        {
            float avail      = ImGui.GetContentRegionAvail().X;
            float aliasWidth = MathF.Min(200, avail * 0.35f);
            float comboWidth = MathF.Min(180, avail * 0.35f);
            float numWidth   = 60f;

            ImGui.SetNextItemWidth(aliasWidth);
            string label = alias.Name;
            if (ImGui.InputText("##alias", ref label, 32) && label.Length > 0)
                alias = alias with { Name = label };

            ImGui.SameLine(0, 4);
            SpecialRuleEntry inner = alias.AliasedRule;
            DrawRuleLineInner(ref inner, comboWidth, numWidth);
            list[idx] = alias with { AliasedRule = inner };
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("X"))
            list.RemoveAt(idx);
    }

    private void DrawRuleLineInner(ref SpecialRuleEntry rule, float comboWidth, float numWidth)
    {
        if (rule is SpecialRuleEntry_Core c)
        {
            string[] names = ComboNames(c.PrintableName);
            int sel = Math.Max(0, Array.IndexOf(names, c.PrintableName));
            ImGui.SetNextItemWidth(comboWidth);
            if (ImGui.Combo("##inner", ref sel, names, names.Length))
                rule = _numericNames.Contains(names[sel])
                    ? new SpecialRuleEntry_CoreNumeric(names[sel], 1)
                    : new SpecialRuleEntry_Core(names[sel]);
        }
        else if (rule is SpecialRuleEntry_CoreNumeric n)
        {
            string[] names = ComboNames(n.Name);
            int sel = Math.Max(0, Array.IndexOf(names, n.Name));
            ImGui.SetNextItemWidth(comboWidth);
            if (ImGui.Combo("##inner", ref sel, names, names.Length))
                rule = _numericNames.Contains(names[sel])
                    ? new SpecialRuleEntry_CoreNumeric(names[sel], n.NumericValue)
                    : new SpecialRuleEntry_Core(names[sel]);

            if (rule is SpecialRuleEntry_CoreNumeric n2)
            {
                int v = n2.NumericValue;
                ImGui.SameLine(0, 4);
                ImGui.SetNextItemWidth(numWidth);
                if (ImGui.InputInt("##innum", ref v) && v > 0)
                    rule = n2 with { NumericValue = v };
            }
        }
    }

    private void DrawAddUnitButton()
    {
        if (ImGui.Button("Add Unit"))
            _army.Units.Add(new UnitFileEntry { Name = "New Unit", ModelCount = 1 });
    }

    private static void EditString(string label, Func<string> getter, Action<string> setter,
        uint maxChars, ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    {
        string tmp = getter() ?? string.Empty;
        if (ImGui.InputText(label, ref tmp, maxChars, flags))
            setter(tmp);
    }
}
