# 203 — Stage-machine continuation chain grows with game length -> uncatchable StackOverflow

**Status:** open (filed 2026-07-09, surfaced by #200's livelock)
**Where:** `ParentStage.TransitionToChild` / `StageBinding.Activate` - synchronous async-void chaining

## Symptom

Long games crash the whole PROCESS with `Stack overflow.` - not an engine Fault, not catchable,
fatal to a benchmark fleet (and to a real GUI session). The stack trace is thousands of
`TransitionToChild -> Enter -> Activate -> TransitionToChild` frames: stage transitions complete
synchronously, so every decision in the game adds frames that never unwind until the game ends.

## Repro / evidence

#200's Orks livelock at default stacks dies as a stack overflow (~2,000+ decisions). With
`DOTNET_DefaultStackSize=0x4000000` (64MB) the same game survives to the watchdog instead. Any
legitimately long/large game (2k+ horde mirrors run 400+ decisions; bigger points or more rounds
push further) walks toward the same cliff - the loop just reached it first.

## Direction (engine core -> Chris sign-off; overlaps the plan's B0 spike)

An `await Task.Yield()` at a per-activation boundary (e.g. entering `DeterminePlayerTurnStage` or
`ReconcileEndOfActivationStage`) unwinds the accumulated frames once per activation, bounding
stack depth by the deepest single activation instead of the whole game. This is the same
async-void plumbing docs/ai-agent-plan.md B0 already plans to stare at (simulation stop/step);
coordinate the change with that spike. Interim mitigation for FdgLab fleets:
`DOTNET_DefaultStackSize` env var (documented, not a fix - the GUI has the same exposure).

## Notes

- 2026-07-09 — filed with #200 during #191 pool validation.
