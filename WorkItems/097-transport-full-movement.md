# 097 — Transport disembark/embark full movement (real path + Rush/Charge)

**Status**: implemented + tested; awaiting GUI hand-verify
**Related**: #035 (Transport — slices C/D), #011 (move-through-enemy validation), #012 (Advance/Rush/Charge bands), #197 (the ">9in" charge-origin rules), #205 (friendly models as end-of-move obstacles)

## Goal
Replace the Advance-equivalent simplifications #035 slices C and D shipped with the faithful movement the rule implies ("units may enter/exit by using any move action"):

- **Disembark (slice C today):** places the unit within 6" of the transport and counts it as an Advance (may then Shoot, can't move further). Should let the unit take the *full* move from the 6" drop point — Rush, or Charge into melee out of the transport.
- **Embark (slice D today):** the unit is "set aside" if a friendly transport is within Advance distance — no real path is drawn, no Rush/Charge in. Should move the unit along a real path into base contact with the transport (and allow Rush/Charge to reach it).

"Done" = a unit can disembark and then charge, and can move (including Rush/Charge) into a transport to embark, with real paths validated against terrain / enemies like any other move.

*(2026-07-26: the disembark half of this goal was re-scoped to the RAW leash reading — see the design fork below. The goal text above is preserved as written; the decision supersedes it.)*

## Decisions

- **2026-07-26 — Disembark semantics: the 6" leash IS the move (RAW), over "drop 6" then take a full move".** Fork surfaced with the user. The rule text recorded in #035 is *"units may enter/exit by using any move action, but **must stay fully within 6" of it when exiting**"*, which caps the exit at the leash; the #097 goal text above described the 40k-style drop-then-move instead, worth up to ~18" of threat on a Rush. Owner chose RAW. Consequences: the 6" `CircularZone` placement stays exactly as slice C shipped it, and no post-drop path move is built. What the exit "spends" is emergent from the distance recorded rather than a declared band — a unit whose Advance is shorter than the leash effectively Rushed out and loses its shot, which is the correct RAW outcome and falls out of the honest distance (below) for free.
- **2026-07-26 — Embark flow: move-first-then-board, over a dedicated action that drives the path.** Fork surfaced with the user. Boarding is offered only from contact (`TransportUtilities.EmbarkContactDistanceInches`, 1"); the approach is an **ordinary Move**, so real paths, terrain, enemy footprints, dangerous-terrain tests and the move-through rules (Strafing, Crossing Attack) all apply for free — none of which the set-aside could see. Rejected: routing a single "Embark into X" pick through `MovementStage` with a pending-embark target, which needs a pending-embark round trip through `MovementStage`/`ChooseAction`, cancel handling, and a new path-end constraint the GUI resolver would have to render. The one thing the rejected option had over this is discoverability, which is bought back with the greyed menu entry.
- **2026-07-26 — Contact is 1", not 0".** True base contact is unreachable in practice: #205 makes friendly models end-of-move obstacles, so a mover can never touch the hull, and no click-driven placement lands on an exact 0.0. 1" is the band `MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES` already uses for "adjacent" everywhere else in the engine.

## Notes

- **2026-07-26: Built.** Engine-only change; no app-side edits.
  - **Disembark (`DisembarkStage`)** — records the REAL distance the exit covered (furthest model's drop from the transport, matching `MovementUtilities.GetMaxMoveDistance`'s max-over-models convention) instead of slice C's flat `RegisterMoveFinished(0f)`. `GetCanShoot` gates on it, so a unit whose Advance is shorter than the leash now has to choose: hop the full 6" and forgo shooting, or stay inside its Advance and shoot. Recording 0 silently handed it both. A normal 6" Advance is unaffected — the leash and the advance-and-shoot cap are the same number, and the compare is margin-tolerant.
  - **Disembark-then-Charge already worked** and is now pinned. Charge is a separate menu action in this engine, gated on `AreUnitsInMeleeRange` and *not* on `HasMoved` (`ChooseActionStage.GetCanCharge`), so a 6" exit landing next to an enemy has always offered it. Nothing implemented; a regression test now says so out loud.
  - **Charge-origin snapshot fixed (`UnitActionContext.SnapshotDistancesToEnemies`)** — an embarked unit's models sit at the origin, so the pre-move snapshot was measuring from the table corner. Every #197 "shoots or charges enemies over 9in away" rule read garbage for any unit that disembarked and charged. Now measured from the transport, which is where the unit physically is when its activation begins.
  - **Embark (`EmbarkStage` + `ChooseActionStage`)** — `GetEmbarkableTransports` takes the distance as a parameter (the caller asks at two ranges) and compares with `LessThanOrAlmostEqual`. Boarding is offered from contact only; `HasMoved` no longer disqualifies (the move it just made IS the entering move), `HasAttacked` still does. A transport with room within the unit's Rush but short of contact is listed **greyed** with a reason rather than omitted — `"Move into contact with <X> first."` before the move, `"Not in contact with <X>, and the move is spent."` after it. Stopping an inch short is the easy mistake, and silently dropping the entry gave no account of why.
  - Tests: `TransportEmbarkTests` 6 -> 10, `TransportDisembarkTests` 5 -> 8 (+7 net). Full engine suite **2178/0**, app build clean, headless smoke exits 0.

- 2026-06-21: Opened from the #035 slice C/D deferrals. Both slices deliberately reused the simplest "place within 6" / set-aside" primitive (the same shape as #035's other Advance-equivalent calls) and recorded the real-movement work here. Threads an embark/disembark target through the movement flow (`DefinePathStage` / `ExecuteMoveStage`).

## Deferred / explicitly not built

- **Declared exit band (Advance vs Rush out of the hatch).** Under the RAW leash there is no separate declaration: the band is emergent from how far the drop actually went. Only a unit whose Advance is under 6" can tell the difference, and for it the choice is made by where the player drops the models. No UI.
- **Embark via Charge.** "Any move action" nominally includes Charge, but Charge targets an enemy and this engine models it as a post-move melee action, so there is nothing to build. Advance and Rush both reach a transport through the ordinary Move.
- **A "boarding band" visual on the move overlay.** The player currently learns they are in contact by finishing the move and reading the menu (valid, or greyed with the reason). Drawing the 1" band around a friendly transport during a move would be nicer — it is a #161/#230-class overlay change and is not in this slice.
- **GUI hand-verify** — see below.

## Hand-verify checklist (GUI)

1. Embarked unit -> Disembark -> drop the squad ~2" from the hull -> **Shoot is still offered**; the log reads `disembarked <transport>, moving 2 inches.`
2. Same, but drop a **Slow** unit (Advance under 6") at the far edge of the 6" circle -> **Shoot is greyed** with the moved-too-far reason.
3. Drop a squad with melee weapons next to an enemy -> **Charge is offered** straight out of the transport, and Move is not.
4. A unit 5" from its own transport: the action menu shows **Embark greyed**, reason `Move into contact with <X> first.`
5. Move that unit into contact -> **Embark goes live**; picking it sets the unit aside and ends the activation.
6. Move it up but stop ~2" short -> **Embark still listed, greyed**, reason `Not in contact with <X>, and the move is spent.`
7. Rush a unit 10" across the table into contact with a transport and board it in the same activation.

## Outcome
