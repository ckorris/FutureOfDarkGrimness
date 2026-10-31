# 204 — Save-roll beats for Rending and non-Rending hit groups play too close together

**Status:** DONE 2026-07-11 (engine change; app pointer bump). Scope expanded on Chris's follow-up:
cover ALL extra-wound sources (Blast, Furious, ...), and show the weapon fired + WHY there are extra wounds.
**Where:** presentation pacing around `RollToSaveStage` (one `DiceRolledBeat` per AP group) /
the beat clock; related umbrella: #056 (presentation beat stream polish)

## Symptom

When an attack contains both Rending (natural-6, per-hit AP) and normal hits, the save flow rolls
each group separately (per #032's per-hit AP split) and emits a `DiceRolledBeat` per group - but
the two beats play nearly on top of each other, so the viewer can't read them as two distinct
save rolls (different thresholds, different dice) before the second one lands.

## Direction

Give consecutive save-roll beats of one attack a minimum spacing (or hold the first group's dice
until the second group's roll presents alongside it, mirroring how the held save-dice already
stay up through wound assignment). Purely presentation - no rules/timing change to the engine
math; verify by eye in the GUI with a Rending weapon vs a mixed volley.

## Notes

- 2026-07-09 — filed verbatim from Chris's report during #200/#203 work.
- 2026-07-09 (second report, same day): re-raised - "the two beats for the dice rolls play too
  close together and it's not clear what happened." The readability harm is confirmed felt in
  real play, not just cosmetic.
- 2026-07-11 - **fixed + expanded.** Owner reframed it around the tabletop model: hits that save with
  the SAME stats are rolled together as one save roll (Blast x3 = roll 6 dice at once; Furious +1 = roll
  4 together), and only hits that save DIFFERENTLY (Rending's per-hit AP) get a separate, spaced roll. Also
  wanted the weapon named and the extra-hit arithmetic shown. Signed off: name the specific rule (thread it
  through the injection sink); cover shooting + melee.

## Outcome

Engine change (submodule) + superproject pointer bump, owner-authorized for this item. Shooting AND melee
(they share `RollToSaveStage`).

**Grouping (the core fix):** `RollToSaveStage` now BATCHES its save-roll beats by save THRESHOLD. The base
group + Furious's on-6 extras + Blast's multiplier overflow all save at the same threshold, so they show as
ONE roll; Rending/Crack's per-hit-AP hits save at a raised threshold, so they show as their own roll. The
dice ROLLING is untouched (each hit group still rolled separately, same RNG draws, same per-group results) -
only the beat presentation is regrouped, so it is **outcome-neutral** (verified: identical DOP-1 benchmark
hash `27B25CFA870A82B6` with and without the change; the DOP-16 difference that first looked like drift was
just #210's known concurrency nondeterminism).

**Why-extra text:** each beat is captioned `{weapon}: {breakdown}` -
`Agony Whip: 2 hits x3 (Blast) = 6`, `Chainsword: 3 hits +1 (Furious) = 4`,
`Autocannon: (2 +1 Furious) x3 (Blast) = 9`, `Power Sword: 2 hits, Rending AP+1`. Provenance is threaded
without changing the hit-group structure: a new `HitGroupSource` tag on `SuccessfulHitInfo` /
`PendingSaveRolls`; `RuleEvaluator.EvaluateAllNamedLive` (a LIVE named evaluation - spends grants/narrates
like `EvaluateAll` but returns `(op, ruleName)` pairs) gives the alias-aware rule names; `PerHitApSplitter`
tags the base/Rending groups; `RollToHitStage` tags the pooled-extra (`ExtraHitRuleNames`) and Blast groups.
The pooled extra-hit group is kept as ONE group exactly as before (not split per rule) so the structure - and
thus the RNG - is byte-identical.

**Tests:** `SaveBeatGroupingTests` (5: same-threshold merge with Blast/Furious/nested arithmetic, Rending
2-beat split, plain volley); `SaveBeatOnWhiffTests` updated for the new label; end-to-end source tags in
`BlastRuleIntegrationTests` and `RendingRuleIntegrationTests`. Engine 1603/0, app 325/0. Outcome-neutrality
bench + headless smoke clean.

**Still visual-verify (Chris, in the GUI):** the beats are now batched and captioned, but the exact on-screen
SPACING / held-dice overlap for the 2-roll Rending case is a front-end rendering concern I can't check
headlessly - eyeball a Rending weapon vs a mixed volley. If the two held rolls still crowd, that's a small
GUI-side spacing tweak on top of this.

Engine commit: `9e5fb4a`; superproject pointer bump: this commit.
