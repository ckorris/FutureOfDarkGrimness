# 401 — Deadly resolves per clump: wound packets in the assignment pipeline

**Status**: done
**Related**: #400 (the ordering fix and the two `[Ignore]`d tests this item un-ignores), #028, #023/#024/#006

## Goal

Deadly(X) + Regeneration must resolve the way the rule's author describes it (OPR Discord,
2026-07-22: "just do them one at a time"; "you can't accurately prevent spillover otherwise"): each
failed save is a clump of X wounds that lands on ONE model, each of those X wounds gets its own
Regeneration roll, and whatever does not fit that model is lost. Today the stage collapses everything
to one scalar, rolls Regeneration over the capped total, and re-allocates freely. Done = the
assignment pipeline carries an ordered list of **wound packets** (a plain wound is a packet of 1, a
Deadly clump a confined packet of X), `AssignWoundsResults` commits packets so no resolver can split
or spill one, the stage rolls Regeneration per packet before asking, the two #400 `[Ignore]` tests
pass, `CombatMath` mirrors it through the same shared code, the GUI/CLI show clump progress, and a
scenario exists that puts the whole thing on screen in one activation.

## Notes

- 2026-09-12 (after Chris's GUI pass): two panel requests. (1) Explain why the dialog is in clump
  mode - `WoundAssignmentText.Explanation` names Deadly(X), mentions Regeneration only when a clump
  actually shrank ("ignored", per Chris). (2) Show every clump up top - a wrapping strip of chips:
  placed (green, "1: 1 -> M3, 2 lost"), next (amber with the highlight ring, "2: 1 wound"), pending
  (grey, "3: 3"), each with a tooltip ("3 rolled, 2 ignored, 1 wound to land"); and each model row
  previews what the next clump does to it ("takes 1 -> 2/3 left" / "takes 2, 1 lost - dies"). Engine
  grew `WoundPacket.OriginalWounds` and a per-commit `PacketCommit` log to feed it. CLI prints the
  same list. Chris also caught the second header line being clipped by the fixed 72px header - the
  list now starts where the header ends.

- 2026-09-12 (close): Shipped in four slices, each green before commit.
  1. `WoundPacket` + packet-aware `AssignWoundsResults`/`AssignWoundsRequest`, behaviour-neutral
     (the scalar constructors build one unconfined packet). 15 new pins in
     `WoundPacketAssignmentTests`, incl. `Simulate == AutoFill` and a wire round-trip.
  2. `AssignWoundsStage` builds the queue (`WoundAllocation.Packets`), rolls Regeneration per packet
     before any cap, and prompts only when the answer can differ; `CombatMath` prices the same queue
     through `Simulate`; `ConfineToClumps` deleted; #400's two `[Ignore]`d tests un-ignored and green.
  3. App: `WoundAssignmentText` shared by the ImGui panel and the CLI prompt ("N landed, M lost
     (Deadly: no carry-over)" / "Next: clump k of n - X wounds to ONE model"); docs updated.
  4. `Scenarios/401-deadly-clumps.json` (+ `.fdgsave`, `armies/401-*.fdgarmy`): YOU defend three
     Tough(3) Regeneration brutes (one on 1 wound) against four Deadly(3) meltas. Verified headless:
     "3 failed save(s) become clump(s) of 3", "1 landed, 2 lost", "Next: clump 2 of 3 - 1 wounds"
     (Regeneration shrugged two of that clump's three), "2 wound(s) lost - a Deadly clump does not
     carry over past the model it hit."
- 2026-09-12: Two things found on the way, neither #401's:
  - The default `SoloRules` bot advances 6.6" toward the enemy on its first activation and then cannot
    shoot (>6" move-and-shoot allowance) - so the scenario's shot lands in round 2 under the default
    profile and in round 1 under `--ai-profile tactician`. Noted in the scenario text; not fixed here.
  - The engine's "No actions available ... - passing" line said nothing about WHY. It now appends
    each gated option's reason, which is how the above was diagnosed at all.

- 2026-09-12: Filed. `git fetch origin --recurse-submodules` first: `origin/master`'s index
  high-water mark **398**, archive max **399**, local (unpushed) max 400, no `WorkItems/40[12]*` on
  any remote branch. Logged in `Reconciliations.md`.

## Decisions

- **Option A over B (per-clump request loop) and C (engine auto-assigns).** Chris's call after the
  proposal. The load-bearing fact: `WoundIgnoreSink` folds every ignore source to ONE unit-wide
  threshold, so rolling clump k's dice before the player picks its target is statistically identical
  to slow-rolling after. One request, one dialog, dice on the host. B would pay a round-trip and a
  modal per clump for the same experience; C deletes the "which model dies" decision the Tactician
  resolver explicitly prices (objective holders, heavy-weapon carriers).
- **Enforcement lives in `AssignWoundsResults.TryAddWounds`**, where #006/#024 already live: it
  commits the NEXT packet to the clicked model, lands `min(packet, capacity)`, discards the rest.
  Resolvers keep the same call and physically cannot produce an illegal allocation.
- **Packets have a Weight** (probability mass, 1 for a whole packet) so a fractional failed-save
  count under the probabilistic roller (2.34 clumps) reads as two sure clumps plus a 34%-likely one:
  landed = weight x min(wounds, capacity). The old `ConfineToClumps` returned min(0.34 x 3, 1) = 1.0
  for that case where 0.34 is right, which mis-priced Deadly against 1-wound targets in the AI's
  valuation.
- **Regeneration beats play per clump, before the dialog.** Sizes are therefore visible when the
  player assigns. Not exploitable: #023 forces the already-wounded model first and #006 the hero
  last, so a uniform squad offers no capacity-differentiated choice for a shrunken clump to be
  steered at. Recorded so the choice is deliberate, not accidental.
- **Plain wounds are untouched by construction.** A single unconfined packet drains exactly as the
  old scalar did, and the stage already rolled Regeneration before capping on that path, so numbers
  and dialog are identical - the whole suite is the proof, not a claim.
- **Wire format of `AssignWoundsResults`/`AssignWoundsRequest` changes** (packets replace the
  scalar). Old mid-assignment saves stop loading; pre-1.0, accepted.

- **Regeneration beats play per clump, before the dialog.** (See above.) The lone-big-model case
  therefore shows one Regeneration roll per clump rather than one combined roll - a presentation
  choice Chris left open; easy to fold into one beat if it reads as noise in play.
- **The melee Takedown pin changed meaning, deliberately.** Under the probabilistic roller its strike
  is a 25/36-likely Deadly(3) clump. The old scalar confinement capped 25/36 x 3 = 2.08 at the
  1-wound model and asserted a *certain* kill from a 69% shot; the weighted packet lands 25/36 of a
  wound, the honest expectation, and the test now asserts confinement (all of it on the picked model,
  none elsewhere) instead of death.
- **`TotalWoundsToAssign` is now the queue's carry, an upper bound under Deadly.** Tests and displays
  that mean "what landed" read `TotalAssignedWounds`; the request/results headline is only exact
  for a plain volley. Resolvers must build their results from `request.Packets`, never the headline.

## Outcome

Shipped 2026-09-12. The wound pipeline carries an ordered queue of `WoundPacket`s end to end - a
plain volley is one unconfined pool (numbers and dialog identical to before, by construction), a
Deadly clump is a confined packet of X landed on one model with the excess lost, and Regeneration is
rolled per packet before any cap. Enforcement lives in `AssignWoundsResults.TryAddWounds`, so no
resolver can split or spill a clump. `CombatMath` prices the same queue; the estimate and the
resolution share one model. GUI and CLI show clump progress from one shared text. Scenario
`401-deadly-clumps` puts it on screen. Engine 3343/0 (1 pre-existing corpus skip), app 2913/0, full
build, headless smoke and the scenario itself all green. Deferred: nothing from this item's scope;
the solo bot's advance-then-cannot-shoot quirk is recorded above, unfiled.
