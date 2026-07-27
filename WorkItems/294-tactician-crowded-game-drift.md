# 294 — Tactician: crowded-game lateral/backward drift; team-blind scoring in 2v2

**Status**: in-progress (investigation + repro done 2026-07-27; fix slices being implemented)
**Related**: #264 (walled-unit sibling — impassible terrain; this is the friendly-congestion +
team-game case), #256 (stuck-unit rescues), #216 (solo fallback), #191 (Tactician umbrella),
#167 (scenario tooling), #291 (bounds rule - exposed the off-table auto-row)

## Goal

In crowded games (esp. 2v2, 3k per player = 6k per side), front-line units (melee hordes,
loaded transports) advance and take/hold objectives instead of drifting laterally/backwards in
the deployment zone; rear units ball up behind the objective line; activation order clears lanes
front-first. Done = the crowded-2v2 scenario plays sanely and Chris's reported shapes are gone.

## Evidence (2026-07-27, committed repro)

`Scenarios/crowded-2v2-3k.json` (Saurian+Goblin vs Soul-Snatcher+DarkElf, ~50 units, open table)
via `--headless --scenario ... --all-ai --ai-profile tactician --log-decisions --seed 42`:

- **Round-1 deployment-zone retreats**: Minions at (23,33) chose FallBack 5.2" BACKWARD (score
  1.09) while `RushObjective Reachable end=(18.0,24.0)` — standing ON a marker — scored 0.61.
  Also Nightmares FallBack, Acolytes backward Escort, Heavy Skimmer backward kite. Nothing had
  fired yet.
- **Rear hordes wedged**: goblin Shooter/Storm Mobs (back rows) log "every movement candidate
  nets < 1 inch" repeatedly in rounds 1-2 (the #256 stuck detector, now visible via the new
  scenario --log-decisions flag).
- **Team-blindness**: 4 chosen plans in one game target TEAMMATES as enemies (EngageAtRange vs
  ally, Block vs ally). Score 0-0 vs 0-1 after 4 rounds of a 12k game; 8 of 10 late objective
  reconciliations are "contested - becomes neutral".

## Findings (code-read 2026-07-27)

1. **Team-blindness (the 2v2 amplifier).** `ITeamExtensions.AreAllied` exists (its own doc
   comment records this exact bug class) and deployment overlap + spell targeting use it - but
   `TacticianPlanner.EnemyBindings/FriendlyBindings`, `TacticianActivationResolver.EnemyBindings`,
   `MacroActionGenerator.LivingEnemies/LivingFriends`, and `MovementPlanner.LiveEnemyFootprints`
   are raw `PlayerID !=` comparisons. In 2v2 the teammate's whole 3k army is priced as HOSTILE
   (retaliation, FallBack-from-threat, kite bands, activation urgency, alt-target shares) and its
   models are enemy footprints (1" standoff, no move-through) while ALSO being friendly footprints
   (team-aware `LiveFriendlyFootprints`). `AreAllied` with no team == same-player, so a fix is
   bit-identical for every 1v1 path/benchmark by construction.
2. **Objective terms are per-player, engine seizure is per-player, victory is per-team (#257).**
   `ObjectiveDelta`/`ObjectiveApproach`/`Posture`/flip treat teammate-held markers as "not ours"
   -> the bot marches onto ally markers; `ReconcileObjectivesStage` then flips them NEUTRAL
   (two players in range = contested, allied or not). The bot is structurally incentivized to
   neutralize its own team's objectives. (Whether allied players SHOULD contest each other is an
   engine design fork - flagged separately; the bot avoiding it is correct under either answer.)
3. **Screen credit pays behind the ward.** `ScreenValue` measures distance to the threat->ward
   SEGMENT; `DistanceToSegment` clamps, so a point BEHIND the ward within 5" still collects up to
   full intercept. A backward move can be paid as a "screen" (Minions' FallBack above; Chris's
   "walking in front of something vulnerable" - sometimes behind it).
4. **No activation-order concept of lane clearing.** Round-1 urgency is ~0 for everyone (kill 0,
   flip 0, threat ~0), so order is arbitrary; rear units activate into sealed lanes (the wedged
   mobs) while the front rows that would clear them wait. Chris's manual remedy is exactly
   front-first activation.
5. **No credit for the objective ball.** `ObjectiveDelta` pays only ON the marker (+1 within
   seize+1.5"); a unit that cannot reach the crowded marker gets ~nothing for stacking up close
   behind it, and there is no "stand between the enemy and the marker" term (Screen wards units,
   not markers). Clipped forward moves also still pay full retaliation while retreat families
   reach their goals at full distance - the #264 asymmetry, friendly-congestion edition.

## Fix plan (slices; 1-3 observation games per slice, same seed, not full FdgLab)

1. Team-awareness sweep across the Tactician (+ shared `LiveEnemyFootprints`); team-aware "ours"
   for all objective terms; ally-held-marker contest treated as the negative it is.
2. Screen credit gated to the segment INTERIOR (no pay behind the ward).
3. Activation urgency: small front-first bias (forward position relative to the team's
   unactivated mass) so round-1 order clears lanes front-to-back. New weight constant.
4. If still needed after 1-3: support-ball credit near friendly/contested markers + crowd-aware
   goal retargeting for jammed objective lanes.

Weights policy: slices 3/4 add scoring terms - benchmark gate before merge to master per
TacticianWeights file-header policy; observation games are the in-progress signal per Chris's
2026-07-27 instruction.

## Tooling landed with this item

- `--all-ai` + `--log-decisions` on the `--scenario` path (headless; GUI errors out) -
  bot-vs-bot observation runs with full candidate tables, no FdgLab needed.
- ScenarioCompiler auto-placement now WRAPS rows inside the deployment band (was: one infinite
  row along +X - a 3k army walked off the 72" table to x~184, where the #291 bounds rule pins
  every model; discovered because game 1's geometry was garbage).
- `Scenarios/crowded-2v2-3k.json` - the committed repro.

## Notes

- 2026-07-27: filed after investigation + two observation games (game 1 invalidated by the
  off-table auto-row; game 2 above is the evidence run). Analyzer script (move classification,
  teammate-target detection, stuck/objective tallies) lives in the session scratchpad; promote to
  FdgLab tools if it earns its keep.

## Decisions

- 2026-07-27 (Chris, session brief): investigate first, catch it live in a 2v2 3k-per-player
  game, then fix and re-run the SAME game(s); 1-3 games at a time, not full FdgLab runs; regular
  updates. Engine edits authorized.

## Outcome

(open)
