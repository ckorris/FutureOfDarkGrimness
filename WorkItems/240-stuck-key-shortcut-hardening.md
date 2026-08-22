# 240 — Stuck-key hardening for Enter/Esc panel shortcuts

**Status**: done
**Related**: #237 (Enter on Auto-assign), #238/#239 (hand-verify session that surfaced it), 840c0ea (introduced the Enter binding)

## Goal

A held-forever key must never drive the UI on its own. Reported 2026-07-16: "whenever I activate a
unit and select Move, it immediately skips the move as though I pressed Pass" — every activation,
all session, gone after a restart.

## Decisions

- **Root cause (diagnosed, then owner-confirmed as sticky keyboard hardware).** Every panel
  keyboard shortcut used `ImGui.IsKeyPressed(key)` with its default `repeat: true`. If a key's
  release is missed (sticky key on old hardware; a focus change mid-press, e.g. Enter in the lobby
  as LAUNCH transitions), Raylib/ImGui read it as held for the rest of the session and "pressed"
  re-fires every repeat interval. The movement panel's Enter-bound Done then self-commits the frame
  it appears: a zero-waypoint Done is a legal 0" move, so the activation continues, nothing is in
  shooting range, and ChooseActionStage logs "No actions available - passing" — reading exactly like
  an instant Pass. Activation (canvas click) and Choose Action (plain buttons) have no key bindings,
  which is why only the panels beyond them misbehaved.
- **Fix: edge-only detection (`repeat: false`) on every commit/cancel shortcut.** A stuck key
  produces no new press edges, so it caps at one spurious fire ever instead of a session-long
  runaway; a deliberate fresh tap still works, and key-repeat is never wanted on a commit button.
- Ruled out while diagnosing: engine flow (CLI scenario drive showed moves define/validate/execute
  correctly), #239 (touches no movement/resolver/input code), #237's registry change (melee-scoped),
  click-through between docked panels (ImGui active-id fires on release only).

## Outcome

`repeat: false` on all seven shortcut sites: `ResolverButtons.Primary` (Enter/KeypadEnter — covers
movement/consolidation/placement Done, Auto-assign All, Yes, Charge!, terrain/objective Confirm),
`GuiChooseRangedAttackResolver` (Enter = Fire!), `GuiYesNoResolver` (Esc = No),
`GuiPlaceOneTerrainResolver` (Esc x2), `GuiPlaceObjectiveResolver` (Esc),
`LobbyScreen` launch-problems popup (Esc = Cancel). App-side only; build + 376 app tests green.
If it recurs despite this: the log reading "No actions available ... - passing" right after picking
Move fingers an instant self-commit; "did not move - returning to Choose Action" would be an
instant Back instead.
