# 433 — "Enhanced X" family (~60 names replacing the Boost family) - design fork

**Status**: todo (DESIGN FORK: present options + recommendation, get Chris's sign-off, then build)
**Related**: #415, #432 (the base rules Enhanced lifts), #197, #434/#435
**Branch**: `feature/opr-3.6`. Model/effort: Fable 5.1 / xhigh.

## Goal

3.6 replaced every `X Boost` / `X Boost Aura` / `X Boost Buff` with `Enhanced X` (+ Aura / Buff): about 60
names across both systems, each phrased "If this model has X, it gets <better parameter> from X (instead of
only <base parameter>)". Representative 3.6 texts:

- Enhanced Lustbound / Scurry / Agile: "moves +2" on Advance and +4" on Rush/Charge from Lustbound (instead of
  only +1" and +2")".
- Enhanced Changebound / Machine-Fog / Screened / Empyrean Spirit: "enemies attacking them always get -1 to hit
  rolls from Changebound (instead of only when shooting/charging from over 9"/6" away)".
- Enhanced Bloodborn / Clan Warrior / Predator / Primal / Royal Warrior: "extra attacks on successful unmodified
  hit results of 5-6 (instead of only on 6)".
- Enhanced Fortified / Guardian / Ossified / Warden: "hits they take count as having AP(-2) (instead of AP(-1))".
- Enhanced Plaguebound / Protected / Self-Repair / Cursed Undead: "ignore wounds on rolls of 5-6 (instead of 6+)".
- Enhanced Darkborn / Shadowborn / Shrouded / Wild Veil: "-8"/-4" range to a min. of 6" (instead of -4"/-2")".
- Enhanced Highborn / Extra Reach / Royal Legion: "+8"/+4" range (instead of +4"/+2")".
- Enhanced Nimble / Rapid Blink / Wave-Step / Wolfborn: "may move by up to 2D3" (instead of D3")".
- Enhanced Guerrilla / Harassing / Hit & Run: "may move by up to 6" (instead of 3")".
- Enhanced Steadfast / Vale Oath: "stops being Shaken on rolls of 3+ (instead of 4+)".
- Enhanced Mischievous / Scrapper / Vicious / Bestial: "re-roll successful unmodified Defense results of 5-6
  (instead of only 6)".
- Enhanced Knightborn / Warding / Angelic Blessing: "always ignore shooting/charging wounds on 6+ (instead of
  only when shot/charged from over 9"/6")".
- Enhanced Versatile Warrior / Watchborn / Vinci Tech: "always gets either AP(+1) or +1 to hit (instead of only
  over 9"/6")".
- Enhanced X Aura: "This model and its unit get Enhanced X"; Enhanced X Buff: once-per-activation grant to a
  friendly unit within 12" (next time the effect would apply).

Every Enhanced rule is inert unless the bearer (or most models, for unit-gated bases) also has X.

## The fork (write both up with examples from the catalog before choosing)

- **Option A - per-rule redefinition.** Each `Enhanced X` is authored as a full definition that carries the
  upgraded effect and a condition `HasRule(X)`, while X's own definition gets `Not(HasRule("Enhanced X"))`
  so they never stack. ~60 definitions, all data, no engine change; brittle (every base-rule edit must be
  mirrored) but transparent in `--trace-rules`.
- **Option B - additive deltas.** `Enhanced X` is authored as the DELTA on top of X: +1"/+2" more movement,
  an extra trigger on 5, an extra AP(-1), an extra D3", the complementary range condition (within 9"/6") for
  the "always" variants, a 3+ in place of 4+ via a roll-modifier token. Base rules untouched; ~60 small
  definitions; needs two or three new primitives (a roll-threshold modifier for token-clear rolls, a
  second-trigger-value for extra-attack rules); `--trace-rules` shows two firings per event.
- **Option C - parameterised rule arguments.** Give the base rules a hidden "tier" argument the Enhanced
  marker raises (engine reads `Arg(tier)` in each effect). Fewest definitions, most engine work, and the
  tier semantics differ per rule family anyway.

Recommendation to present: B for parameter-bump families (movement, AP, ranges, dice, move distance), A for
the "always instead of range-gated" and "5-6 instead of 6" families where a delta is awkward, C rejected.
Stop here until Chris signs off.

## Acceptance (after sign-off)

Every Enhanced name resolves (`--rule-coverage` 0 for the family), one integration test per family shape
(not per name) plus a no-stack test (base + Enhanced never double-applies), Aura and Buff variants grant.

## Code anchors (survey 2026-10-29)

- The 3.5 `X Boost` definitions in `GdfRuleSupplement.json` / `AofRuleSupplement.json` (the shapes being
  replaced), `Effect.AddRule` / `Effect.Aura` grant plumbing, `RuleEvaluator.CollectGrantedRules` (arity gate
  LAT-1). See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

- The 3.5 `X Boost` / `X Boost Aura` / `X Boost Buff` definitions in both supplements are the shapes being
  replaced. Grant plumbing: `Effect.AddRule` / `Effect.Aura` / `Effect.MarkTarget`, `TokenPayload.RuleGrant`,
  `RuleEvaluator.CollectGrantedRules` (~:436-439 skip path; LAT-1 arity gate screens argumented grants - an
  Enhanced rule that must carry an argument needs that gate revisited).

## Notes

- 2026-10-29 (from #418, its D2): the 3.5 Boost rules' 5-face entries (`UnmodifiedRollEquals(5)` +
  `AddExtraHit(5)` - Primal / Clan Warrior / Devout / Ferocious / Lucky / Royal Warrior Boost, Great
  Sergeant) add an extra hit for ANY natural 5, including a miss (a Fatigued unit's 5s always miss; so do a
  Quality 6+ unit's). #418 stripped only face 6, per the Fatigue text. The 3.6 Enhanced texts say "extra
  attacks on successful unmodified hit results of 5-6", so whichever option this item takes must gate the 5s
  on SUCCESSFUL hits, and should pin a Fatigued Enhanced unit getting nothing from its 5s or 6s. New
  hit-face primitives must read `IHasUnmodifiedHitRolls.SpecialRuleHitRolls`, not the raw roll.
- 2026-10-29: filed with the plan.

## Decisions

(fork pending)

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
