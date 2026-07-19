# 248 — Resolver keyboard hotkeys + activation back-out

**Status**: done (awaiting GUI hand-verification)
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

- 2026-07-19: S3 landed (engine f072205 + superproject bump). Engine: `StringSelectionRequest.AllowCancel`
  (default false) + null-reply cancel sentinel (wire-safe - RequestMessageSender already forwards null as
  a legitimate cancel); `IUnitActionContext.IrreversibleActionTaken` marked at every commit point
  (ActivationStartStage passive ops + resolved ability offers, CastSpellStage token spend, CustomActionStage
  resolve, TeleportStage accepted placement; HasMoved/HasAttacked cover the rest); ChooseActionStage offers
  AllowCancel only while pristine and routes a null reply out `ToBackOut` -> MainUnitActionStage's own
  `OnBackedOut` sibling (nothing marks the unit activated) -> SingleTurnStage rebinds to
  ChooseUnitToActivateStage. 5 new tests (`ActivationBackOutTests`), suite 1716 green. App: GUI action menu
  gets Back (Esc) when cancellable; CLI prints `[0] Back` (EOF default unchanged - never cancels).
  Known cosmetic: the GameProgress activating-unit spotlight stays on the backed-out unit until the next
  pick overwrites it.

- 2026-07-19: S2 landed. Shared pieces: `KeyboardListNav` (per-resolver highlight state, resets on
  request change) + `ResolverHotkeys.PressedNumberIndex/NumberPrefix/ArrowDelta/HorizontalArrowDelta/
  IsEnterPressed/PressedDigit`. Coverage: `GuiSelectionResolver<T>` base (unit/model/zone dialogs,
  incl. scroll-into-view + `OnValidOptionHighlighted` ring hook overridden in unit/model selectors),
  `GuiCancellableSelectionResolver<T>` + unit twin, melee-defender confirm card (Esc), shoot panel
  (Left/Right weapon, Up/Down + numbers target in fireable-first display order, Esc while Back shown),
  spell picker (numbers/arrows highlight - NOT instant, boost stepper owns Left/Right, Enter casts,
  Esc cancels), ability-effect picker (numbers/arrows/Enter, still mandatory - no Esc), Yes/No (Y/N),
  cast assist (digit = tokens: 0 = don't spend), deployment zones (numbers). Every new commit-path
  key is edge-only; every resolver now applies picks ONCE after drawing (same-frame click + key can't
  double-resolve the TCS - several inline-Complete paths were converted to this discipline).
- 2026-07-19: Skipped on purpose in S2: `GuiAssignWoundsResolver` (stateful canvas clicker; #237
  already gave it Enter = Auto-assign All), movement/placement/aircraft/terrain canvas resolvers (not
  lists - their Esc/right-click undo shipped with #202/#161-C), `GuiPlaceObjectsResolver`.
- 2026-07-18: Filed. Sign-offs recorded in Decisions. Verified 248 free on origin index + archive;
  ff-synced master to origin (9843c76) before starting. S1 landed (a279b9d).

## Decisions

- 2026-07-18 (user sign-off): letter scheme = **fixed mnemonic + positional overflow** (not pure
  positional); number keys = **instant pick** (not highlight+Enter); engine changes **authorized**
  for the activation back-out.
- Back-out must be invisible to AI/CLI resolvers (AllowCancel + cancellable reply, GUI-only
  affordance) — a listed "Back" option would let the solo AI or an EOF default pick it forever.

## Outcome

All three slices shipped 2026-07-19 (S1 a279b9d, S2 4ca72bb, S3 engine f072205/13f2531 +
superproject c5d5436/98bf002). Suite 1717 green, build clean, headless smoke exit 0.
Deferred (recorded in Notes): wound-assign/placement canvas resolvers keep their existing click
schemes; GameProgress spotlight stays on a backed-out unit until the next pick.

**Verify by hand (GUI):**
- Action menu shows [W]/[C]/[S]/[A]/[X] letters + pool letters on Disembark/custom rows; pressing
  W moves, letters stay put when Move grays out next activation.
- Activate a unit, press Esc (or Back) at the action menu before doing anything -> back at unit
  selection, unit still activatable, no Esc-menu popup on that press.
- Move (or shoot/cast) first -> the action menu's Back button is gone; Esc opens the in-game menu.
- Unit/target/spell pickers: number keys pick instantly, Up/Down walks rows (ring highlight follows
  on unit/model pickers, list scrolls), Enter commits the highlight.
- Shoot panel: Left/Right cycles weapons, numbers/arrows pick targets, Esc backs out until the
  first volley fires.
- Yes/No prompts answer to Y and N. Cast assist: 0 = don't spend, N = spend N.
- Headless: at the action menu of a fresh activation the CLI lists "[0] Back"; entering 0 returns
  to unit selection; piped EOF runs still complete (never auto-cancels).
