# 183 — Hero-join Subject-seat rule attribution

**Status**: DONE 2026-07-08 (Option C, slices 1-3 shipped; engine + superproject committed, local)

## Outcome
**DONE 2026-07-08.** Closed audit item 17 (§3 + §8), both directions of the attribution gap, with one
mechanism. **Slice 1** (engine `b271541`): `Condition.AllModelsHaveThisRule` added to all 15 Subject-seat
entries across the 12 unit-scoped defensive rules (Evasive, Melee Evasion, Artillery, Aircraft x2,
Resistance x2, Protected, Shielded, Fortified, Ranged Shrouding, Darkborn-Defensive x2, Melee Shrouding,
Counter-Attack), plus a `RuleValidator` check that enforces the gate for any unit-scoped Subject-seat
defensive rule (flows through army-load, supplement validate/apply, OPR import, and the catalog/supplement
fire-lint) so the catalog is self-tested clean and future ungated rules are rejected at load. **Slice 2**
(engine `c926946`): every Subject-seat dispatch site threads the defender's living models
(`HeroStatRules.LivingModels`, AnyOwner), so a joined hero's relocated defensive rule is collected,
evaluated, and #163-traceable instead of silently vanishing; the gate governs whether it applies (only
when every living model has it — the sole-survivor case fires, matching last-model-Defense). Non-hero units
unaffected. **Slice 3**: this close-out (audit cross-off, ledger, archive). Also shipped ahead of the
slices (engine `4c2b86a`): the grants-cover-the-joined-hero gate fix and Resistance's spell facet (2+ vs
spells). Verify at each slice: engine green (1302 -> 1308), full build clean, headless smoke exit 0.
Deferred, filed separately: weapon-scoped Counter strike sequencing (**#184**); Fear/Fearless asymmetry
stays audit item 24 / **#175** (Fear is Actor-seat morale, needs a rulebook check — NOT closed by this
item). Follow-up cleanup surfaced during implementation: the evaluator's participant tuples want a
`RuleParticipant` struct (**#185**).

**Status (historical)**: open — plan written 2026-07-08, **awaiting design sign-off** (options + recommendation below)
**Related**: #006 (Hero merge), #093 (per-model dispatch — built the primitives this plan reuses), #175 (Fear/Fearless rulebook check — same ruling principle), #166a (fire-lint), #163 (rule trace), Audit-2026-07-06 §3 + §8 Bug 17 (this item) and Bug 24 (the host-side asymmetry this plan also fixes)
**All work is engine-side (submodule)** — authorized by this item once signed off.

## Problem

`HeroJoinResolver.Apply` relocates every non-Hero unit-scoped rule from a joining hero onto the hero
*model* (`FutureOfDarkGrimness/Rules/Dispatch/HeroJoinResolver.cs:109-117`). Per-model dispatch
(#093's `EModelRuleScope` machinery) is opt-in per call site and was only ever wired at **Actor-seat**
sites (hit batches, offers, movement, caster grants). Every **Subject-seat** (defensive) call site
passes `models: null`, so a hero-carried defensive rule is never collected after the join — it
silently stops existing. Three rules dodge this because their unit-level copies are gated by the
self-referential `Condition.AllModelsHaveThisRule` (Stealth / Fearless / Regeneration, #093 slice 2);
the other Subject-seat rules have no gate and no model visibility.

Two distinct defects, symmetric directions of the same attribution gap:

- **Hero-side (audit Bug 17, the headline):** hero carries e.g. Resistance, host doesn't → the rule
  is relocated onto the hero model and never evaluated again. Silent. (For *unit-targeted* rules the
  rulebook outcome — no effect unless all models have it — is coincidentally right, but it's right by
  omission: untraceable, unlintable, and wrong for the sole-survivor case, see below.)
- **Host-side (audit Bug 24's class):** host carries e.g. Evasive, joined hero doesn't → the unit
  keeps the full -1-to-hit including the hero, because only the 3 special-cased rules check
  all-models. Rulebook says the unit should lose it.

## Facts established (2026-07-08 code read)

**Subject-seat rule inventory** (unit-scoped unless noted; from `CoreRuleCatalog.cs`):

| Rule | Subject hook(s) | Effect | All-models gated? |
|---|---|---|---|
| Stealth | HitRollModifier | hit -1 beyond 9" | YES |
| Regeneration | SaveRollComplete | ignore wound 5+ | YES |
| (Fearless) | MoraleTestComplete (Actor seat) | morale reroll | YES |
| Evasive | HitRollModifier | hit -1 | no |
| Melee Evasion | HitRollModifier (IsMelee) | hit -1 | no |
| Artillery | HitRollModifier | hit -2 vs it | no (single-model units in practice) |
| Aircraft | RangeCheck, HitRollModifier | -12" range, hit -1 | no (single-model by rule) |
| Resistance | SaveRollComplete | ignore wound 6+ | no |
| Protected | SaveRollComplete | ignore wound 6+ | no |
| Shielded | HitRollComplete (IsNotSpell) | save +1 | no |
| Fortified | HitRollComplete | AP -1 | no |
| Ranged Shrouding | RangeCheck | -6" (min 6) | no |
| Darkborn (Defensive) | RangeCheck, ChargeDeclared | -4" range, charge -2" | no |
| Melee Shrouding | ChargeDeclared | charge -3" | no |
| Counter-Attack | CounterTrigger | StrikeFirst | no |
| Counter | CounterTrigger + ChargeContact | StrikeFirst + impact dice -1/model | **weapon-scoped** — rides the hero's weapon, already survives the merge |

The GDF rule supplement (`GdfRuleSupplement.json`) has no Subject-seat entries of its own; "Stealth
Buff" grants Stealth, which is gated.

**Subject-seat dispatch sites** (all pass `models: null` today):

1. `RangeRuleQueries.cs:32` — Shooting_OnRangeCheck (Aircraft, Shroudings, Darkborn)
2. `DetermineHitRollStage.cs:47` — Shooting_OnHitRollModifier (Stealth, Evasive, Melee Evasion, Artillery, Aircraft)
3. `RollToHitStage.cs:91` — Shooting_OnHitRollComplete (Shielded, Fortified)
4. `AssignWoundsStage.cs:40-43` — Shooting_OnSaveRollComplete (Regeneration, Resistance, Protected, Bane's Actor side)
5. `MovementRuleQueries.cs:107` — Movement_OnChargeDeclared (Darkborn, Melee Shrouding)
6. `DetermineStrikeOrderStage.cs:37` (via `SubjectWithMeleeWeapons`) — Melee_OnCounterTrigger
7. `ResolveImpactHitsStage.cs:49` (same helper) — Melee_OnChargeContact
8. `ResolveSpellDamageStage.cs:101` — spell damage hook
9. `UnitDestructionNotifier.cs:44` — unit-destroyed (special: unit is dead; "living models" is empty)

**Load-bearing constraints:**

- The wound-ignore effects (`IgnoreWoundOnRoll`) are folded against the **pooled** wound total in
  `AssignWoundsStage` *before* wound assignment — the model taking each wound is unknowable at that
  point. True per-model Regeneration ("hero regenerates wounds assigned to the hero") requires the
  per-model wound-attribution restructure the stage already tags as a deferred TODO. Out of scope.
- `CollectTagged` always collects **unit-level** rules regardless of model scope; the `models` param
  only adds model-list rules (AnyOwner = union, AllOwners = intersection). So model-aware dispatch
  alone can NEVER fix the host-side direction — only a condition on the unit-level rule can.
- Only joined-hero models ever have rules on `IModel.RuleDefinitions` today (the merge is the sole
  writer outside tests). So adding model visibility at Subject sites changes behavior **only for
  hero-joined units** — zero risk to ordinary units.
- Dedup is per-unit for argless rules: a rule present both on the unit and the hero model fires once.
- Wounds-last (#006 slice B) means the hero mostly takes wounds when it's the sole survivor — so
  "hero's defensive wound rule active only when all living models have it" degenerates, for a
  sole-surviving hero, to exactly the hero's own rule. This mirrors the deliberate
  `HeroStatRules.GetSaveDefense` last-model-Defense design.

## Options

### A — Selective relocation (audit's first suggestion)
`HeroJoinResolver` skips relocating any rule with a Subject-seat hook entry; warns on the drop.
- **For:** smallest change (~half day); turns silent loss into a warned, documented one.
- **Against:** granularity is per-rule, not per-entry — a mixed-seat rule would lose its Actor
  entries too; the host-side asymmetry (Bug 24 class) is untouched; the sole-survivor case is lost
  (a lone hero with Regeneration should regenerate); still invisible to the #163 trace; walks away
  from #093's "per-model as the general model" direction.

### B — Generalize Subject-seat dispatch with AllOwners (audit's second suggestion)
Thread the defender's living models through sites 1-9 with `EModelRuleScope.AllOwners`.
- **For:** leak-proof without touching rule data (an ungated hero rule can't fire unit-wide because
  the intersection excludes it); sole-survivor case works (intersection of one model = its rules).
- **Against:** hero-carried rules stay invisible while grunts live (not collected → no trace line —
  the exact silence #163 was built to kill); the host-side direction still needs the all-models gate
  anyway, so B alone is half a fix; two mechanisms ("intersection" + "gate") continue to coexist with
  nothing telling a future author which to use — the audit's explicit complaint.

### C — Gate-centric: AnyOwner visibility + universal all-models gate + validator enforcement (RECOMMENDED)
Make `Condition.AllModelsHaveThisRule` **the** single semantic mechanism for unit-targeted defensive
rules, and use dispatch purely for visibility:

1. Add `AllModelsHaveThisRule` (And-composed with existing conditions) to every ungated unit-scoped
   Subject-seat rule in the table above (12 rules).
2. Pass the defender's living models at sites 1-8 with `AnyOwner` (site 9 passes all models — none
   are living). Relocation in `HeroJoinResolver` stays unconditional and simple.
3. New `RuleValidator` check: *a unit-scoped rule with a Subject-seat entry at an
   attack-interaction hook must include `AllModelsHaveThisRule`* (small allowlist for deliberate
   exemptions). This is load-bearing, not cosmetic: under AnyOwner an ungated rule on one model
   would fire unit-wide, so the validator is what makes the design durable against future authoring
   (catalog and supplement both flow through it).

Behavior after C, all four directions:
- Hero has it, host doesn't, grunts alive → collected, gate fails → **no effect, traced** as
  `condition ... AllModelsHaveThisRule ... not met` (vs. silent nothing today).
- Hero has it, sole survivor → gate passes over the one living model → **fires** (rulebook-faithful;
  matches the last-model Defense philosophy).
- Host has it, hero doesn't → unit-level copy's gate fails → **unit loses it** (fixes the Bug-24
  asymmetry; matches the Stealth/Fearless/Regeneration ruling already shipped in #093 slice 2).
- Both have it / homogeneous unit → gate passes, per-unit dedup keeps it to one firing → unchanged.

- **For:** one mechanism, both directions correct, every non-firing visible to the #163 trace and
  provable by the #166a lint; reuses existing primitives end-to-end (no new dispatch machinery).
- **Against:** touches 12 catalog definitions (each needs a rulebook sanity-check); depends on the
  validator to stay safe; the wound-ignore class remains a pooled approximation (below).

## Rulebook confirmation (2026-07-08, from the OPR corpus reference `GDF Armies/Special Rules and
Spells by Army.md`)

The official texts settle the classification directly — no FAQ inference needed. **Explicitly
"units where all models have this rule"**: Evasive, Melee Evasion, Darkborn (Defensive, both
facets), Fortified, Protected, Resistance, Shielded, Ranged Shrouding, Melee Shrouding. Artillery
and Aircraft are single-model-unit rules (gate trivially true). The one outlier is
**Counter-Attack: "Strikes first when charged."** — no all-models qualifier. Ruling (recommended,
pending user confirmation): gate it anyway — strike order is indivisible per unit, the ungated
per-model reading is exactly the #184 interleave problem, and nearly all occurrences arrive via
Counter-Attack Aura ("this model and its unit get Counter-Attack"), which grants unit-wide and
passes the gate regardless.

**Wound-ignore rules are RAW-pooled, not an approximation:** official Resistance/Protected text is
"When a unit where all models have this rule takes wounds, roll one die for each" — unit-level pool,
all-models gated. So slice 1's gate makes `AssignWoundsStage`'s existing pooled ignore roll exactly
faithful; the per-model wound-attribution restructure this plan previously flagged as a deferred
approximation is NOT needed for RAW. (Even the mid-attack edge is faithful: an attack that kills the
grunts and spills into the hero was resolved while the unit didn't qualify — RAW gives those wounds
no roll either.)

## Discovered corners — BOTH FIXED 2026-07-08 (user-approved, shipped ahead of the slices)

- **Joined hero vs unit-held grants** (latent bug in the shipped gate, surfaced by the Counter-Attack
  Aura reading): `Condition.AllModelsHaveThisRule` counted a joined hero's OWN model rules only, so a
  hero bringing its own aura (or a buff cast on the combined unit) broke the gated rule for everyone.
  **FIXED**: the gate now splits static vs granted — unit-held RuleGrant tokens count for EVERY living
  model including the joined hero (grants target the current combined unit); only the host's static
  rules stay excluded from the hero. Tests: `AllModelsRuleGateIntegrationTests` grant-covers-hero +
  pure-grant (aura-hero, resolver-collected) cases.
- **Resistance's spell facet** ("If the wounds were from a spell, then they are ignored on a 2+
  instead") was missing — engine had only the flat 6+. **FIXED**: new `Condition.IsSpell` (positive
  twin of IsNotSpell), `SaveRollCompleteContext`/`ICombatMetadata` carry `IsSpell` (set by
  `ResolveSpellDamageStage`, threaded by `AssignWoundsStage`), Resistance gains an IsSpell-gated
  `IgnoreWoundOnRoll(2)` second entry (the `WoundIgnoreSink` best-threshold fold yields "2+ instead"),
  lint context variants extended. Tests: `WoundRuleIntegrationTests` (6+ vs 2+ thresholds,
  probabilistic), `CasterRuleIntegrationTests` end-to-end (Resistance survives the spell that kills
  the Shielded control).
  Note for slice 1: Resistance now has TWO Subject-seat entries — the gate goes on both.

## Deliberate deferrals (recorded, not silently cut)

- **Weapon-scoped Counter hero edge → filed as #184** (`WorkItems/184-counter-strike-sequencing.md`):
  a hero whose melee weapon carries Counter drags the whole host unit's strikes ahead of the charger
  (and over-reduces Impact dice). Pre-existing #030 simplification, exact for homogeneous units; not
  made worse by this plan. Needs its own melee-flow design pass.
- **Fear** is a morale-side (Actor-seat) cousin — same ruling principle, but it stays #175.

## Plan (each slice: implement -> integration test -> verify -> commit -> ledger)

**Slice 1 — gates + validator (the host-side fix).**
Add the gate to the 12 rules; add the `RuleValidator` Subject-seat check + validator tests; extend
`AllModelsRuleGateIntegrationTests` with one host-has/hero-lacks case per effect class (hit-mod
Evasive, wound-ignore Protected, save-mod Shielded, range-mod Ranged Shrouding, charge-mod Melee
Shrouding, strike-first Counter-Attack). Fire-lint must stay green (native models satisfy the gate;
`RuleFireLint` already lints gated Stealth successfully). Homogeneous units: no behavior change.
Also the grant fix from Discovered corners: the gate counts unit-held grants for the joined hero too
(aura-hero and buff-spell tests).

**Slice 2 — Subject-seat model visibility (the hero-side fix).**
Thread living models (AnyOwner) at sites 1-8; site 9 gets the full model list; update the
`HeroJoinResolver` doc comment. Integration tests through real stages, mirroring
`HeroPerModelRuleIntegrationTests`: hero-carried Evasive dormant while grunts live **and the trace
line proves it was evaluated** (CapturingOutput, as in `RuleTraceTests`); sole-survivor hero
Regeneration/Resistance fires; both-carry rule fires once (dedup). Confirm zero diff for non-hero
units (only hero models carry model rules).

**Slice 3 — close-out.** EngineNotes known-stubs update, audit Bug 17/24 cross-off, archive.

Estimated size: slice 1 is mostly data + one validator rule; slice 2 is ~9 one-line site changes +
tests. The risk is concentrated in test design, not mechanism — all primitives exist.

## Sign-off state — COMPLETE 2026-07-08: ready to implement (slices 1-3)

1. All-models ruling incl. Counter-Attack gate: **confirmed** (official text for all but
   Counter-Attack; Counter-Attack gate approved as the conservative engine-consistent ruling — user
   notes the "where all models have this rule" phrasing was added to OPR text over time to
   disambiguate exactly this confusion and may not have landed everywhere, supporting the uniform gate).
2. Option C: **signed off.**
3. Weapon-scoped Counter edge: **deferred, filed as #184.**
4. Wound-ignore pooling: RAW-faithful once gated — nothing deferred.
5. Grants-count-for-the-joined-hero ruling: **approved and shipped** (see Discovered corners).

## Notes

- 2026-07-08 (slice 2 DONE, engine commit): **Subject-seat model visibility (the hero-side fix).**
  Threaded the defender's living models (`HeroStatRules.LivingModels`, the Subject counterpart to
  `LivingWeaponBatchOwners`) with `EModelRuleScope.AnyOwner` at all 8 live Subject dispatch sites -
  `RangeRuleQueries`, `DetermineHitRollStage`, `RollToHitStage`, `AssignWoundsStage`,
  `MovementRuleQueries`, `DetermineStrikeOrderStage`+`ResolveImpactHitsStage` (via the shared
  `SubjectWithMeleeWeapons`, widened to 5-tuples with models on the weaponless participant), and
  `ResolveSpellDamageStage`; site 9 (`UnitDestructionNotifier`, unit already dead) passes the full
  model list. So a joined hero's relocated defensive rule is now COLLECTED and evaluated at the Subject
  seat (its all-models gate then governs whether it applies), instead of silently vanishing. Non-hero
  units are unaffected (no model carries per-model rules -> the union adds nothing). `HeroJoinResolver`
  doc comment updated. Tests: new `HeroSubjectRuleIntegrationTests` (6) - through the real
  `RollToHitStage` reading the folded `SaveModifier` from a hero-carried Shielded: baseline non-hero,
  dormant-while-grunts-live, **fires-when-sole-survivor (the wiring proof - impossible under
  models:null)**, host-has/hero-lacks suppressed, both-carry fires once (dedup); plus a trace test
  (direct evaluator + capturing output) proving the relocated rule is narrated by the #163 trace
  ("condition AllModelsHaveThisRule not met") rather than silently dropped. Verify: engine **1308/1308**,
  full build clean, headless smoke exit 0. Remaining: slice 3 (close-out).
- 2026-07-08 (slice 1 DONE, engine commit): **gates + validator (the host-side fix).** Added
  `Condition.AllModelsHaveThisRule` to all 15 Subject-seat entries across the 12 unit-scoped defensive
  rules (Evasive, Melee Evasion, Artillery-Subject, Aircraft x2, Resistance x2, Protected, Shielded,
  Fortified, Ranged Shrouding, Darkborn-Defensive x2, Melee Shrouding, Counter-Attack); bare `Always`
  became the bare gate, real conditions became `And(existing, gate)`. New `RuleValidator` check
  (`CheckAllModelsGate`): a unit-scoped rule with a Subject-seat entry at a defensive attack hook must
  gate on `AllModelsHaveThisRule` (conjunctive-position check; weapon-scoped Counter exempt via the
  scope filter; Actor-seat buffs exempt). The check flows through army-load, supplement validate/apply,
  OPR import, AND the catalog/supplement fire-lint (all call `RuleValidator.Validate`), so the catalog
  is self-tested clean and any future ungated Subject rule is rejected at load. `RuleViolation`
  generalized to carry an optional `Detail` (capability violations keep `MissingCapability`; the gate
  violation sets `Detail`) with a `Describe()` renderer; the 3 formatters route through it. Verified no
  OPR-synthesized rule (all Actor-seat) or supplement rule (zero Subject entries) is newly rejected.
  Tests: `RuleValidatorTests` +3 (ungated-flagged, weapon-exempt, actor-exempt) and 2 fixtures
  re-gated; `EmbeddedRuleValidationTests` fixture re-gated; `AllModelsRuleGateIntegrationTests` +6
  (one per effect class: hit-mod/wound-ignore/save-mod/range-mod/charge-mod/strike-first, each proving
  homogeneous-fires + hero-lacks-suppressed). Stale "approximated as unit-level (#093)" scope comments
  on the Shrouding/Darkborn rules updated to name the gate. Verify: engine **1302/1302**, full build
  clean, headless smoke exit 0. Remaining: slice 2 (hero-side model visibility), slice 3 (close-out).
- 2026-07-08 (latest): Full sign-off received; the two discovered corners were fixed immediately at
  the user's request (engine commit — gate/grants fix + Resistance spell facet, 5 new tests; suite
  1293/1293, build clean, headless smoke exit 0). Slices 1-3 remain for implementation (planned on
  Opus with this file as the spec).
- 2026-07-08 (later): Sign-off discussion — user chose Option C. Rulebook texts pulled from the OPR
  corpus reference confirmed the all-models phrasing class-wide; Counter-Attack lacks the qualifier
  (ruling recommended above); wound-ignore pooling confirmed RAW-faithful; discovered the
  joined-hero-vs-grants gate bug and the missing Resistance spell facet; filed #184 for the Counter
  sequencing deviation.
- 2026-07-08: Item opened; code read done (rule inventory, site inventory, CollectTagged semantics,
  AssignWoundsStage pooling constraint); plan + options written. Awaiting user sign-off. Implementation
  planned to run on Opus with this file as the spec.
