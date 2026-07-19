# 248 — Resolver keyboard hotkeys + activation back-out

**Status**: in-progress
**Related**: #161 (resolver consistency — the shared keyboard list helper absorbs part of its dedup finding), #202 (built the AllowCancel/CancellableResult back-out plumbing this extends), #237 (Enter-as-commit precedent), #240 (repeat:false stuck-key rule), #246 (EscapeRouter)

## Goal

Keyboard-first resolver interaction, three facets:
1. **Action menu letter hotkeys** (`GuiStringSelectionResolver`): every valid option gets a letter
   hotkey shown in its label. Built-in five are fixed (Move=W, Charge=C, Shoot=S, Cast=A, Pass=X);
   rule-added actions (Disembark, Teleport, custom) draw from a left-hand pool
   (Q E R T D F G Z V B) in display order. Letters stay put when options gray out.
2. **List picker numbers + arrows**: on list-style pickers (unit/model/zone selection, ranged-attack
   list, spell list, melee defender, ability effect), 1-9,0 instantly picks options 1-10; Up/Down
   moves a highlight (synced to the canvas ring highlight where one exists); Enter commits the
   highlight. Built as a shared helper, not a fifth copy-paste (#161 D).
3. **Activation back-out** (engine + app, authorized 2026-07-18): after picking a unit to activate,
   if nothing irreversible has happened (no move, no attack, no tokens/costs spent), Back/Esc returns
   to unit selection. Follows #202's AllowCancel pattern — NOT a plain valid option, so AI resolvers
   and CLI EOF defaults can never pick it and loop the turn.

All key handling: `repeat: false` (#240), muted under `WantTextInput` and while the Esc menu is open;
Esc routed through `EscapeRouter` (#246).

## Slices

- S1: `ResolverHotkeys` helper + letters on the action menu (app-only).
- S2: numbers/arrows/Enter on the `GuiSelectionResolver` family + bespoke list resolvers (app-only).
- S3: engine Back route `ChooseActionStage -> ChooseUnitToActivateStage` gated on pristine
  activation + GUI Back button/Esc claim; engine tests mirroring `MeleeBackOutTests`.

## Notes

- 2026-07-18: Filed. Sign-offs recorded in Decisions. Verified 248 free on origin index + archive;
  ff-synced master to origin (9843c76) before starting.

## Decisions

- 2026-07-18 (user sign-off): letter scheme = **fixed mnemonic + positional overflow** (not pure
  positional); number keys = **instant pick** (not highlight+Enter); engine changes **authorized**
  for the activation back-out.
- Back-out must be invisible to AI/CLI resolvers (AllowCancel + cancellable reply, GUI-only
  affordance) — a listed "Back" option would let the solo AI or an EOF default pick it forever.

## Outcome

_(open)_
