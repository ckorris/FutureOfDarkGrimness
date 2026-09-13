# 336 — Melee weapon rules read the way shooting's do

**Status**: in progress
**Related**: #298 (put the rule text under the melee buttons in the first place), #292 (the shoot panel's
inline hoverable rule names), #321 (the hold-back companion button on the melee row), #320 (the hold-back
consequence line), #259 (the Army Forge underline/hover convention this all descends from)

## Goal
The melee weapon menu explains its special rules with a permanent block of full descriptions under every
button; the shoot panel puts the rule NAMES inline on the row (tinted, underlined, hover for the text) and
expands the full text once, in a Details pane, for the weapon currently under attention. The Army Forge and
the in-game army list use the same inline-and-hover convention. Melee is the only place that does it the
other way, and the block inflates rows enough to push options out of the panel.

Make melee read like shooting: inline hoverable rule names on the button, plus a Details strip fed by the
hovered row.

## Notes

- 2026-08-04: **GUI hand-verify round 1 found a real bug** (owner: "while hovering over a special rule,
  the grayed out text of weapons that aren't available disappears"). Cause: the row loop collected the
  frame's tooltip with `ruleTooltip ??= RuleHoverText.DrawInline(...)`, and `??=` does not evaluate its
  right-hand side once the left is non-null - but `DrawInline` DRAWS the line as well as reporting what
  the cursor is on. So from the hovered rule onward nothing was drawn, and since invalid options are
  appended after every valid one, the greyed rows were always in that dead zone. Now called
  unconditionally with the tooltip coalesced afterwards, at both sites: the same one-liner was already
  in `ArmyListOverlay.DrawWrappedSegments` (pre-existing, same defect, would blank the later wrapped
  lines of a rules block), fixed too. Swept every other `??=` under `FdgRaylib/Rendering` - the rest are
  lazy-init or already-computed locals, all correct.
  **Not covered by a test**: `DrawInline` needs an ImGui context and the app suite has none by design
  (pure logic only). The guard is a comment at both call sites; this stays hand-verify territory.
- 2026-08-04: **Hand-verify asset**: `WeaponRules.fdgsave` (repo root), compiled from
  `Scenarios/336-weapon-rules-showcase.json` + `Scenarios/armies/RuleShowcase.fdgarmy`. Built to put every
  display state on ONE screen rather than needing several games: the Blademasters' first melee menu has two
  valid rule-bearing rows (Great Sword `Deadly(3), Rending`; Demo Charge `Limited, Deadly(6)`, which also
  carries the #321 Hold back companion and its #320 consequence line) above two GREYED rule-bearing rows
  the Deadly gate is holding (Serrated Blade `Rending, Lacerate`; Odd Dagger `Mysterious` - a rule the army
  file defines with an empty description, so it is the faded-underline "not enforced in play" case). The
  Marksmen next door carry the shooting comparison: `Rending, Reliable` / `Blast(3), Indirect` /
  `Limited, Deadly(3)` (the last for the #319 ONCE PER GAME badge). Dummies are Tough(6) so the menus can
  be reopened. Verified headless: every state listed above appears, and `Mysterious` loads with no warning.
- 2026-08-04: Implemented in one slice. Engine: `StringSelectionRequest.OptionRules` (new) +
  `ChooseMeleeWeaponStage.BuildOptionRules`, `BuildRuleDescriptions` narrowed to the hold-back line;
  `MeleeWeaponRuleDescriptionTests` re-pointed and grown to 7. App: `OptionRuleSegments` +
  `OptionRuleDetailsLayout` (both new), `GuiStringSelectionResolver` row drawing and vertical budget, CLI
  resolver prints from the structured rules. 19 new app tests. Engine suite 2790/2790, app suite
  1043/1043, full build clean, headless smoke exits 0. Engine `4416ed7`. **Awaiting GUI hand-verify.**
- 2026-08-04: Verified the wire end-to-end on a probe scenario rather than trusting the suites: a
  two-rule melee weapon comes back with `Rending, Reliable` appended to the label in attachment order and
  the rules list in that same order, which is the ordering assumption the segment scan rests on.
- 2026-08-04: Filed. Owner picked the engine-side fix (structured rules on the request) over a client-side
  parse of the description strings, and picked the Details-strip variant over hover-only, noting the
  buttons can afford to be taller.

## Decisions

**The engine ships the rules structured, not as prose.** `StringSelectionRequest` carries only option
STRINGS, so the GUI resolver has no `Weapon` and no `ResolvedRule`s to build `RuleHoverText.Segment`s from
— which is exactly why #298 shipped the descriptions as a pre-formatted block in the first place. The
alternative was to parse the `Name - description` lines back apart client-side and match them against the
label text; that is a string-shaped contract between two projects and it would rot. So the request gains
`OptionRules` (name + description per option) alongside the free-form `OptionDescriptions`, and the two
fields keep separate jobs: structured rule glosses vs. free-form consequence text (#320's hold-back line
stays in the latter, because it describes the DECLINE, not the weapon).

**Undocumented rules are included, unlike #298.** #298 dropped a rule with no description entirely, on the
grounds that a bare name adds nothing over the label. The shoot panel disagrees: it renders such a rule
with a faded underline and a tooltip saying the engine will not resolve it, which is real information at
the moment of choosing. Since the whole point of this item is that the two agree, melee now carries every
rule and lets the front end decide. The CLI keeps printing documented ones only, so #298's CLI output is
unchanged.

**The Details strip follows hover, and is sticky.** Shooting's Details pane keys off the SELECTED weapon,
but melee has no selection — clicking a weapon commits the attack. So the strip follows the mouse instead,
and does not clear when the mouse leaves the row: a strip that emptied as soon as you moved toward it
would be unreadable. It defaults to the first rule-bearing option and only exists on requests that carry
`OptionRules`, so every other string menu (the action menu, spell and ability pickers) is pixel-identical.

**A greyed row's reason is kept out of the name scan.** The rule names are located inside the finished
label by matching from the RIGHT, because a weapon whose name contains a rule word ("Rending Blade - A2,
AP0, Rending") would otherwise underline the wrong run. That same scan would have been fooled by an
invalid row, whose label continues with the reason it is greyed - "1x Demo Charge - A1, AP0, Limited
(Already used this game (Limited).)" repeats the rule's own name verbatim. The resolver splits the label
into the option and its reason and only ever scans the option.

## Outcome

_pending GUI hand-verify_
