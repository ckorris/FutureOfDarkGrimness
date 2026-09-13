# 302 — Dynamic stage transitions (a jump-with-context hook for the state machine)

**Status**: open, unscoped. Filed 2026-07-30 out of #197 P19; deliberately NOT sized by that rule.
**Related**: #197 (P19 out-of-order activation, the rule that raised the question), #052 (save/resume),
#203 (stage-machine stack growth), #082 (controller lifecycle)

## Goal

Let a stage — or a rule operation — declare *"next, transfer control to stage X with this pre-built
context"*, instead of only the statically bound transitions each `ParentStage` builds in
`PopulateTransitions`. The use case that would justify it is **reactions and interrupts**: flow that has to
leave the current chain and come back, which the engine has no way to express today.

## Why it was raised, and why P19 did not use it

#197 P19 (Coordinate: "another friendly unit may be activated immediately") looked like it wanted a jump,
and the owner asked for one rather than a rule-specific mechanism. On inspection the stage SEQUENCE was
already correct — `DeterminePlayerTurnStage -> ChooseUnitToActivateStage -> MainUnitActionStage` is exactly
what should happen next. What was wrong was only the **data** those stages compute: which player is acting
and which unit gets picked. A jump would have landed in the very stages the machine was already heading to.

The same is true of every out-of-order effect shipped so far, which is worth recording because it is the
argument against building this speculatively:

- **Harassing / Hit & Run** ("move 3in after shooting or being in melee") — a passive `HookEntry` at
  `Shooting_OnPostShoot` / `Melee_OnPostMelee` emitting `Effect.TriggeredMove`, enacted through
  `PostCombatMoveGate` + `MovementExecutor`. `PostShootStage` and `PostMeleeStage` are always in the chain
  and no-op when no rule produces an operation.
- **Impact / Ravage / Storm / P16's extra attack** — a dedicated child stage that runs a real pipeline at a
  fixed point.
- **P19** — a flag two always-present stages read on their way past.

So the engine's answer to "something happens out of the usual order" has always been *a hook at a stage that
already exists*, plus either an executable operation or a child stage. None of them needed the sequence
changed.

## What makes it expensive (scope this before building)

1. **Context ownership.** Contexts are strongly typed per layer (`ISingleRoundContext` / `ISingleTurnContext`
   / `ICombatActionContext` / `ICombatMetadata`) and parents build their children's via
   `GetNewChildContext`. Jumping into a stage whose parent chain is not currently entered means synthesizing
   that whole ancestry — the genuinely hard part.
2. **Save/resume.** #052's rolling snapshot and `ParentStage.GetResumeEntry` are built around the static
   chain. A jump target has to be representable in the save format, or a load lands somewhere the snapshot
   cannot describe. (This is the objection the owner rated highest.)
3. **Stack discipline.** #203: transitions are deliberately tail calls so a long game does not accumulate
   frames. A jump must preserve that, not reintroduce nesting.

## Done =

Not yet defined — this item is a placeholder for a design pass, not a scheduled build. The right first step
is to name two or three concrete features that genuinely need it (reactions/interrupts, a nested sub-game,
an out-of-sequence phase) and check whether each is really a sequence problem rather than a data problem,
the way P19 turned out to be.
