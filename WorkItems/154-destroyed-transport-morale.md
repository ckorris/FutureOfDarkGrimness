# 154 — Destroyed-transport morale test

**Status**: Closed 2026-07-03 — not a gap; won't implement (decided with the user).
**Related**: #035 (Transport — slice E spillout), #009 (non-melee morale = Shaken on fail), #091 (morale core)

## Goal (as originally scoped)
When a Transport is destroyed, have its spilled-out occupants take a **morale test** (Shaken on fail;
not melee, so never Rout) — treating "being in an exploding transport" as a morale-test trigger alongside
shooting/melee, and distinct from the dangerous-terrain *damage* on destruction (which deals wounds but
never itself triggers morale). Surfaced 2026-07-02 while auditing morale triggers.

## Decision — closed without implementation
Reviewed against the code before building. The transport rule as quoted in the #035 detail file (GF v3.5.1):

> When a transport is destroyed, units inside must take a dangerous terrain test, **are Shaken**, and must
> be placed fully within 6" of the transport before it's removed.

"are Shaken" is **unconditional** — occupants are always Shaken, no test. That is already implemented: #035
slice E's `TransportUtilities.ApplySpilloutEffects` (`Rules/Dispatch/TransportUtilities.cs`) adds the
`Shaken` token directly to every spilled occupant (alongside the un-embark and per-model dangerous-terrain
test), and `SpilloutOccupantsStage` drives it after `ApplyWoundsStage` in both the shooting and melee
pipelines.

A morale **test** (Shaken only on fail) can't coherently be layered on top of this:
- The test's only failure outcome is Shaken, and the occupant is already Shaken — so the test can never
  change the result.
- `MoraleUtilities.TakeMoraleTest` auto-fails a unit that already holds the `Shaken` token, so the test
  would always fail anyway.

So #154 would have to **replace** the guaranteed Shaken with a probabilistic one — a rules change that
diverges from the quoted rule text. The user chose to keep the auto-Shaken as authoritative.

**No code change.** The behavior #154 describes (occupants become Shaken on destruction) is already the
shipped behavior; only the *mechanism* differs, and the guaranteed-Shaken mechanism is the correct one.

## If this is ever revisited
The morale-test variant would be small but is a deliberate rules divergence: drop the unconditional Shaken
line from `ApplySpilloutEffects`, and in `SpilloutOccupantsStage.SpillOccupants` run
`MoraleUtilities.TakeMoraleTest(GameContext, occupant, HeroStatRules.GetMoraleQuality(occupant))` per
occupant (before any Shaken is applied), calling `ApplyShakenWithPresentation` only on failure — mirroring
`CastSpellStage.ResolveConditionalSpell` (the #034 `Effect.MoraleTestThen` path). Re-open only on a
deliberate decision to change the rule.
