using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Draws one unit the way the Army Forge's third column draws it: stat line, gear with rule hovers,
/// and the interactive upgrade sections.
///
/// <para>
/// Split out of <see cref="ArmyForgeScreen"/> for #397. The Combat Calculator builds units the same
/// way the Forge does, and a second copy of "which upgrades may be taken, and what do they cost"
/// would be a second thing to keep correct - the availability rules here are subtle enough (#324's
/// reservation, #383's per-model budget, combined pairs' linked sections) that two copies would drift.
/// The availability MATHS still lives on the Forge screen, where its tests point; this owns the drawing.
/// </para>
/// </summary>
internal static class ForgeUnitDetail
{
    private static readonly Vector4 CyanText = new(0.45f, 0.80f, 0.90f, 1f);

    /// <summary>Name, model count, stat line and points.</summary>
    internal static void DrawHeader(UnitFileEntry unit)
    {
        ImGui.TextUnformatted(ArmyBuilderScreen.UnitStatLine(unit));
        ImGui.SameLine();
        ImGui.TextDisabled($"({unit.PointCost} pts)");
        ImGui.Separator();
    }

    /// <summary>Weapons, wargear and special rules, each rule underlined with its hover (#259).</summary>
    internal static void DrawGear(UnitFileEntry unit, IReadOnlyList<ItemEntry> items, RuleGlossary glossary)
    {
        ImGui.Indent();
        foreach (WeaponFileEntry weapon in unit.Weapons)
            RuleTextFlow.Draw(RuleTextFlow.WeaponLine(weapon), glossary, ImGuiCol.TextDisabled);
        foreach (ItemEntry item in items)
            RuleTextFlow.Draw(RuleTextFlow.ItemLine(item), glossary, ImGuiCol.TextDisabled);
        if (unit.SpecialRules.Count > 0)
            RuleTextFlow.Draw(RuleTextFlow.RuleList(unit.SpecialRules), glossary, ImGuiCol.TextDisabled);
        ImGui.Unindent();
    }

    /// <summary>
    /// A unit that cannot be edited here - one read straight out of a saved army file, which carries no
    /// book and so has no upgrade options to offer.
    /// </summary>
    internal static void DrawReadOnly(UnitFileEntry unit, RuleGlossary glossary)
    {
        DrawHeader(unit);
        DrawGear(unit, Array.Empty<ItemEntry>(), glossary);
    }

    // Interactive upgrade sections: mutate the BuilderUnit's choices; the per-frame recompile re-costs live.
    // When <paramref name="mirror"/> is non-null this unit is combined, and whole-unit (Affects=All) sections
    // are shared: any edit to one is copied to the partner copy (marked "[linked]"), so a "Replace all X" swap
    // applies to both halves and is paid on both. Per-model/one/any sections stay independent per copy.
    internal static void DrawUpgrades(BookFile book, RuleGlossary glossary, BuilderUnit bu, RosterUnit roster,
        UnitFileEntry compiledUnit, List<ItemEntry> items, BuilderUnit? mirror = null)
    {
        if (roster.Sections.Count == 0) return;
        ImGui.Spacing();
        ImGui.TextDisabled("UPGRADES");
        ImGui.Separator();

        foreach (UpgradeSection section in roster.Sections)
        {
            bool isReplace = section.Variant == UpgradeVariant.Replace;
            bool linked = mirror != null && section.Affects == UpgradeAffects.All;
            // #324: when a single-target all-swap above this section has been taken, the compiler now leaves
            // this section its copies rather than eating the pool - so availability must be measured against
            // that same reservation, or the Forge would gray out a swap the compiler would honour. Only pay
            // for the extra compile when such a rival is actually selected.
            int available = !isReplace ? int.MaxValue
                : ArmyForgeScreen.YieldingAllSwapChosen(bu, roster, section)
                    ? ArmyForgeScreen.ReplacePool(book, bu, roster, section, excludeOwn: false)
                    : ListCompiler.AvailableApplications(compiledUnit.Weapons, items, section.Targets);
            // Availability ignoring this section's OWN pick: a mutually-exclusive (radio) pick returns its
            // replaced target to the pool the moment you switch away, so other options must not gray out
            // just because the current pick consumed the target (hand-verify round 2). Also drives the
            // header: "(none to replace)" only when there's no target even without this section's choice.
            int switchAvailable = isReplace ? ArmyForgeScreen.AvailableExcludingSection(book, bu, section) : int.MaxValue;

            ImGui.TextUnformatted(section.Label);
            if (linked)
            {
                ImGui.SameLine();
                ImGui.TextColored(CyanText, "[linked]");
            }
            if (isReplace && switchAvailable == 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(none to replace)");
            }
            ImGui.Indent();

            if (section.IsCounted) // "any"/"up to N" or add-models → a stepper
            {
                // #383: a per-model section ("Any model may replace/take ...") spends one pick per MODEL,
                // shared across its options — 3 models may take 3x of one gun OR spread across guns, never
                // 3 of each. Other counted sections keep independent steppers.
                int sectionTotal = section.PerModelBudget
                    ? section.Options.Sum(o => BuilderListEditing.ChoiceCount(bu, section.Id, o.Id))
                    : 0;
                foreach (UpgradeOption option in section.Options)
                {
                    int v = BuilderListEditing.ChoiceCount(bu, section.Id, option.Id);
                    DrawStepper(bu, glossary, section, option, v,
                        ArmyForgeScreen.StepperMax(section, roster, compiledUnit, available, v, sectionTotal - v));
                }
            }
            else if (section.MaxPicks <= 1 && section.Options.Count >= 2) // pick one of several → radios
            {
                bool noneChosen = !section.Options.Any(o => BuilderListEditing.IsChosen(bu, section.Id, o.Id));
                if (ImGui.RadioButton($"- none -##{section.Id}-none", noneChosen))
                    BuilderListEditing.ApplyChoice(bu, mirror, section, string.Empty, 0);
                foreach (UpgradeOption option in section.Options)
                {
                    bool chosen = BuilderListEditing.IsChosen(bu, section.Id, option.Id);
                    ImGui.BeginDisabled(isReplace && switchAvailable == 0 && !chosen);
                    string id = $"{section.Id}-{option.Id}";
                    bool hit = ImGui.RadioButton($"##{id}", chosen);
                    ImGui.SameLine();
                    hit |= DrawWrappedOptionLabel(option, glossary, id);
                    if (hit) BuilderListEditing.ApplyChoice(bu, mirror, section, option.Id, 1);
                    ImGui.EndDisabled();
                }
            }
            else // single option (binary) or multi-select → checkboxes
            {
                foreach (UpgradeOption option in section.Options)
                {
                    bool chosen = BuilderListEditing.IsChosen(bu, section.Id, option.Id);
                    ImGui.BeginDisabled(isReplace && available == 0 && !chosen);
                    string id = $"{section.Id}-{option.Id}";
                    bool hit = ImGui.Checkbox($"##{id}", ref chosen);
                    ImGui.SameLine();
                    // Clicking the label toggles, so the box's state is what the label reports back.
                    if (DrawWrappedOptionLabel(option, glossary, id))
                    {
                        chosen = !chosen;
                        hit = true;
                    }
                    if (hit) BuilderListEditing.ApplyChoice(bu, mirror, section, option.Id, chosen ? 1 : 0);
                    ImGui.EndDisabled();
                }
            }
            ImGui.Unindent();
        }
    }

