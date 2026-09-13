using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>
    /// Name and model count, then the stats as the printed army list badges them (#329/#398): Quality
    /// and Defense always, Tough when the unit carries one. The numbers a player actually reads off a
    /// unit are the ones worth making findable at a glance, and they were previously buried mid-sentence
    /// in "Cult Rangers [5] - Qua 4+ Def 4+".
    /// </summary>
    internal static void DrawHeader(UnitFileEntry unit)
    {
        ImGui.TextUnformatted($"{unit.Name} [{unit.ModelCount}]");
        ImGui.SameLine();
        UiText.Disabled($"({unit.PointCost} pts)");

        UiChrome.DrawPill("Quality", $"{unit.Quality}+", ImGuiTheme.AccentBlue);
        ImGui.SameLine(0f, UiChrome.PillGap);
        UiChrome.DrawPill("Defense", $"{unit.Defense}+", ImGuiTheme.AccentBlue);
        if (ToughValue(unit) is { } tough)
        {
            ImGui.SameLine(0f, UiChrome.PillGap);
            UiChrome.DrawPill("Tough", tough, ImGuiTheme.AccentBlue);
        }
        ImGui.Separator();
    }

    /// <summary>
    /// The unit's Tough rating for its pill, or null when it has none. A saved unit carries no resolved
    /// wound count - Tough is a rule on the list - so it is read off the TYPED entry
    /// (<see cref="SpecialRuleEntry_CoreNumeric"/>) rather than by re-parsing the "Tough(3)" string this
    /// very code formats: the number is right there, and a parser would be a second place to be wrong.
    /// An alias ("Ancient Hide (Tough(6))") counts - the unit is Tough however the book names it.
    /// Internal so the walk is pinned by tests.
    /// </summary>
    internal static string? ToughValue(UnitFileEntry unit)
    {
        foreach (SpecialRuleEntry rule in unit.SpecialRules)
            if (ToughOf(rule) is { } value)
                return value;
        return null;
    }

    private static string? ToughOf(SpecialRuleEntry rule) => rule switch
    {
        SpecialRuleEntry_CoreNumeric numeric
            when string.Equals(numeric.Name, "Tough", StringComparison.OrdinalIgnoreCase)
            => numeric.NumericValue.ToString(CultureInfo.InvariantCulture),
        SpecialRuleEntry_Alias alias => ToughOf(alias.AliasedRule),
        _ => null,
    };

    /// <summary>
    /// Weapons as the printed list tabulates them - Weapon / RNG / ATK / AP / SPE - then wargear and the
    /// unit's own rules as text. The weapons were a run of parenthesised sentences whose numbers never
    /// lined up with each other; as columns two guns can actually be compared, which is the one thing a
    /// player does with this block. Rules keep their underline and hover (#259).
    /// </summary>
    internal static void DrawGear(UnitFileEntry unit, IReadOnlyList<ItemEntry> items, RuleGlossary glossary)
    {
        DrawWeaponTable(unit, glossary);

        ImGui.Indent();
        foreach (ItemEntry item in items)
            RuleTextFlow.Draw(RuleTextFlow.ItemLine(item), glossary, ImGuiCol.TextDisabled);
        if (unit.SpecialRules.Count > 0)
            RuleTextFlow.Draw(RuleTextFlow.RuleList(unit.SpecialRules), glossary, ImGuiCol.TextDisabled);
        ImGui.Unindent();
    }

    private static void DrawWeaponTable(UnitFileEntry unit, RuleGlossary glossary)
    {
        if (unit.Weapons.Count == 0) return;
        if (!ImGui.BeginTable("##gear", 5,
            ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp)) return;

        float em = ImGui.GetFontSize();
        ImGui.TableSetupColumn("Weapon", ImGuiTableColumnFlags.WidthStretch, 3f);
        ImGui.TableSetupColumn("RNG", ImGuiTableColumnFlags.WidthFixed, em * 2.6f);
        ImGui.TableSetupColumn("ATK", ImGuiTableColumnFlags.WidthFixed, em * 2.2f);
        ImGui.TableSetupColumn("AP", ImGuiTableColumnFlags.WidthFixed, em * 2.2f);
        ImGui.TableSetupColumn("SPE", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableHeadersRow();

        foreach (WeaponFileEntry weapon in unit.Weapons)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(ArmyListLayout.CountedName(weapon.Quantity, weapon.Name));
            ImGui.TableSetColumnIndex(1);
            UiText.Disabled(ArmyListLayout.RangeText(weapon.RangeInches));
            ImGui.TableSetColumnIndex(2);
            UiText.Disabled(ArmyListLayout.AttacksText(weapon.Attacks));
            ImGui.TableSetColumnIndex(3);
            UiText.Disabled(ArmyListLayout.ApText(weapon.ArmorPenetration));

            ImGui.TableSetColumnIndex(4);
            if (weapon.SpecialRules.Count == 0) UiText.Disabled("-");
            else RuleTextFlow.Draw(RuleTextFlow.RuleList(weapon.SpecialRules), glossary, ImGuiCol.TextDisabled);
        }

        ImGui.EndTable();
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
        UiText.Colored(ImGuiTheme.HeaderAccent, "UPGRADES");
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
                UiText.Colored(CyanText, "[linked]");
            }
            if (isReplace && switchAvailable == 0)
            {
                ImGui.SameLine();
                UiText.Disabled("(none to replace)");
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
    /// <para>That would have cost the label as a click target - the thing you actually aim at - so the
    /// footprint is MEASURED first, claimed by an invisible button, and the text is then drawn over it
    /// from the same cursor position. The obvious order (text, wind the cursor back, button, wind it
    /// forward again) crashed: the final <c>SetCursorPos</c> left the cursor past the pane's content
    /// extent with no item after it to justify the extent, which is exactly the state ImGui asserts on
    /// (<c>ErrorCheckUsingSetCursorPosToExtendParentBoundaries</c>) - and in the Army Forge an upgrade
    /// section IS the last thing in its pane, so adding any unit with options aborted the app. Measuring
    /// first means the cursor only ever moves BACKWARDS, and the very next call validates the extent.</para>
    ///
    /// <para>It does not interfere with the rule tooltips: <c>Draw</c> paints to the draw list and
    /// hit-tests the mouse position itself rather than asking ImGui which item is hovered.</para>
    /// </summary>
    private static bool DrawWrappedOptionLabel(UpgradeOption option, RuleGlossary glossary, string id)
    {
        IReadOnlyList<RuleTextFlow.RuleSegment> label =
            RuleTextFlow.OptionLabel(option, ArmyForgeScreen.OptionSummary(option));
        float width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        float height = MathF.Max(ImGui.GetTextLineHeight(), RuleTextFlow.MeasureHeight(label, width));

        Vector2 at = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton($"##label-{id}", new Vector2(width, height));
        ImGui.SetCursorScreenPos(at);
        RuleTextFlow.Draw(label, glossary, ImGuiCol.Text);
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
