# 316 — Limited is not enforced in melee at all

**Status**: in-progress (implemented + tested + CLI hand-verified; awaiting GUI hand-verify)
**Related**: #032 (Limited: marker rule + per-model spent token, shooting only), #315 (the shooting
opt-out this mirrors), #028 (Deadly-first gating in melee, the gate hold-back had to release)

## Goal
A Limited melee weapon may only be used once per game, and a player may decline to use it — the same
two properties #032 and #315 gave the shooting path.

## Notes

- 2026-08-02: **Implemented. Engine 2587/0, app 889/0, full build clean, default headless smoke exit 0,
  and every path hand-run in the CLI via `Scenarios/limited-weapons-test.json` (Bomb Wardens: swing the
  bomb and see it spent; hold it back and be refused the blade's hold-back; swing the blade then hold
  back the bomb and get the confirmation).**

  **Enforcement** (`ChooseMeleeWeaponStage`): a spent weapon moves to `invalidOptions` with
  "Already used this game (Limited)."; choosing one calls `MarkFired` and logs the spend, mirroring
  `ChooseRangedAttackStage`.

  **Per-model scope.** `LimitedRules.IsSpent`/`MarkFired` gained model-subset overloads and melee passes
  `InRangeAttackingModels`. Melee only lets models within melee range swing, so a carrier standing three
  inches back must not have its charge burned — and since `IsSpent` asks whether EVERY living carrier has
  used it, marking that model would also have retired the weapon for the whole unit a melee early.

  **Hold back.** `Enter` loops like #315's; each weapon that can swing this pass gets a
  "Hold back: <label>" row in the same `StringSelectionRequest` (no new request type — the label is
  already the option's identity on this wire, and the stage maps the reply back through its own
  (label, weapon, holdBack) list, so nothing is parsed out of a string). Declining calls
  `context.DeclineWeapon`, so a declined Deadly+Limited weapon stops gating the unit's ordinary ones.
  New `OnNoWeaponsLeftToSwing` binding, bound in BOTH graphs that host this stage (`MeleeStage` ->
  offer-strike-back, `StrikeBackStage` -> finished) — the same exits `DetermineCanKeepSwinging` uses.

  **Tests.** New `MeleeLimitedTests` (9): spend on choose; spent weapon offered as unavailable; only
  in-range models spend; hold back leaves it unspent and re-offers the rest; hold back a Deadly+Limited
  weapon unlocks the ordinary ones; the last-weapon hold-back is refused with nothing swung; the
  attack-ending hold-back confirms and routes on; declining the confirmation re-offers; all-spent routes
  on without asking. Four existing melee tests updated for the extra rows (expectation-only).

## Decisions

- **At least one weapon must attack after charging** (user sign-off). Unlike a shoot — which can be
  backed out of entirely because nothing has happened yet — the charge move has already been made and
  cannot be rewound, so a unit may not decline its way into charging in and doing nothing. The last
  un-declined weapon's hold-back row is offered as UNAVAILABLE with that reason rather than hidden. It
  costs nothing in practice: to keep a Limited weapon you hold it back and swing something else.
- **The attack-ending hold-back confirms** (user sign-off), via a `YesNoRequest` naming the weapon and
  what it keeps — the melee analogue of #315's "Done shooting" confirmation. Per-weapon holds with swings
  still to come stay silent, exactly as Hold fire does. `defaultAnswer: true`, since the player had to
  pick the hold-back row to get there.
- **`HOLD_BACK_PREFIX` / `IsHoldBackChoice` are public, and `AiStringSelectionResolver` skips hold-back
  rows explicitly.** The AI's catch-all for this menu is `ValidOptions[0]`; hold-backs sort last, but
  relying on sort order for that is precisely the trap `AiStringSelectionResolverTests` already pins for
  the Ambush prompt ("Hold in Ambush" listed first). The AI has no policy for spending a once-per-game
  weapon well and declining is strictly worse for it, so it never picks one.
- **All-weapons-spent routes on without a request.** Otherwise the menu would have zero valid options —
  an all-disabled list for a human, and a throw for the AI's `ValidOptions[0]`.

## Outcome
(pending — GUI hand-verify)

## Follow-ups not taken here
- **The Charge action gate does not know about spent Limited weapons.** Shooting grays out Shoot when the
  only ranged weapon is a spent Limited one (#032); `ChooseActionStage.GetCanCharge` only checks melee
  range, so a unit whose ONLY melee weapon is spent can still charge and then swing nothing (it now
  routes cleanly and logs "has no melee weapon left to use", rather than faulting). No shipped book has
  such a unit — all five Limited melee weapons sit on units with ordinary melee too — so this is left as
  a known asymmetry rather than fixed blind.
- **AI never holds back**, in melee or shooting. An AI-policy question, not a rules one.