    /// <summary>
    /// An upgrade option's label, WRAPPED, with its rule names underlined and hoverable, and the whole
    /// block clickable so it still toggles the control beside it.
    ///
    /// <para>An ImGui checkbox/radio label is one line by construction: a long option
    /// ("Uranium Rifle (30\", A1, AP(1), Reliable, Shred, Takedown)") ran off the edge of the column and
    /// the tail was simply lost. So the control is drawn with NO label and the text is laid out
    /// separately by <see cref="RuleTextFlow.Draw"/>, which wraps at the remaining width and hangs
    /// continuation lines under the first.</para>
    ///
    /// <para>That would have cost the label as a click target - the thing you actually aim at - so an
    /// invisible button is laid over the text afterwards. It does not interfere with the rule tooltips:
    /// <c>Draw</c> paints to the draw list and hit-tests the mouse position itself, in the same frame,
    /// before this button is ever submitted.</para>
    /// </summary>
    private static bool DrawWrappedOptionLabel(UpgradeOption option, RuleGlossary glossary, string id)
    {
        RuleTextFlow.Draw(RuleTextFlow.OptionLabel(option, ArmyForgeScreen.OptionSummary(option)),
            glossary, ImGuiCol.Text);

        // The Dummy that Draw reserved IS the text's footprint, so the last item's rect is the region to
        // cover - no re-measuring, and it stays correct however many lines the text wrapped to.
        Vector2 min = ImGui.GetItemRectMin();
        Vector2 size = ImGui.GetItemRectMax() - min;
        if (size.X <= 0f || size.Y <= 0f) return false;

        Vector2 restore = ImGui.GetCursorPos();
        ImGui.SetCursorScreenPos(min);
        bool clicked = ImGui.InvisibleButton($"##label-{id}", size);
        ImGui.SetCursorPos(restore);
        return clicked;
    }

    // Counted-section control: [-] [count] [+] label. The buttons gray individually at their bound (- at 0,
    // + at max) and the type-in box has no internal step buttons (step 0), so it's wide enough for the number.
    private static void DrawStepper(BuilderUnit bu, RuleGlossary glossary, UpgradeSection section,
        UpgradeOption option, int v, int max)
    {
        string id = $"{section.Id}-{option.Id}";
        float frameH = ImGui.GetFrameHeight();

        ImGui.BeginDisabled(v <= 0);
        if (ImGui.Button($"-##{id}-dec", new Vector2(frameH, frameH)))
            BuilderListEditing.SetChoice(bu, section, option.Id, v - 1);
        ImGui.EndDisabled();
        ImGui.SameLine();

        int typed = v;
        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 2.5f);
        ImGui.BeginDisabled(max == 0 && v == 0);
        if (ImGui.InputInt($"##{id}-val", ref typed, 0))
            BuilderListEditing.SetChoice(bu, section, option.Id, Math.Clamp(typed, 0, max));
        ImGui.EndDisabled();
        ImGui.SameLine();

        ImGui.BeginDisabled(v >= max);
        if (ImGui.Button($"+##{id}-inc", new Vector2(frameH, frameH)))
            BuilderListEditing.SetChoice(bu, section, option.Id, v + 1);
        ImGui.EndDisabled();
        ImGui.SameLine();
        RuleTextFlow.Draw(RuleTextFlow.OptionLabel(option, ArmyForgeScreen.OptionSummary(option)), glossary, ImGuiCol.Text);
    }




}
