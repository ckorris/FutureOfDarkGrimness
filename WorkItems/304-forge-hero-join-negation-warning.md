# 304 — Army Forge: warn when joining a hero switches off a host unit's ability

**Status**: todo
**Related**: #303 (the engine-side mechanic and the fork that decides this warning's wording), #006 (hero join), #061

## Goal

In the Army Forge, when the author picks a host unit for a hero, surface the rules that joining will
render inert. Today the negation is completely invisible at list-building time and only shows up as a
rule that quietly never fires in play. Done = choosing a host that costs the host unit one or more
rules shows a warning naming those rules, next to the join control.

## Notes

- 2026-07-30: Filed. The mechanic: `Condition.AllModelsHaveThisRule` excludes a joined hero from the
  host's static rules, so a hero that lacks a rule the host carries makes the gate fail and **the host
  unit loses that rule entirely** — see #303 for the source lines, the rationale and the blast radius
  (43 supplement rules + 25 core-catalog entries gate this way, and `MostModelsHaveThisRule` shares it).
  This item is the list-building surface for that; #303 is whether the mechanic is right at all.
- 2026-07-30: **Where it goes.** `FdgRaylib/Rendering/ArmyForgeScreen.cs` -> `DrawHeroJoin` (~line 695),
  which draws the "Joins unit" combo and already owns a per-join yellow warning line (~717,
  "! Join target missing or ineligible ... - deploys solo."). The new warning belongs directly under it,
  in the same idiom.
- 2026-07-30: **Nearest existing precedent for the copy.** The import panel already renders
  `RULES NOT ENFORCED BY THE ENGINE (inert in play)` (~line 249, fed by
  `ArmyForgeShareService.InertRules` / `OprListImporter.UnresolvedRuleNames`). Different cause — that is
  "we never implemented it", this is "your own list choice disabled it" — but the same promise to the
  author: this text looks like it does something and won't. Consider whether the two should read alike.
- 2026-07-30: **What it needs that doesn't exist yet.** A predicate the Forge can call on COMPILED list
  entries, with no live game: given a host `UnitFileEntry` and a hero `UnitFileEntry`, which of the
  host's rules are gated all-models (or most-models) and absent from the hero. The engine owns the
  gate but evaluates it against a live `IUnit` mid-dispatch; the Forge has neither. Likely shape: a
  small query beside the condition that reports a rule definition's gating, so the Forge does not
  re-implement (and drift from) the engine's semantics.

## Decisions

- (none yet)

## Notes on scope

- The mirror case is worth deciding at the same time: a hero whose OWN rule is all-models-gated loses
  it on joining a host that lacks it. Same warning, other direction.
- The CLI has the same blind spot (`FdgRaylib/Program.cs` ~line 123 prints the import-time inert list);
  whether the warning is Forge-only or shared with the CLI/army-builder path is open.

## Outcome

(open)
