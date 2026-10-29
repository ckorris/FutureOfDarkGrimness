# 423 — Parry(X): new melee hit-ignore step with a defender pick

**Status**: todo
**Related**: #415, #042 (rules architecture), #286 (assign-wounds dialog, nearest GUI pattern), #435 (AoF hand weapons carry Parry), #442
**Branch**: `feature/opr-3.6`. Model/effort: Fable 5.1 / xhigh. Read `docs/ResolverGuide.md` before touching resolvers.

## Goal

Core Rules v3.6.0, Parry(X): "When this unit takes hits in melee, roll X dice per model with this rule in it,
up to as many dice as hits taken. For each 6+ one hit is ignored (defender picks which)." In the books it is
a weapon rule (AoF Hand Weapons: 252 references across 35 books; GDF 12 references in the four Daemons
books), so "model with this rule" = a model carrying a Parry weapon. It is new in 3.6.

"Done" = after the attacker's hits are known and before saves, a Parry step rolls min(X x parry models, hits)
dice, and for each 6+ the defender removes one hit of their choice - the choice matters only when hits differ
(AP, Deadly, Rending-6 hits, Blast groups), so the engine offers a pick only when the incoming hits are not
all equivalent and otherwise auto-resolves; works for strike-backs and Counter strikes alike (any hits the
unit takes in melee); not against Impact hits? - Impact hits are hits taken in melee during a charge: the
text says "when this unit takes hits in melee", so Impact hits ARE parryable; record the ruling in Decisions.

## What changes

1. Engine: a `ParryStage` (or a sink in the existing hit->save pipeline) between hit resolution and
   `RollSaves`, Subject seat, per-model count from weapon-scoped Parry carriers alive at that moment; a
   `ChooseParriedHitsRequest` (new `IStageTaskRequest`) only when a pick is meaningful; EOF default = ignore
   the most dangerous hits (highest AP, then Deadly). Catalog entry `CoreRuleCatalog.Parry` with
   `engineArgumentCount 1`, weapon AND unit scope.
2. Resolvers: CLI resolver (numbered list of hit groups, default = best); GUI resolver (a small panel listing
   hit groups with counts, click to allocate ignores, mirrors the assign-wounds dialog); AI resolver = the
   EOF default. Register in both `ResolverRegistryFactory` builds; add the request to the network message
   catalogue (serialization test like `RequestSystemTests`).
3. Tests: 5 models with Parry(1) vs 8 hits -> 5 dice; 2 models vs 1 hit -> 1 die; 6+ removes a hit; the pick
   request appears only for mixed hits; strike-back path; histogram mode run.
4. Presentation: a dice beat "Parry" with the ignored count; combat calculator (#442) expected-hit reduction.

## Acceptance

Tests green; network round-trip test for the new request green; hand-verify (Chris): an AoF infantry unit
with hand weapons parries a charge in the GUI.

## Code anchors (survey 2026-10-29)

- Melee pipeline: `MeleeStage` -> `RollToHitStage` -> `RollSaves`/`ApplyWoundsStage`; `WoundIgnoreSink`
  (nearest existing "ignore" sink, but post-save); `HitInjectionSink`; `docs/ResolverGuide.md` resolver
  inventory. See the appended survey rows below for exact lines.

### Survey rows (code survey 2026-10-29)

- Parry is absent everywhere (engine, supplements, books). Pipeline order: see #422's survey rows; hits are
  grouped by AP in `MainUnitActionStage/PerHitApSplitter.cs:47-51` (the natural granularity for "defender
  picks which"). Sinks to model on: `Rules/Dispatch/WoundIgnoreSink.cs` (post-save ignore), `HitInjectionSink.cs`,
  `HitMultiplierSink.cs`. Request/resolver pattern: `docs/ResolverGuide.md`, `ResolverRegistryFactory`,
  `Tests/RequestSystemTests.cs` (serialization).

## Notes

- 2026-10-29: filed with the plan.

## Decisions

(none yet)

## Outcome

(open)

## Rules for the agent (plan section 1, restated)

- Branch `feature/opr-3.6` in BOTH repos; check `git branch --show-current` in each before committing.
  Never commit, merge or push `master`. Submodule-first commit cadence; subjects start with `NNN:`.
- 3.6 rule text: `~/Projects/GDF Armies/Rules 3.6.0/` (core PDFs' text extracts, common-rules JSON) and the
  3.6.0 book snapshots. Never commit anything from that folder. No 3.5 fallbacks or version flags.
- Vertical slice: engine change + CLI resolver + GUI resolver/overlay + AI resolver legality + an
  integration test mirroring the nearest `*RuleIntegrationTests` + the `docs/` line if a number changed.
- Verify per CLAUDE.md (engine suite green, full build, headless smoke exit 0) and
  `FdgRaylib --rule-coverage FdgRaylib/Assets/Books` not regressing. Never commit red.
- Keep this file's Notes/Decisions current and flip the row in `docs/opr-3.6-upgrade-plan.md` section 5.
  A design fork stops for Chris's sign-off with options + a recommendation written here.
