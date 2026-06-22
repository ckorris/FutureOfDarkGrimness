using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FDG.Stages;
using ImGuiNET;
using TinyDialogsNet;

namespace FdgRaylib.Rendering;

public class ArmyBuilderScreen : IAppScreen
{
    public Action? OnBack;

    // Test-army-friendly default points; matched by the "start fresh" reset below.
    private const int DefaultPointsLimit = 1000;

    private readonly ArmyListFile _army = new() { PointsLimit = DefaultPointsLimit };

    private static readonly FileFilter ArmyFilter = new(
        $"Army List (*{ArmyListFile.EXTENSION_WITH_PERIOD})",
        new[] { $"*{ArmyListFile.EXTENSION_WITH_PERIOD}" });

    // The picker is derived from the engine catalog plus this army's embedded rules (#059 workstream 6),
    // refreshed each frame in Draw() so loading an army with embedded rules surfaces them immediately.
    // Split by scope (#059): a weapon's rule list only offers weapon-scoped rules and a unit's only
    // unit-scoped, so a mis-scoped rule (which load-time resolution silently skips) can't be authored.
    private readonly Dictionary<ERuleScope, string[]> _namesByScope = new();
    private readonly Dictionary<ERuleScope, HashSet<string>> _numericByScope = new();
    private static readonly Dictionary<string, (int sel, int val)> _addRuleState = new();

    // Rules a buff spell (Effect.AddRule) may grant: any non-numeric rule from either scope. AddRule carries
    // no numeric argument, so numeric rules (Tough(X)/Blast(X)/…) can't be granted meaningfully and are
    // excluded. Recomputed each frame in RefreshRuleNames so embedded rules surface too.
    private string[] _grantableRuleNames = Array.Empty<string>();

    private void RefreshRuleNames()
    {
        foreach (ERuleScope scope in new[] { ERuleScope.Unit, ERuleScope.Weapon })
        {
            IReadOnlyList<SpecialRuleRegistry.PickerEntry> entries =
                SpecialRuleRegistry.GetPickerEntries(_army.RuleDefinitions, scope);
            _namesByScope[scope] = entries.Select(e => e.Name).ToArray();
            _numericByScope[scope] = entries.Where(e => e.IsNumeric).Select(e => e.Name)
                .ToHashSet(StringComparer.Ordinal);
        }

        SortedSet<string> grantable = new(StringComparer.Ordinal);
        foreach (ERuleScope scope in new[] { ERuleScope.Unit, ERuleScope.Weapon })
            foreach (string name in NamesFor(scope))
                if (!IsNumericFor(scope, name))
                    grantable.Add(name);
        _grantableRuleNames = grantable.ToArray();
    }

    private string[] NamesFor(ERuleScope scope) =>
        _namesByScope.TryGetValue(scope, out string[]? n) ? n : Array.Empty<string>();

    private bool IsNumericFor(ERuleScope scope, string name) =>
        _numericByScope.TryGetValue(scope, out HashSet<string>? n) && n.Contains(name);

    // New numeric rules (e.g. Tough N) start at 3, and their ± steppers move in 3s.
    private const int DefaultRuleValue = 3;
    private const int RuleValueStep    = 3;

