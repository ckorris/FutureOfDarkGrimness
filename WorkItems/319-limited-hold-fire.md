# 315 — Limited weapons were mandatory: Hold fire / Done shooting + once-per-game visibility

**Status**: in-progress (implemented + tested + CLI hand-verified; awaiting GUI hand-verify)
**Related**: #032 (Limited shipped as a marker rule + spent token), #308 (the engine took ownership of
"may the player back out?"), #028/#314 (resolve-first gating, the reason hold-fire had to be
per-weapon), #316 (melee never enforces Limited at all — split out, not fixed here)

## Goal
A player may decline to fire a weapon, so a once-per-game Limited weapon is never spent against their
will; and the once-per-game state is visible at the moment of the decision, in both front ends.

## Notes

- 2026-08-02: **Implemented. Engine 2578/0, full build clean, default headless smoke exit 0, and the
  new `Scenarios/limited-hold-fire.json` hand-run through every new CLI path.**

  **The defect.** Limited itself was enforced correctly (spent-token gating, #032), but nothing let a
  player NOT fire. The shoot loop (`DetermineCanKeepShootingStage` -> `ChooseRangedAttackStage`) re-offers
  every remaining weapon while `AvailableWeapons.Count > 0`, and the only exit — Back — was offered only
  while `AlreadyUsedWeapons.Count == 0` (#308). So: rifle + Limited rocket, fire the rifle, and the next
  pass offers the rocket alone with no exit. The player was forced to burn a once-per-game weapon. Back
  also abandoned the WHOLE shoot action, so even before firing there was no way to shoot one weapon and
  keep another.

  **Engine.**
  - `RangedAttackChoice.TargetUnit` is now nullable; a null target is HOLD FIRE
    (`RangedAttackChoice.HoldFire(weapon)`, `IsHoldFire`). No new `CancellableResult<T>` subtype — that
    would have to be generic over T for one request's benefit.
  - `ICombatActionContext.DeclineWeapon` + `DeclinedWeapons`: the weapon leaves `AvailableWeapons`
    WITHOUT entering `AlreadyUsedWeapons` (that one means "has fired" and drives the exit/morale logic).
  - `ChooseRangedAttackStage.Enter` now loops (`OfferWeapons` per pass): hold fire declines and re-offers,
    everything else routes onward on the first pass. Declining the last weapon routes exactly like the
    no-fireable-target branch — Choose Action if nothing fired, end-the-shoot if something did.
  - `ChooseRangedAttackRequest.AllowStopShooting`: the mirror of `AllowCancel`, true once a weapon has
    fired. Both reply `Cancelled`; the engine still decides what that MEANS, so the pair only changes what
    the button is allowed to be called. Exactly one is ever true.
  - `WeaponOption.LimitedRule` / `.LimitedAlreadyFired`, from the new `LimitedRules.LimitedRuleName`
    (alias-aware, the `CoverIgnoreSource` shape). Carried on the option because `IWeapon.RuleDefinitions`
    is `[JsonIgnore]` — a remote player's request has no rules on it to read.
  - Spending a Limited weapon now logs "X is Limited - spent for the rest of the game."

  **App.** GUI: ONCE PER GAME / SPENT badge on the weapon row, an amber consequence line in Details
  ("firing spends this weapon for the REST OF THE GAME. Hold fire to keep it."), a two-row footer
  (Fire! on top; Back-or-Done + "Hold fire (H)" below), and a Done confirmation popup that names the
  weapons being given up and calls out that the Limited one keeps its shot. CLI: the same badge text on
  every weapon line, `[h1..hN] Hold fire` entries, `[0] Done shooting`, and a y/N confirm listing the
  unfired weapons (EOF answers yes — a piped script that asked to stop meant it).

  **Tests.** 7 new in `ChooseRangedAttackStageTests`: hold fire leaves a Limited weapon unspent and
  re-offers the rest; hold fire on a Deadly+Limited weapon un-gates the ordinary ones; decline-everything
  with nothing fired returns to Choose Action; decline-the-last after firing ends the shoot;
  `AllowStopShooting` is offered only after firing; `LimitedRule` naming; JSON round-trip of the
  hold-fire reply and the badge fields. Plus two assertions folded into the existing spent-Limited test.

- 2026-08-02: **Hand-test fixtures.** `LimitedWeaponsTest.fdgarmy` (repo root, 500pts, 4 units — one
  case each: Deadly+Limited rocket beside plain rifles; two DIFFERENT Limited weapons on one unit;
  a unit whose only weapon is Limited; and a Limited MELEE weapon, which is #316's gap and is expected
  to stay unenforced). `Scenarios/limited-weapons-test.json` drops all four in range on turn 1 so the
  shoot panel is two clicks away; `Scenarios/limited-hold-fire.json` is the narrow Deadly+Limited case.

## Decisions

- **Per-weapon hold fire, not just an end-the-shoot exit** (user sign-off). A plain "Done shooting" cannot
  serve the common OPR profile: a Deadly+Limited rocket is a RESOLVE-FIRST weapon (#028/#314), so while it
  is on offer it gates the unit's ordinary weapons. With only an end-the-shoot exit, declining the rocket
  would cost the player their rifles too. Declining it per-weapon releases them — the gates read the
  available pool, so a declined weapon stops demanding to be resolved first. This is the scenario the
  integration test and the hand-run scenario both pin.
- **Confirmation on Done, not on Fire** (user sign-off on Done; the Fire side was my default, flagged).
  Done ends the action with loaded weapons, so it asks and names what is being given up. Firing a Limited
  weapon does NOT prompt — the badge, the amber Details line and the Hold fire button are the warning, and
  a confirm on every shot would be one click per volley forever.
- **Hold fire is always offered, including under Instinctive** ("must attack the closest valid target").
  Deliberate, and a judgment call worth revisiting: the compulsion as modelled only narrows TARGETS, and
  the unit could already dodge it entirely by choosing a different action at the menu, so hold-fire opens
  no hole that was not already there. If Instinctive should compel the shot itself, that is a
  ChooseActionStage-level fix, not a resolver one.
- **AI still spends Limited weapons freely.** The AI resolvers only ever pick; they never hold fire.
  Out of scope here (it is an AI-policy question, not a rules one), and called out so it is not mistaken
  for an oversight.
- **`DeclinedWeapons` kept separate from `AlreadyUsedWeapons`.** Folding declines into "already used"
  would have been one line, and would have silently told #308's logic that the unit had fired — flipping
  Back to Done and making the unit owe a morale test it never triggered.

## Outcome
(pending — GUI hand-verify)
