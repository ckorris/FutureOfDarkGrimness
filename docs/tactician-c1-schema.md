# C1 self-play data schema v1 (campaign step 4)

Authored 2026-09-03 (Opus, per the campaign doc's "the feature schema is a lock-in, so it gets an
Opus review before the first long run"). Implementation is Sonnet work; this file is the spec, and
it exists because regenerating days of data costs box-days. Design authority for the WHY:
`docs/ai-agent-plan.md` sec 10 (C1) plus invariant **G13** (scale/shape generalization).

**Status: awaiting Chris's go. Nothing here is built yet.** Changing this schema after the first
long generation run is a stop-and-ask (it invalidates data).

---

## 0. The three rules everything below obeys

1. **No absolutes.** No raw inches, points, wound counts or unit counts as a feature. Every value
   is a fraction, a share, or normalized by a scale reference. This is G13(a), and it is what lets
   one net read a 1k skirmish and a 4k battle.
2. **Per-SIDE aggregation, fixed width.** Features are computed for four blocks - SELF, ALLY,
   ENEMY_SUM, ENEMY_MAX - so a 1v1 and a 2v2 produce the SAME vector width (ALLY is zeros in 1v1).
   This is G13(b) and it is why 3v3 or FFA would also fit later without a schema change.
3. **Cheap.** The encoder runs at every activation boundary: ~190 (1k) to ~690 (2v2) times per
   game, across hundreds of thousands of games. Budget: **< 5ms per call**. Anything needing a
   CombatMath sweep per unit is out of v1 (see sec 4).

---

## 1. Row identity and labels

One row per activation boundary, from the ACTING player's perspective.

| Field | Meaning |
|---|---|
| `game_id` | GUID per game, joins rows to their outcome |
| `boundary` | 0-based index of this activation boundary within the game |
| `round` | `GameProgressData.RoundCount` |
| `acting_slot` | slot ID of the player about to activate (never the PlayerID GUID - #193's rule) |

Labels are joined **at game end** (rows buffer in memory per game; ~700 rows x ~120 floats is
about 1MB, so this is free):

| Label | Definition |
|---|---|
| `result` | 1.0 win / 0.5 tie / 0.0 loss, for the ACTING player's SIDE (team, not player - #257) |
| `obj_diff_norm` | (own side's final objectives - best enemy side's) / objective count, in [-1, 1] |
| `rounds_played` | for filtering short/faulted games |

Faulted, disconnected and watchdog-killed games are **discarded entirely**, not labelled - a
game that did not reach victory calculation has no ground truth.

**Decision taken at this boundary (added 2026-09-03, sign-off item).** A row records the STATE
before the decision; without also recording WHAT was then decided, the data supports a value net
only, and a v2 policy head (or a behavior-cloning warm start for B's tree policy) would need a
regeneration run. Three cheap fields, captured when the boundary's decision resolves and
written with the row:

| Field | Meaning |
|---|---|
| `chosen_unit` | index of the activated unit in the acting player's roster order (never a GUID) |
| `chosen_action` | the Choose Action reply string (`ChooseActionStage` constants or a rule-offer name); empty if the activation backed out |
| `chosen_macro` | the Tactician's winning macro-action label, or empty for non-planning profiles |

Capturing `chosen_action` is trivially reliable once step 5a's typed `ChooseActionRequest`
exists (the exporter wraps one request type instead of sniffing a prompt); until then the
exporter keys on the same `"Choose Action"` instructions the AI resolvers do.

---

## 2. Global scalars (7)

| Feature | Definition | Why not an absolute |
|---|---|---|
| `round_frac` | round / `NUMBER_OF_ROUNDS` | |
| `rounds_left_frac` | (total - round) / total | the single most decision-relevant clock |
| `objective_count_norm` | objectives / 5 | D3+2 gives 3..5, so this varies per game |
| `players_per_side_norm` | players on the acting side / 4 | tells the net the SHAPE (1v1 vs 2v2) |
| `points_norm` | min(1, total game points / 4000) | *Deliberate exception:* game SIZE is real context (1k plays differently from 4k), and it is admitted as ONE normalized scalar precisely so every other feature can stay scale-free. Without it the net cannot tell a small game from a big one at all. |
| `activation_frac` | boundary / expected boundaries this round | where in the round we are |
| `acting_side_is_first` | 1 if the acting side moved first this round | alternation matters |

## 3. Per-side block (15 features x 4 blocks = 60)

Computed for SELF (the acting player), ALLY (sum over allied players, zeros in 1v1), ENEMY_SUM
(sum over all opposing players), ENEMY_MAX (per-feature max over opposing players - "the
strongest single opponent", which is what a max^n backup cares about).

| Feature | Definition | Source |
|---|---|---|
| `health_frac` | current wounds / max wounds over living models | `ModelData` max wounds is set at creation (Tough), so this needs NO history |
| `value_share` | this side's living `UnitValue` / all sides' living `UnitValue` | `TacticalAnalysis.UnitValue` |
| `units_alive_frac` | living units / all units ever (UnitBindings is append-only, so the denominator is the starting roster for free) | |
| `ranged_share` | this side's `RangedOutputWounds` / all sides' | `TacticalAnalysis.RangedOutputWounds` |
| `melee_share` | same for melee output | derived the same way |
| `activations_left_frac` | unactivated / living units | `Progress`/`UnactivatedUnits` |
| `obj_held_share` | projected-owned objectives / objective count | `TacticalAnalysis.ProjectObjectives` (per SIDE, #297) |
| `obj_contested_share` | objectives with this side in range but not owned | same call |
| `mean_obj_dist_norm` | mean over living units of min base-edge distance to any objective, / table diagonal | `MinBaseEdgeDistanceToPoint` |
| `min_obj_dist_norm` | the closest unit's normalized distance | who can contest soonest |
| `mobility_norm` | mean `AdvanceDistance` / table width | `TacticalAnalysis.AdvanceDistance` |
| `threat_coverage` | fraction of enemy living units inside this side's threat range | `ThreatRangeAgainst` |
| `reserve_frac` | off-table units (reserve/embarked) / living units | `ReserveRules` / tokens |
| `seizer_frac` | units that `CanSeizeObjectives` / living units | aircraft cannot seize, so this is not the same as unit count |
| `activation_share` | this side's living units / all sides' living units (added 2026-09-03, sign-off item) | the activation-economy asymmetry: a share, so still no absolutes. See the Titan Lords note below |

**Titan Lords note (Chris, 2026-09-03).** Titan Lords are the schema's stress test: at 3k the list is
SIX single-model high-Tough units against an opponent's fifteen to twenty-five. Two things follow.
(1) Unit count and wound pool diverge maximally, which is exactly why `health_frac` (wounds) and
`value_share` (points) are both present and `units_alive_frac` is not trusted alone. (2) The
number of activations a side has RELATIVE to its opponent is a real tactical quantity (who runs out
of moves first, who gets to react) that no per-side fraction captures - hence `activation_share`.
The step 2 baseline's weakest cell (79%) was the only one containing Titan Lords; a 1v1 Titan cell
was added to the 3k panel the same day, and the mix for generation must include it so the net sees
single-model armies, not only hordes.

**Vector width v1: 7 + 60 = 67 floats** (15 per block since `activation_share`). (The plan sketched "~200"; that was a guess before the
primitives existed. Smaller is better here - every feature is one we can defend, and a wider
vector is easy to add at v2 while a regenerated dataset is not.)

## 4. Deliberately deferred to v2 (recorded, not dropped - CLAUDE.md's "never silently cut scope")

- **Firepower vs defense bands 2+..6+.** The plan names this as the thing that makes features
  army-agnostic, and it is the strongest candidate for v2. It is OUT of v1 purely on cost: it
  needs a `CombatMath.EstimateShooting` sweep against five synthetic defenders per unit per
  boundary, which is the planner's hot path, not a 5ms budget. `ranged_share`/`melee_share` are
  the cheap stand-ins. Revisit with a measurement, not a guess (G6).
- **Fatigue state**, which Appendix A's diversity rule explicitly says C1 features must carry.
  Cheap to add; deferred only because the fatigue token's read path wants checking first.
- **Local force concentration** (M10's learnable signal). Needs a clustering pass; measure first.

## 5. Per-unit entity table (logged from day one, SAMPLED)

v2 may want per-unit vectors with attention/DeepSets pooling, and the campaign doc's rule is to
log it now so v2 never needs a regeneration run. But a full entity table is ~50 units x ~15
features x ~700 boundaries per game - two orders of magnitude more data than the global vector.

**Decision: log entity tables for a 5% sample of GAMES (whole games, not scattered boundaries),
recorded as `entity_sample_rate` in the file header.** Whole games because a v2 trainer needs
complete trajectories, and 5% of a multi-day run is still tens of thousands of games.

Per-unit features (all normalized the same way): value share of own side, health frac, alive,
activated, in reserve, can seize, mobility norm, ranged share, melee share, normalized distance
to nearest objective, normalized distance to nearest enemy, threat-coverage frac, is-caster,
owning block (SELF/ALLY/ENEMY one-hot).

## 5b. Boundary subsampling (added 2026-09-03 from step 2's throughput data)

Measured throughput at DOP 16, today's engine: 3.4 games/s (1k mirror), 1.6 (3k vs solo), 0.8
(3k/4k mirror), 0.5 (2v2). A mixed generation run therefore averages roughly **1 game/s, so about
85k games/day and ~340k over a four-day window** - at 190-690 boundaries per game that is well
over 100M rows, or tens of GB gzipped. Disk is not the binding constraint (421GB free), but
training on it would be.

**Decision: write 1 row in 4 boundaries (`boundary_sample_rate`, uniform, recorded in the
header).** Rows from one game share a single outcome label and are highly correlated, so the
700th row of a game is worth far less than the 1st row of a NEW game - subsampling within games
while maximising game count is the right trade. Uniform (not head- or tail-biased) so early,
middle and late game phases stay equally represented. Yields ~25M rows over the window, which is
ample for a 67-feature model and still leaves headroom to lower the rate if v2 wants more.

The rate is a header field, not a constant, so a later run can change it without a schema bump.

## 6. File format and provenance

Gzipped JSONL, one file per 200 completed games, under `FdgLab/data/<UTC date>/`.

Header record (first line of every file), per G9 - training data provenance is per file:

```
schema=1, engine_commit, superproject_commit, created_utc, profile_a, profile_b,
seed_range, shape, points_level, army_a, army_b, held_out (bool), entity_sample_rate,
boundary_sample_rate, encoder_ms_mean
```

`held_out` is stamped from `FdgLab/armies/pool.json`'s `heldOut` list so a held-out pairing can
never silently enter training even if the mix config is wrong - the exporter refuses to write a
row whose pairing is held out, rather than relying on the sampler to have excluded it.

## 7. Verification before any long run (non-negotiable)

The campaign doc's step 4 gate, made concrete. A 10-minute sample must show:

1. Row count per game == that game's activation-boundary count (no dropped or double rows).
2. Every feature within its declared range - fractions in [0,1], `obj_diff_norm` in [-1,1] -
   asserted per column, not eyeballed.
3. Label balance sane (roughly the bench's win/tie rate for that matchup, not 100% one class).
4. Held-out pairings absent from the sample.
5. Byte-identical output for a fixed seed (the encoder must not itself be a source of
   nondeterminism - it reads state, it must never roll).
6. `encoder_ms_mean` under the 5ms budget; if not, cut features before generating, not after.

A silent encoder bug is the single most expensive failure mode available here: it wastes the
whole unattended window and is invisible until training. Hence 1-6 run BEFORE the long launch,
and the driver re-runs check 6 periodically as a canary.