    // Width for the small rule-value field (e.g. Tough N): room for the digits PLUS both ± step buttons
    // (each a square of frame height) and their inner spacing, so neither the number nor the steppers
    // get clipped at any UI scale. At the old fixed 60px the buttons ate the whole width.
    private static float NumericFieldWidth()
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        float buttons = 2f * (ImGui.GetFrameHeight() + style.ItemInnerSpacing.X);
        float digits  = ImGui.GetFontSize() * 2.5f;
        return digits + buttons;
    }

    // A loaded army may reference a rule the picker no longer offers at this scope (e.g. an unimplemented
    // one, or one saved at the wrong scope earlier). Keep it visible/selected in that entry's combo so
    // opening the army doesn't silently relabel it; new additions still come only from the offered list.
    private string[] ComboNames(ERuleScope scope, string current)
    {
        string[] names = NamesFor(scope);
        return Array.IndexOf(names, current) >= 0 ? names : names.Append(current).ToArray();
    }

    // Like ComboNames, but over the grantable-rule set (buff spells). Keeps a loaded spell's current rule
    // visible even if the catalog no longer offers it, so opening an army never silently relabels it.
    private string[] GrantableComboNames(string current) =>
        Array.IndexOf(_grantableRuleNames, current) >= 0
            ? _grantableRuleNames
            : _grantableRuleNames.Append(current).ToArray();

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
        ImGui.Separator();
        DrawSpells();

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
        _army.RuleDefinitions.Clear();
        _army.Spells.Clear();
        _army.Name     = loaded.Name;
        _army.Faction  = loaded.Faction;
        _army.PointsLimit = loaded.PointsLimit;
        _army.Units.AddRange(loaded.Units);
        // Carry the army's embedded rule definitions and spell list too, so loading then saving
        // round-trips them (without these, a loaded caster army would lose its spells on save).
        _army.RuleDefinitions.AddRange(loaded.RuleDefinitions);
        _army.Spells.AddRange(loaded.Spells);
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
                _army.RuleDefinitions.Clear();
                _army.Spells.Clear();
                _army.Name    = "";
                _army.Faction = "";
                _army.PointsLimit = DefaultPointsLimit;
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

        DrawJoinedByHint(unit);

        DrawSpecialRuleList(unit.SpecialRules, $"unit{idx}", ERuleScope.Unit);

        // #006: the Hero join target is only meaningful for a unit carrying the Hero rule, so the field
        // appears right under Special Rules once that rule is present (no separate "is a hero" toggle).
        if (UnitHasHero(unit))
            DrawHeroJoinField(unit, idx);

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
                unit.Weapons.Add(new WeaponFileEntry { Attacks = 1 });
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

        DrawSpecialRuleList(weapon.SpecialRules, $"w{ui}_{wi}", ERuleScope.Weapon);
    }

    private void DrawSpecialRuleList(List<SpecialRuleEntry> rules, string id, ERuleScope scope)
    {
        if (!ImGui.CollapsingHeader($"Special Rules##sr_{id}", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        for (int i = 0; i < rules.Count; ++i)
        {
            ImGui.PushID(i);
            DrawRuleLine(rules, i, scope);
            ImGui.PopID();
        }

        string popupId = $"AddRulePopup_{id}";
        if (ImGui.Button($"Add Rule##add_{id}"))
            ImGui.OpenPopup(popupId);

        if (ImGui.BeginPopup(popupId))
        {
            ref var state = ref CollectionsMarshal.GetValueRefOrAddDefault(
                _addRuleState, popupId, out _);
            if (state.sel == 0 && state.val == 0) state = (0, DefaultRuleValue);

            string[] names = NamesFor(scope);
            if (names.Length == 0) { ImGui.EndPopup(); return; }
            if (state.sel >= names.Length) state.sel = 0;

            ImGui.Combo("Rule", ref state.sel, names, names.Length);

            bool isNumeric = IsNumericFor(scope, names[state.sel]);
            if (isNumeric)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(NumericFieldWidth());
                ImGui.InputInt("##val", ref state.val, RuleValueStep, RuleValueStep * 3);
                if (state.val < 1) state.val = 1;
            }

            if (ImGui.Button("Add"))
            {
                rules.Add(isNumeric
                    ? new SpecialRuleEntry_CoreNumeric(names[state.sel], state.val)
                    : new SpecialRuleEntry_Core(names[state.sel]));
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Add Alias"))
            {
                SpecialRuleEntry inner = isNumeric
                    ? new SpecialRuleEntry_CoreNumeric(names[state.sel], state.val)
                    : new SpecialRuleEntry_Core(names[state.sel]);
                rules.Add(new SpecialRuleEntry_Alias("New Alias", inner));
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawRuleLine(List<SpecialRuleEntry> list, int idx, ERuleScope scope)
    {
        SpecialRuleEntry current = list[idx];

        if (current is SpecialRuleEntry_Core core)
        {
            string[] names = ComboNames(scope, core.PrintableName);
            int sel = Math.Max(0, Array.IndexOf(names, core.PrintableName));
            if (ImGui.Combo("##rule", ref sel, names, names.Length))
                list[idx] = IsNumericFor(scope, names[sel])
                    ? new SpecialRuleEntry_CoreNumeric(names[sel], DefaultRuleValue)
                    : new SpecialRuleEntry_Core(names[sel]);
        }
        else if (current is SpecialRuleEntry_CoreNumeric num)
        {
            string[] names = ComboNames(scope, num.Name);
            int sel = Math.Max(0, Array.IndexOf(names, num.Name));
            if (ImGui.Combo("##rule", ref sel, names, names.Length))
                list[idx] = IsNumericFor(scope, names[sel])
                    ? new SpecialRuleEntry_CoreNumeric(names[sel], num.NumericValue)
                    : new SpecialRuleEntry_Core(names[sel]);

            if (list[idx] is SpecialRuleEntry_CoreNumeric n2)
            {
                int v = n2.NumericValue;
                ImGui.SameLine();
                ImGui.SetNextItemWidth(NumericFieldWidth());
                if (ImGui.InputInt("##val", ref v, RuleValueStep, RuleValueStep * 3) && v > 0)
                    list[idx] = n2 with { NumericValue = v };
            }
        }
        else if (current is SpecialRuleEntry_Alias alias)
        {
            float avail      = ImGui.GetContentRegionAvail().X;
            float aliasWidth = MathF.Min(200, avail * 0.35f);
            float comboWidth = MathF.Min(180, avail * 0.35f);
            float numWidth   = NumericFieldWidth();

            ImGui.SetNextItemWidth(aliasWidth);
            string label = alias.Name;
            if (ImGui.InputText("##alias", ref label, 32) && label.Length > 0)
                alias = alias with { Name = label };

            ImGui.SameLine(0, 4);
            SpecialRuleEntry inner = alias.AliasedRule;
            DrawRuleLineInner(ref inner, comboWidth, numWidth, scope);
            list[idx] = alias with { AliasedRule = inner };
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("X"))
            list.RemoveAt(idx);
    }

    private void DrawRuleLineInner(ref SpecialRuleEntry rule, float comboWidth, float numWidth, ERuleScope scope)
    {
        if (rule is SpecialRuleEntry_Core c)
        {
            string[] names = ComboNames(scope, c.PrintableName);
            int sel = Math.Max(0, Array.IndexOf(names, c.PrintableName));
            ImGui.SetNextItemWidth(comboWidth);
            if (ImGui.Combo("##inner", ref sel, names, names.Length))
                rule = IsNumericFor(scope, names[sel])
                    ? new SpecialRuleEntry_CoreNumeric(names[sel], DefaultRuleValue)
                    : new SpecialRuleEntry_Core(names[sel]);
        }
        else if (rule is SpecialRuleEntry_CoreNumeric n)
        {
            string[] names = ComboNames(scope, n.Name);
            int sel = Math.Max(0, Array.IndexOf(names, n.Name));
            ImGui.SetNextItemWidth(comboWidth);
            if (ImGui.Combo("##inner", ref sel, names, names.Length))
                rule = IsNumericFor(scope, names[sel])
                    ? new SpecialRuleEntry_CoreNumeric(names[sel], n.NumericValue)
                    : new SpecialRuleEntry_Core(names[sel]);

            if (rule is SpecialRuleEntry_CoreNumeric n2)
            {
                int v = n2.NumericValue;
                ImGui.SameLine(0, 4);
                ImGui.SetNextItemWidth(numWidth);
                if (ImGui.InputInt("##innum", ref v, RuleValueStep, RuleValueStep * 3) && v > 0)
                    rule = n2 with { NumericValue = v };
            }
        }
    }

    // ---- #006 Hero join wiring ----------------------------------------------------------------
    // Ids/JoinsUnitId stay invisible plumbing: the user picks a host by name from a filtered combo, and
    // the builder generates the host's Id on demand and mirrors the engine's eligibility so it can't
    // author a join that silently deploys solo at game time.

    private static readonly Vector4 WarnColor = new(1f, 0.55f, 0.15f, 1f);
    private static readonly Vector4 HintColor = new(0.6f, 0.6f, 0.6f, 1f);

    private void DrawHeroJoinField(UnitFileEntry hero, int idx)
    {
        // Hosts a hero may join: a different multi-model unit in this army that isn't itself a Hero.
        List<UnitFileEntry> hosts = _army.Units
            .Where(u => !ReferenceEquals(u, hero) && u.ModelCount > 1 && !UnitHasHero(u))
            .ToList();

        // The currently-referenced target (if any), regardless of whether it is still eligible — kept
        // visible so editing the host (e.g. dropping it to one model) surfaces a warning instead of
        // silently clearing the link.
        UnitFileEntry? target = string.IsNullOrEmpty(hero.JoinsUnitId)
            ? null
            : _army.Units.FirstOrDefault(u => !ReferenceEquals(u, hero) && u.Id == hero.JoinsUnitId);

        bool targetIneligible = target != null && !hosts.Contains(target);
        if (targetIneligible)
            hosts.Add(target!);

        string[] options = new string[hosts.Count + 1];
        options[0] = "(none – deploys solo)";
        for (int i = 0; i < hosts.Count; ++i)
            options[i + 1] = ReferenceEquals(hosts[i], target) && targetIneligible
                ? $"{hosts[i].Name} (ineligible)"
                : hosts[i].Name;

        int sel = target == null ? 0 : hosts.IndexOf(target) + 1;

        ImGui.SetNextItemWidth(240);
        if (ImGui.Combo($"Joins unit##join{idx}", ref sel, options, options.Length))
        {
            if (sel == 0)
                hero.JoinsUnitId = null;
            else
                hero.JoinsUnitId = EnsureId(hosts[sel - 1]);
        }

        if (hero.ModelCount != 1)
            Warn("A Hero must be a single-model unit to join a unit.");

        if (TryGetToughValue(hero, out int tough) && tough > HeroJoinResolver.MaxHeroToughForJoin)
            Warn($"A Hero with Tough({tough}) can't join a unit (max Tough({HeroJoinResolver.MaxHeroToughForJoin})).");

        if (!string.IsNullOrEmpty(hero.JoinsUnitId) && target == null)
            Warn("Join target was deleted; this hero will deploy on its own.");

        if (targetIneligible)
            Warn("Join target is no longer eligible (must be a multi-model unit without another Hero).");
    }

    // Host-side breadcrumb: shows which hero(es) have selected this unit as their join target.
    private void DrawJoinedByHint(UnitFileEntry unit)
    {
        if (string.IsNullOrEmpty(unit.Id))
            return;

        List<string> joiners = _army.Units
            .Where(h => !ReferenceEquals(h, unit) && UnitHasHero(h) && h.JoinsUnitId == unit.Id)
            .Select(h => h.Name)
            .ToList();

        if (joiners.Count > 0)
            ImGui.TextColored(HintColor, $"Joined by: {string.Join(", ", joiners)}");
    }

    private string EnsureId(UnitFileEntry host)
    {
        if (string.IsNullOrEmpty(host.Id))
            host.Id = Guid.NewGuid().ToString("N");
        return host.Id;
    }

    private static void Warn(string message) => ImGui.TextColored(WarnColor, $"! {message}");

    private static bool UnitHasHero(UnitFileEntry unit) => unit.SpecialRules.Any(RuleEntryIsHero);

    private static bool RuleEntryIsHero(SpecialRuleEntry entry) => entry switch
    {
        SpecialRuleEntry_Core core => string.Equals(core.Name, "Hero", StringComparison.Ordinal),
        SpecialRuleEntry_CoreNumeric num => string.Equals(num.Name, "Hero", StringComparison.Ordinal),
        SpecialRuleEntry_Alias alias => RuleEntryIsHero(alias.AliasedRule),
        _ => false,
    };

    private static bool TryGetToughValue(UnitFileEntry unit, out int value)
    {
        foreach (SpecialRuleEntry entry in unit.SpecialRules)
        {
            if (TryGetNumericRule(entry, "Tough", out value))
                return true;
        }

        value = 0;
        return false;
    }

    private static bool TryGetNumericRule(SpecialRuleEntry entry, string ruleName, out int value)
    {
        switch (entry)
        {
            case SpecialRuleEntry_CoreNumeric num when string.Equals(num.Name, ruleName, StringComparison.Ordinal):
                value = num.NumericValue;
                return true;
            case SpecialRuleEntry_Alias alias:
                return TryGetNumericRule(alias.AliasedRule, ruleName, out value);
            default:
                value = 0;
                return false;
        }
    }

    private void DrawAddUnitButton()
    {
        if (ImGui.Button("Add Unit"))
            _army.Units.Add(new UnitFileEntry
            {
                Name = "New Unit", ModelCount = 1, Quality = 4, Defense = 4, PointCost = 100,
            });
    }

    // ---- #033 Spell authoring ---------------------------------------------------------------------
    // Army-wide spell list, castable by any unit with Caster(X). SpellDefinition / TargetSelector / Effect
    // are immutable records, so each field edit rebuilds the record — the same pattern the special-rule
    // editor above uses. Combos that don't map 1:1 to an enum (affinity, buff duration) use a parallel
    // value array so the displayed order is independent of the enum's.

    private static readonly ETargetAffinity[] SpellAffinities =
        { ETargetAffinity.Foe, ETargetAffinity.Friend, ETargetAffinity.Any, ETargetAffinity.Self };
    private static readonly string[] SpellAffinityNames = { "Enemy", "Friendly", "Any", "Self" };

    private static readonly string[] SpellEffectKindNames =
        { "Damage (deal hits)", "Buff (grant rule)", "Stat modifier (roll bonus)" };

    private static readonly ERollKind[] SpellRollKinds = { ERollKind.Hit, ERollKind.Save, ERollKind.Morale };
    private static readonly string[] SpellRollKindNames = { "To-hit", "Defense (save)", "Morale" };

    private static readonly ELifetime[] BuffScopes =
        { ELifetime.NextTrigger, ELifetime.ThisActivation, ELifetime.ThisRound };
    private static readonly string[] BuffScopeNames = { "Once (next time)", "This activation", "This round" };

    private void DrawSpells()
    {
        if (!ImGui.CollapsingHeader("Spells##armyspells", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextColored(HintColor, "Army-wide spells, castable by any unit with the Caster(X) rule.");

        for (int s = 0; s < _army.Spells.Count; ++s)
        {
            SpellDefinition spell = _army.Spells[s];
            ImGui.PushID($"spell{s}");

            bool open = ImGui.TreeNodeEx("spell", ImGuiTreeNodeFlags.None, $"{spell.Name} ({spell.Threshold})");
            ImGui.SameLine();
            if (ImGui.SmallButton("X##delspell"))
            {
                _army.Spells.RemoveAt(s--);
                ImGui.PopID();
                continue;
            }

            if (open)
            {
                _army.Spells[s] = DrawSpellFields(spell, s);
                ImGui.TreePop();
            }

            ImGui.PopID();
        }

        if (ImGui.Button("Add Spell"))
            _army.Spells.Add(new SpellDefinition("New Spell", 1,
                new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                new Effect.DealHits(1, Array.Empty<string>(), 0)));
    }

    private SpellDefinition DrawSpellFields(SpellDefinition spell, int idx)
    {
        string name = spell.Name;
        if (ImGui.InputText($"Name##sn{idx}", ref name, 48))
            spell = spell with { Name = name };

        int threshold = spell.Threshold;
        if (ImGui.InputInt($"Tokens to cast##stk{idx}", ref threshold))
            spell = spell with { Threshold = Math.Max(1, threshold) };

        spell = spell with { Target = DrawTargetFields(spell.Target, idx) };
        spell = spell with { Effect = DrawEffectFields(spell.Effect, idx) };

        // Live preview of exactly the subtext the cast menu will show.
        ImGui.TextColored(HintColor, SpellText.Describe(spell));
        return spell;
    }

    private TargetSelector DrawTargetFields(TargetSelector target, int idx)
    {
        int range = (int)target.RangeInches;
        if (ImGui.InputInt($"Range\"##sr{idx}", ref range))
            target = target with { RangeInches = Math.Max(0, range) };

        int affinitySel = Math.Max(0, Array.IndexOf(SpellAffinities, target.TargetAffinity));
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo($"Targets##sa{idx}", ref affinitySel, SpellAffinityNames, SpellAffinityNames.Length))
            target = target with { TargetAffinity = SpellAffinities[affinitySel] };

        int maxCount = target.MaxCount;
        if (ImGui.InputInt($"Max targets##smc{idx}", ref maxCount))
        {
            maxCount = Math.Max(1, maxCount);
            target = target with { MaxCount = maxCount, MinCount = Math.Min(target.MinCount, maxCount) };
        }

        bool los = target.RequireLineOfSight;
        if (ImGui.Checkbox($"Requires line of sight##sl{idx}", ref los))
            target = target with { RequireLineOfSight = los };

        return target;
    }

    private Effect DrawEffectFields(Effect effect, int idx)
    {
        int currentKind = EffectKindIndex(effect);
        int kindSel = currentKind;
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo($"Effect##sek{idx}", ref kindSel, SpellEffectKindNames, SpellEffectKindNames.Length)
            && kindSel != currentKind)
        {
            effect = DefaultEffectForKind(kindSel);
        }

        if (effect is Effect.DealHits dh)
        {
            int count = dh.Count;
            if (ImGui.InputInt($"Hits##sdc{idx}", ref count))
                dh = dh with { Count = Math.Max(1, count) };

            int ap = dh.ArmorPenetration;
            if (ImGui.InputInt($"AP##sap{idx}", ref ap))
                dh = dh with { ArmorPenetration = Math.Max(0, ap) };

            List<string> withRules = dh.WithRules.ToList();
            ImGui.TextColored(HintColor, "Weapon rules on each hit (e.g. Bane, Blast):");
            DrawWithRulesEditor(withRules, idx);
            return dh with { WithRules = withRules };
        }

        if (effect is Effect.AddRule ar)
        {
            string[] names = GrantableComboNames(ar.RuleName);
            int ruleSel = Math.Max(0, Array.IndexOf(names, ar.RuleName));
            ImGui.SetNextItemWidth(200);
            if (ImGui.Combo($"Grants rule##sgr{idx}", ref ruleSel, names, names.Length))
                ar = ar with { RuleName = names[ruleSel] };

            int scopeSel = Math.Max(0, Array.IndexOf(BuffScopes, ar.Scope));
            ImGui.SetNextItemWidth(180);
            if (ImGui.Combo($"Duration##ssc{idx}", ref scopeSel, BuffScopeNames, BuffScopeNames.Length))
                ar = ar with { Scope = BuffScopes[scopeSel] };

            return ar;
        }

        if (effect is Effect.StatModifier sm)
        {
            int rollSel = Math.Max(0, Array.IndexOf(SpellRollKinds, sm.Roll));
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo($"Roll##smr{idx}", ref rollSel, SpellRollKindNames, SpellRollKindNames.Length))
                sm = sm with { Roll = SpellRollKinds[rollSel] };

            int delta = sm.Delta;
            ImGui.SetNextItemWidth(NumericFieldWidth());
            if (ImGui.InputInt($"Modifier (+/-)##smd{idx}", ref delta))
                sm = sm with { Delta = delta };

            int scopeSel = Math.Max(0, Array.IndexOf(BuffScopes, sm.LifetimeScope));
            ImGui.SetNextItemWidth(180);
            if (ImGui.Combo($"Duration##sms{idx}", ref scopeSel, BuffScopeNames, BuffScopeNames.Length))
                sm = sm with { LifetimeScope = BuffScopes[scopeSel] };

            return sm;
        }

        return effect;
    }

    private static int EffectKindIndex(Effect effect) => effect switch
    {
        Effect.AddRule => 1,
        Effect.StatModifier => 2,
        _ => 0, // DealHits (and anything else) → Damage
    };

    private static Effect DefaultEffectForKind(int kind) => kind switch
    {
        1 => new Effect.AddRule("Furious", ELifetime.NextTrigger),
        2 => new Effect.StatModifier(ERollKind.Hit, 1, ELifetime.NextTrigger),
        _ => new Effect.DealHits(1, Array.Empty<string>(), 0),
    };

    // Picks the weapon rules a damage spell's hits carry from the weapon-scoped catalog (so names are valid),
    // storing each as a "Name" or "Name(N)" string the engine parses back at load. Avoids a free-text field,
    // which would fight the user as the displayed value is re-derived from the list each frame.
    private void DrawWithRulesEditor(List<string> rules, int idx)
    {
        for (int r = 0; r < rules.Count; ++r)
        {
            ImGui.PushID($"wr{idx}_{r}");
            ImGui.TextUnformatted($"- {rules[r]}");
            ImGui.SameLine();
            if (ImGui.SmallButton("X")) rules.RemoveAt(r--);
            ImGui.PopID();
        }

        string[] names = NamesFor(ERuleScope.Weapon);
        if (names.Length == 0) return;

        string key = $"spellrules{idx}";
        ref var state = ref CollectionsMarshal.GetValueRefOrAddDefault(_addRuleState, key, out _);
        if (state.sel == 0 && state.val == 0) state = (0, DefaultRuleValue);
        if (state.sel >= names.Length) state.sel = 0;

        ImGui.SetNextItemWidth(180);
        ImGui.Combo($"##wraddc{idx}", ref state.sel, names, names.Length);

        bool isNumeric = IsNumericFor(ERuleScope.Weapon, names[state.sel]);
        if (isNumeric)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(NumericFieldWidth());
            ImGui.InputInt($"##wraddv{idx}", ref state.val, RuleValueStep, RuleValueStep * 3);
            if (state.val < 1) state.val = 1;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton($"Add rule##wradd{idx}"))
            rules.Add(isNumeric ? $"{names[state.sel]}({state.val})" : names[state.sel]);
    }

    private static void EditString(string label, Func<string> getter, Action<string> setter,
        uint maxChars, ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    {
        string tmp = getter() ?? string.Empty;
        if (ImGui.InputText(label, ref tmp, maxChars, flags))
            setter(tmp);
    }
}
