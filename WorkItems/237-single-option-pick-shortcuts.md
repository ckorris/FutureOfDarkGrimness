# 237 — Single-option pick shortcuts (shoot pre-select, charge confirm card, Enter auto-assign)

**Status**: implemented (awaiting GUI hand-verification)
**Related**: #202 (charge back-out — constrains this), #161 (resolver consistency), #223/#227 (same hand-verify batch)

## Goal

Playtest feedback (2026-07-16): stop making the player click through picks that have only one
possible answer.

1. **Shooting**: when the selected weapon has exactly one fireable target, pre-select it — the
   player only presses Fire. Never auto-fires.
2. **Charge**: with exactly one valid defender, don't show a one-item pick list.
3. **Assign wounds**: Enter triggers "Auto-assign All".

## Notes

- 2026-07-16: All three landed, app-side only (no engine changes).
  - `GuiChooseRangedAttackResolver`: new internal statics `FirstFireableWeaponIndex` (the
    auto-selected weapon is now the first weapon that can actually fire, not blindly index 0 —
    fallback to 0 when nothing is fireable) and `SoleFireableTargetIndex` (the weapon's only
    fireable target, or -1 when zero/several). Applied on new request and on weapon click.
    6 tests pin both seams.
  - New `GuiChooseMeleeDefenderResolver` subclasses `GuiCancellableUnitSelectionResolver`
    (whose `DrawTargetRings`/`_hoveredValidRef` became protected) and registers for
    `ChooseMeleeDefenderRequest`, replacing BuildGui's `DerivedRequestAdapter`. 2+ defenders:
    inherited list + canvas behavior, unchanged. Exactly 1: a confirm card — "Charge X?",
    primary Charge button (Enter-bound) + deemphasized Back; the ringed defender on canvas is
    still clickable to confirm.
  - `GuiAssignWoundsResolver`: "Auto-assign All" now uses `ResolverButtons.Primary`, which
    binds Enter (and gains the green accent + "(Enter)" hint).
- Verified: engine suite 1647 green, app suite 353 green, headless smoke exit 0. Build outputs
  were redirected during verification because a live game session held the bin locks.

## Decisions

- **Charge keeps a confirm (user ruling, 2026-07-16, picked over silent auto-select).** #202
  deliberately made the sole-defender charge ask, because this prompt is the charge flow's only
  Back button (impact hits roll right after the pick). The confirm card keeps that back-out at
  one click while dropping the pick-list ceremony. The engine stage is untouched and still
  always poses the request.
- **Sole-target pre-select is per selected weapon**, and only when the choice is forced —
  two fireable targets pre-select nothing.
- **CLI resolvers unchanged**: headless prompts still list the sole option (EOF defaults
  already skip it in automation).

**Verify by hand:**
- Shoot with one enemy in range: target already selected (rings + lines up), Fire enabled
  immediately; Enter fires. With two enemies in range: no target pre-selected.
- Multi-weapon unit whose first weapon is out of range but second in range: second weapon
  auto-selected.
- Charge with one enemy in range: "Charge X?" card with Charge!/Back; Enter charges; Back
  returns to the action menu with Move/Charge/Shoot intact (the #202 check). Clicking the
  ringed enemy on canvas also charges. With two enemies: old pick list.
- Assign wounds: Enter = Auto-assign All; per-model clicks unaffected.
