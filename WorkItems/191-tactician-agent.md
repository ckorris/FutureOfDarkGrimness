# 191 — Tactician: challenge-level game-playing AI (umbrella)

**Goal:** an AI opponent that genuinely challenges human players with any army vs any army,
built as a ladder of shippable bots: evaluation-driven heuristics (A) -> MCTS over macro-actions
(B) -> learned value function (C) -> optional self-improvement loop (D).

**Design authority:** `docs/ai-agent-plan.md` — architecture, standing decisions (D1-D6),
invariants (G1-G12), stop-and-ask triggers, per-slice specs, gates, and the macro-action
vocabulary (Appendix A, awaiting Chris's edit). This file is the running ledger; the plan doc is
the spec. Keep both current (plan G10).

**Prerequisites:** #192 (structured game result), #193 (determinism/seeding), #194 (FdgLab
harness). Order: any, but all three before Phase A. Related: #066 (AI resolver legality tests),
#168 (rule-load diagnostics surfacing), #170 (solo-rules deploy packing — baseline hygiene).

**Standing authorizations (Chris, 2026-07-09):** engine submodule modification within
`Ai/Tactician/` + the named P/B seams; new project `FdgLab/` in this repo; Python+ONNX stack.
Solo-rules bot behavior is frozen (benchmark baseline) — refactors sharing its machinery need
pin tests.

## Notes (newest first)

**2026-07-10 — A5-4b + A5-5 SHIPPED; GATE 78.1% MIRRORS / 80.6% MATRIX, NO CELL BELOW 50,
FAULTS = BASELINE - ALL AUTOMATED A-GATE CRITERIA PASS.** Engine `bcedbe4`. A5-4b (Chris's
review): ward threat = EXCHANGE MARGIN (a counter-blade powerhouse needs no screen; pinned) +
one-screen-per-lane (no dogpiles); his cases (a) weak-melee-threat and (d) late-objective-vs-
screen were already self-limiting (documented in code). A5-5: THE DE FIX - zero voluntary
disembarks existed in any DE log (cargo rode until the boat died and spilled out Shaken; the
fallback chain ended in Pass for embarked units). WantsDisembark: get out when a not-ours
marker or a winnable melee is within post-drop reach (6" placement + move/charge), keep riding
otherwise. Pinned both ways. 50-game probe DE-vs-Orks 46 -> 61.2 BEFORE the gate. **Gate
(a5-5-gate): matrix 77.4 -> 80.6, mirrors 79.4 -> 78.1, below-50 cells 1 -> 0 (DE-vs-Orks
cleared), faults 1/1800 = baseline v4 (#208 family). A-gate automated criteria: aggregate >= 70
PASS, no cell < 50 PASS, faults <= baseline PASS. Remaining: hallway probe, A6, Chris's two
hand-played games.** Report: FdgLab/reports/a5-5-gate. Next: A5-6 already code-complete
(Chris's second review pass - charge-band staging outside charge+2"-melee-cylinder threat
reach, boat-then-payload activation order, emergency disembark from doomed transports,
Takedown/single-model-spell sniping resolver, cargo-aware target/ward value,
shoot-what-threatens-you), gate to follow.

**2026-07-10 — A5-4 ANTI-HORDE PLAY SHIPPED (Chris-designed); GATE 79.4% MIRRORS / 77.4% MATRIX,
ZERO FAULTS - ONE CELL LEFT BELOW 50.** Engine `f-see-log` (A5-4 commit). Chris's design: screen
with expendable bodies (spent transports, the BB tank), shoot the horde before racing markers,
break mobs with concentrated fire. Implementation: (1) MoveScreen credits endpoints on the lane
between the biggest melee threat and our most valuable OTHER unit x the ward's threatened value
- the M8 Block / M9 Escort candidates existed all along, nothing paid them; deliberately NO
who-may-screen gate (retaliation prices each unit's own cost of absorbing the charge, so Tough
tanks and empty transports screen and casters do not). (2) MoraleBreakBonus 1.3x for volleys
expected to push a unit below HALF strength (the engine's own rout mechanic - break, don't
shave); needs CombatMath.ExpectedKillsFrom (public wrapper on the allocation mirror). (3)
ObjectiveUrgency scales the objective terms ~0.66 (round 1) -> 1.3 (final round). 3+1 pins;
suite 1554/1554. 50-game probes first: BB-vs-Orks 49 -> 61, DE-vs-Orks flat 48. **Gate
(a5-4-gate): mirror avg 79.1 -> 79.4, matrix 79.2 -> 77.4 (parity within #210 noise +
redistribution), faults 0/1800. Below-50 cells 2 -> 1: BB-vs-Orks 49 -> 58, BB-vs-HEF 37 -> 51;
remaining straggler DE-vs-Orks 46. Watch: BB-vs-RL 82 -> 63 (more conservative BB; still
comfortable).** Queued next (Chris review 2026-07-10): A5-4b screen tweaks - ward threat as
EXCHANGE MARGIN (a counter-blade powerhouse ward needs no screen) + one-screen-per-lane
(no dogpiled screens); DE disembark timing investigation (does cargo ever leave the boats
proactively, or only on spillout?); cargo-aware transport value; speed-differential kiting;
shoot-what-threatens-you. Chris's read on DE-vs-Orks: possibly a genuinely one-sided matchup,
but it should still beat the dumb bot (>50).

**2026-07-10 — A5-4 ANTI-HORDE SCORING PROBED AND REVERTED (negative result).** The 49%-cell
loss reading (BB/DE vs Orks): elite units take an early marker, hold a firing position, get
CAUGHT by the horde's melee elements in rounds 2-3 (BB s3005: APC eaten r2, a BB squad r3,
Battle Tank routed), and the horde's surplus bodies take every marker in round 4. Kiting
endpoints DO exist (EngageAtRange far-band aims can back away) - they lose the argmax. Two
scoring hypotheses probed on the cells' own 20 seeds: (a) soft-OR retaliation aggregation
(1 - prod(1-x)) + melee-threat factor 0.5 -> 0.75: BB 50, DE 45 (tie-heavy - near a horde every
endpoint saturates to "dangerous", differences flatten, the army turns passive); (b) max
aggregation + 0.75 factor alone: BB 47.5, DE 47.5 (DE's fast transports WANT to operate close -
pricier melee threat makes them shy). Neither beats shipped A5-3 (BB 50, DE 49-60 on the same
seeds); both reverted, no engine change. Takeaway: the anti-horde lever is BEHAVIORAL
(screening, focus-fire to break mobs, or true kite-cycles), not a constant nudge, and 20-game
probes are too noisy for weight deltas this small - use 50-100 games for any future retune.
The two cells sit at parity (49) and do not block practical play; candidates for the next
session alongside the hallway probe and A6.

**2026-07-10 — A5-3 OBJECTIVE GRADIENT SHIPPED; GATE 79.1% MIRRORS / 79.2% MATRIX - THROUGH THE
70% A-GATE AGGREGATE.** Engine `26eb326`. Mechanism (from G2 log-reading the a5-2-gate DE-vs-Orks
losses): ObjectiveDelta pays only ON the marker, so a unit two moves out had no reason to close -
shooter armies froze against hordes (offense 0 out of range, retaliation punishes proximity =>
Hold/Pass; DE units PASSED their round-4 activations while Orks walked onto the markers). The
melee-approach bug's exact twin, on the other win condition. Fix: ObjectiveApproach pays
MoveObjectiveApproach (0.4) x the fraction of the gap closed toward the nearest not-ours
objective; below MoveObjective (0.75) so arriving still dominates. 1 pin (shooter far from an
uncontested marker with a looming out-of-range horde must walk, not pass); suite 1551/1551.
20-game probe on the worst cell first: DE-vs-Orks 23 -> 60. **Gate (a5-3-gate, hash
63AC904B902B3D1D): mirror avg 61.9 -> 79.1, matrix 63.5 -> 79.2. Every mirror >= 64 (Hives 85,
BB 78, DE 90, Dwarf 81, HEF 90, HDF 64, Orks 78, RL 67). A-gate criteria: aggregate >= 70
PASSED; "no matchup < 50" NOT YET - BB-vs-Orks 49.0 and DE-vs-Orks 49.0 (one game each);
faults 2/1800 vs baseline 1, but ZERO Tactician-attributable: one is #208 (triggered-move
cohesion, baseline family), one is NEW #211 - the SOLO mover pathing through impassible terrain
during its own activation (repro'd; solo-side, #159's family).** Remaining for the A-gate: the
two 49% cells (both "shooters/transport vs Ork horde" - the next lever is likely kiting /
focus-fire, not objectives), the hallway probe, A6 selection UX, and Chris's >= 2 hand-played
games. Report: FdgLab/reports/a5-3-gate.

**2026-07-10 — A5-2 AMBUSH/RESERVES SHIPPED; GATE 61.9% MIRRORS / 63.5% MATRIX, ZERO FAULTS -
DWARF MIRROR 66->84.** Engine `6e6f523`. Neither bot ever used Ambush (solo always answers
"Deploy normally"). Now: AmbushPolicy holds melee/short-range Ambushers (max weapon range <
18" - they skip the approach march; long-range units keep their round-1 shooting), capped at
half the army's living units so the table is never conceded; the hold prompt is answered
explicitly both ways (never "Back to unit list" - the deploy-picker loop). Arrivals aim at the
most WINNABLE objective (not-ours -> fewest enemies within 9" -> nearest table centre; the
engine's spiral search enforces the clearance); Scout placement ("Place Scout Unit") reuses the
objective-aware deployment aim. Arrival TIMING stays the engine default (first opportunity,
round 2) - deferring arrival is search-level judgment (Phase B); dropping beside enemies to set
up charges is a recorded deferral. 4 pins; suite 1550/1550. G2: Dwarfs hold Jetpack
Warriors/Miners, arrive round 2, seed 3050 flips to a win. **Gate (a5-2-gate, hash
BED656997B7235ED): mirror avg 56.3 -> 61.9, matrix 58.8 -> 63.5, faults 0/1800 (baseline v4:
1). Dwarf row transformed: mirror 66->84, vs Orks 29->63, vs HDF 54->64, vs HEF 53->65, vs RL
48->69. Hives row also up broadly (65-94); Orks mirror 44->51, RL mirror 36->49.** Solo pool
baseline v4 frozen: hash `64A59D65881C48A6`, 1 fault/1800 (#208 family; note #210 - DOP-16
hashes only approximately reproducible). Remaining weak cluster is now sharply defined:
Tactician-as-shooters/transports vs Ork horde (DE-vs-Orks 23, BB-vs-Orks 30, HDF-vs-Orks 33)
plus the HDF rows generally (mirror 45, vs RL 40) - anti-horde defense
(screening/kiting/focus-fire) and Tough/vehicle handling, not casting or reserves. A-gate
check: aggregate 63.5 vs the 70 target, 9 cells below 50. Next: G2 log-read the weak cluster
before choosing the next slice.

**2026-07-10 — A5-1 CASTING SHIPPED (engine `0b0c0f7`) + #209 DETERMINISM FIX (engine `52d1968`,
Chris-authorized); GATE 56.3% MIRRORS / 58.8% MATRIX, ZERO FAULTS - HEF MIRROR 66->77.** A5-1:
Cast is LAYERED (loops back to Choose Action without ending the activation), so the planner takes
any positive-EV cast FIRST - checked before the post-move branch too, which is what pays off M11
MoveToCast set-up moves. SpellValuation prices damage spells through a new
CombatMath.EstimateSpellDamage (fixed hits through the save/wound mirror; the stage's
hit-complete fold on spell hits - Blast multiply - is a recorded gap); non-damage effects get the
flat CastEffectStaticFraction placeholder (plan A5; real buff value arrives in C). Net EV = 0.5 x
target sum - tokens x CastTokenValue. Pickers are livelock-safe BY CONSTRUCTION: spell pick =
argmax over the ENGINE's offered labels, never Cancel (a cancelled pick re-enters Choose Action
unspent); target pick never cancels before MinCount (same loop), stops adding targets when value
runs out after it. TacticianCastAssistResolver spends tokens when a 1/6 threshold shift beats
CastTokenValue, friend-boost and enemy-deny alike (solo always declines). G2 verified in logs:
spell picks, casts, and a +2 assist turning a 4+ into a 2+. 6+2 pins; suite 1546/1546. Deferred
(recorded, not silent): ability-effect choice + pre-attack ability menus (solo first-option),
single-model spell target pick (solo), granted-token buff read-back (existing evaluator gap).
**#209 (found during G2 verification): weapon-choice options were built by enumerating a
Weapon-keyed dictionary in identity-hash order - multi-weapon units swung/fired in RANDOM order,
so same-seed games did not replay (predates A5; hit the solo baseline too - two identical
10-game benches gave different hashes). Fixed at both stages (deterministic option order),
pinned by WeaponOrderDeterminismTests; serial runs now reproduce hashes exactly across
processes. Residual DOP>1 flips = #210 (contention race, trace-diff tooling added to bench).
Consequence: pre-fix gate hashes are historical one-shots; this gate is only loosely comparable
to A4b-2's because #209 changed both bots' weapon order in every multi-weapon game.**
**Gate (a5-1-gate, hash 53E1E8837F86AC8E): mirror avg 56.3 (was 57.1), matrix 58.8 (was 58.7),
faults 0/1800. A5 verify criterion (caster matchups improve or hold) PASSED: HEF mirror 66->77,
HEF-vs-HDF 73->79, HEF-vs-RL 82->86. Scattered moves elsewhere (DE-vs-Orks 33->23, RL mirror
45->36, Hives-vs-HDF 66->72) are consistent with the #209 baseline shift.** Solo pool baseline
v4 re-freeze pending (v3 hash is pre-#209). Report: FdgLab/reports/a5-1-gate. Next: A5-2
ambush/reserves - neither bot uses Ambush at all today (solo always answers "Deploy normally"),
so this is the Dwarf list's whole signature mechanic.

**2026-07-10 — A4b-2 OBJECTIVE PLACEMENT SHIPPED; GATE 57.1% MIRRORS / 58.7% MATRIX.** Engine
`dd0b1f1`. TacticianPlaceObjectiveResolver: zones are chosen AFTER objectives, so the
side-agnostic lever is cluster-vs-spread along X - an army whose model-count majority carries
>=18" guns clusters the markers around centre at MinSeparation steps (one firebase covers
them all); everyone else races them wide (+/-0.7 x half-width, first marker central). Z
reflects the existing-marker centroid through the band centre (solo's balancing idea,
deterministic - no RNG). Legality via public ObjectivePlacementValidator.Check on a 1" grid
sorted nearest-to-target, same as solo. 3 pins in TacticianObjectivePlacementTests; suite
1538/1538. **Gate: mirror avg 54.4 -> 57.1, matrix 54.4 -> 58.7, faults 1/1800 (= baseline,
#208 cohesion family). BB mirror recovered 42->50 (the A4b watch item), RL mirror 45 (still
soft). Six of eight mirrors >= 50; Hives rows dominant (60-94).** Weakest cells now
Tactician-as-shooters vs Orks horde: BB-vs-Orks 22, HDF-vs-Orks 36, Dwarf-vs-Orks 36 -
anti-horde defense (screening/focus-fire vs bodies), not obviously an A5 casting/reserve
gap; watch after A5, may need a weight pass. Dwarf rows + HEF-as-opponent rows remain A5
scope (ambush timing, casting). Report: FdgLab/reports/a4b2-gate (hash 05AE804C8A32F2EB).
Next: A5 casting/abilities/reserves.

**2026-07-10 — A4b DEPLOYMENT SHIPPED; GATE 54.4% MIRRORS / 54.4% MATRIX - FIRST GATE ABOVE
PARITY.** Engine `bb971b1`. Mechanism: the solo placement resolver's only strategy knob (the
preferred block centre) became a protected virtual seam - solo's fan-out is the unchanged base
implementation (pinned bit-identical by TacticianDeploymentTests' disembark comparison + the
in-suite determinism hashes); TacticianPlaceObjectsResolver overrides it for DEPLOYMENT
requests only (TaskName discriminator "Place Unit Models"): units spread across objectives
nearest-to-zone-first, melee crowds the forward edge, shooters stand 6" back (12"-range units
3"). Non-deployment placements (disembark/spillout/ambush/reposition) ARE the solo resolver.
4 pins; suite 1535/1535. **Gate: mirror avg 49.0 -> 54.4, matrix 47.4 -> 54.4. HEF mirror
45->68, DE 61->77, Dwarf 44->58, Hives 43->54; regressions BB mirror 55->42 and RL 60->46
(static gunlines may dislike clustered deploys - watch after A4b-2/A5, retune depth if it
persists). Faults 2/1800, both #208's triggered-move cohesion family (baseline has 1) - no new
fault modes.** Scope note (not silent): cover-aware centre choice deferred to a later A4b
sub-slice; deployment ORDER (which unit next) and zone choice stay solo. Report:
FdgLab/reports/a4b-gate. Next: A4b-2 objective placement (side-agnostic: zones are chosen
AFTER objectives, so the profile lever is cluster-vs-spread, not own-side).

**2026-07-10 — #207 MOVE-THROUGH FLAVOR FIXED (Chris-authorized engine core) + A4-4 SHIPPED;
GATE 49.0% MIRRORS, ZERO FAULTS.** Engine fix (`ebd2c8f`): GetEnemyModelFootprints and
GetEnemyUnitsMovedThrough skip off-battlefield units - embarked models parked at (0,0) no
longer form an invisible wall at the table corner. Pinned by EnemyFootprintTests (embarked
cargo leaves no footprint; deployed enemies still obstacles). Verified: seed-3000 Hives-DE
fault repro now plays out (Hives win 2-0); 100-game Hives-DE matchup 0 faults (was 12/50),
Tactician 81%. **Solo pool baseline re-frozen: v3 hash `0888D6E37A1F11E8`** (v2
CC04AE4A5C713492 stale - the fix changes transport-game outcomes); 1 fault/1800 remains,
triggered-move cohesion = #208 family, NOT #207. A4-4 (`580e194`):
TacticianAssignWoundsResolver - the engine machinery already enforces every ordering rule and
TryAddWounds pours full capacity per pick, so the decision is fill ORDER; greedy min
output-lost-per-wound-absorbed (static weapon score attacks x AP factor; special rules not
weighed - recorded gap). Mixed units lose cheap bodies first; Tough models soak partial
volleys; AutoFill fallback so it can never fault (G3). 3 pins in
TacticianWoundAssignmentTests. Suite 1531/1531. **A4-4 gate (1800 games, seeds 3000+):
mirror avg 49.0% (was 47.1%), matrix 47.4% (was 45.9%), faults 0/1800 vs baseline 1/1800 -
fault criterion passed clean. HDF mirror 34->39, Dwarfs 37->44 (wound assignment helping the
Tough-heavy lists).** Weakest rows now: BB-vs-Orks 24, DE rows vs melee ~24-29, Dwarf rows
27-36 (ambush timing = A5 scope). Reports: FdgLab/reports/{207-fix-hives-de,
pool-baseline-v3, a4-4-gate}. Next: A4b deployment + objective placement.

**2026-07-10 — OPTION (a) SHIPPED: MELEE APPROACH TERM; THIRD GATE 47.1% MIRRORS (from 25.4%)
- collapse fixed, fault regression root-caused to engine core (awaiting Chris).** Chris picked
option (a). Three-part fix (engine `5dc976d`, all inside Ai/Tactician): (1) generator - an
out-of-charge-reach M5 candidate now emits a RUSH-budget approach move toward a 1.1"-standoff
point on the lane to the nearest enemy model (before: an unplayable charge-budget move that
ActionNameFor discarded, so melee units outside 12" had literally no candidate that closed
distance); (2) planner dispatch keys on ActionType - Charge-typed candidates map to Charge,
the Rush-typed approach plays as a plain Move; (3) Score adds `MoveApproach=0.75 x exchange
margin-if-reached x fraction-of-charge-gap-closed` (cached per enemy per activation), zeroed
once in reach so real charges still dominate; the reachable-charge offense branch now also
requires ActionType==Charge so a reached standoff point is not scored as a fight. Pinned by
MeleeUnitOutOfChargeReach_ApproachesInsteadOfStanding (brawlers 24" out must close >= 6").
Suite 1526/1526. **Gate (a4-approach-gate, 1800 games, seeds 3000+): mirror avg 47.1%, matrix
45.9%. Melee mirrors: Hives 7->47, Orks 5->38; shooters held (DE 62, BB 51, RL 60). Six of
eight mirrors within noise of parity or above.** Remaining below: HDF 34 (Tough/vehicles -
wound-assignment and target-saturation, A4-4 territory), Dwarfs 37 (ambush/scout timing = A5).
**Faults 17/1800 vs 9 baseline - REGRESSION, but root-caused to an ENGINE-CORE bug the
approach behavior merely tickles more often** (embarked models parked at (0,0) count as
movement obstacles at the table-origin corner; full writeup + candidate one-line fix in #207;
faulting moves are legal per the real rules). Engine fix is outside the authorized seam -
stopped and asked Chris. Reports in FdgLab/reports/a4-approach-gate.

**2026-07-10 (overnight) — SECOND A4 GATE FAILED (25.4%); STOPPED per plan sec. 13. Analysis for
Chris below; no further weight iterations without his direction.** Cumulative A4-2(retuned)+A4-3
gate, mirrors: Hives 7, Orks 5, Dwarfs 12, HEF 15, HDF 24, RL 26 - but **Dark Elf 62 and Battle
Brothers 52: the two SHOOTING armies WIN their mirrors.** That split is the mechanism, confirmed
by reading a Hives game (G2): an all-melee mirror produced only ~8 melee engagements in 4 rounds -
Tactician brawlers barely fight. Why: the greedy one-step score gives a melee unit outside charge
reach NO reason to approach (offense=0 beyond 12", every position near the enemy scores
-retaliation), so melee armies dither/kite while solo's Charge>Move priority marches in, wins the
attrition war, then takes the objectives. Shooting armies don't have this hole - their one-step
damage calculus is correct at range - and they beat solo. **This is the anticipation gap the plan
assigns to Phase B search (D6); greedy was always going to be weakest here.**
Options for Chris (recommendation first):
(a) RECOMMENDED - add an approach term for melee units: progress toward the best charge target
    scaled by the expected margin-if-reached (a one-line proxy for next-turn value; plan A4's
    'small terms' clause covers it). One more gate run decides it.
(b) Hybrid interim: Tactician planner defers to solo behavior for melee-only units, keeps its
    (winning) policy for shooters - ships a strictly-better-than-solo bot today, ugly but honest.
(c) Accept A4 as scaffolding and pull Phase B (search) forward - the failure is exactly what
    search fixes, but it leaves the A-gate unpassed.
Faults 9/1800 (Dark-Elf #207-family; profile attribution still TODO). Suite 1525/1525 throughout;
all code pushed (engine `8c17102`). Gates archived in FdgLab/reports/a4-2-gate + a4-3-gate.

**2026-07-10 (overnight) — A4-2 + A4-3 SHIPPED; A4-2 GATE FAILED (23.75%) -> weights retuned;
cumulative re-gate running.** A4-2: TacticianPlanner scores (action x macro-action) pairs at
Choose Action (value-weighted damage - retaliation + objective delta), caches the winner, plays
it out at the movement request with request-budget re-validation and solo fallback (G3). Perf
war: 508ms -> 68ms per decision (one lazy shared TerrainGrid per enumeration; straight-clear
paths skip the grid). A4-3: value-weighted shooting target choice (CombatMath EstimateVolley per
selectable weapon x target, kill bonus) + melee defender by exchange margin; ChooseMeleeDefenderRequest
split from the generic cancellable selection (A4-1 pattern; adapters keep CLI/GUI dialogs and
solo behavior identical - solo hashes stable). Suite 1525/1525.
**THE GATE LESSON (G2/G4 doing their job): A4-2's first gate scored 23.75% mirror average** -
Hives 4%, Dwarfs 7%, Orks 7% - a collapse, not a tuning miss. Root cause read from the numbers:
objective terms were FLAT bonuses (2.5 move / 2.0 activation) while damage/retaliation terms are
value-fractions (~0.0-0.5), so every unit rushed objectives (Rush = no shooting), never fought,
and solo's brawlers cleared them then took the table. Retune: objective terms onto the same scale
(0.75) - a flip outranks a good exchange, not ten. Cumulative A4-2+A4-3 re-gate running (seeds
3000+, timeout 240). Per plan sec. 13: if this second attempt also fails the gate, STOP and
present analysis to Chris (one weight iteration is spent). Also noted: 7 Dark-Elf-game faults in
the failed gate (#207-family signatures, 5x "moves through an enemy unit" new flavor) - needs
profile attribution (TODO in #207); Tactician games ~12.6s wall (thinking is real; G6 later).


**2026-07-10 — A4-1 GATE + post-#199 baseline recorded.** Baseline v2 (solo-vs-solo, fixed engine,
36x100, seeds 1000+): hash `CC04AE4A5C713492` - THE frozen solo reference now (v1's
`3AC9C6FA0B50D590` was pre-#199). A4-1 gate (tactician-vs-solo, 36x50, seeds 3000+): hash
`94AA56B0A094DAD0`. **Mirror average 52.75% for the Tactician** (Robot Legions 64, Hives 59,
Dwarfs 56, HDF 52, BB 51, HEF 50, Orks 48, Dark Elf 42; N=50 each, so single-mirror noise ~7pp,
average ~2.5pp) - the small positive nudge expected from activation order alone; movement is
still solo. Faults 4/1800: three #207-class (consolidation standoff/move-through, all Dark Elf
transport games) + one 120s watchdog on a Hives-DarkElf game (baseline showed legit 2k games
reach 103s - pool runs should use --timeout 240; noted). Cross-matchup rows mix army strength
with profile and are not read as profile signal. NEXT: A4-2 (action+movement onto the
MacroActionGenerator) - Chris authorized continuing overnight; then A4-3 (shooting/melee targets).


**2026-07-09 — A4-1 SHIPPED (activation order + request split); #199 FIXED; first pool baseline.**
A4-1: `ChooseUnitToActivateRequest` split out per Chris's call (type dispatch - which immediately
caught the string version matching Instructions vs the auto-generated TaskName: it would have
silently no-opped); `DerivedRequestAdapter` forwards to existing base-type resolvers in all three
sets, GUI canvas dialog unchanged (shared instance - Chris to eyeball next GUI session);
`TacticianRegistry` = own resolvers over a solo fallback; urgency scoring (value-weighted kill +
flip + threat, weights in TacticianWeights). A0 identity pin retired per its own instructions;
3 behavioral tests replace it. Solo hashes identical (split is behavior-neutral).
**#199 fixed** (Chris-authorized): the float-identity trio in AssignWoundsResults - guard compared
RemainingWoundsBinding against its own double-rounded round-trip; exact-equality finish check;
ULP residues as "room". WoundEpsilon (1e-4) + one capacity formula; four-seed graveyard pinned +
mutation-verified. Suite 1518/1518.
**First 2k pool baseline (solo-vs-solo, 36 matchups x 100, realistic dice, PRE-#199 build):**
hash `3AC9C6FA0B50D590`, 2.79 games/s (10k/hour), mean 5.7s/game, mirrors ~48-52%, real archetype
signal (Hives 80% over elite shooting, HEF casters 64% over Hives). 7 faults / 3600 (~0.2%) - NONE
were #199 (realistic dice): two NEW classes filed as **#207** (AI standoff-violating moves, Dark
Elf transport list, rect-base geometry suspected - kin of #206) and **#208** (#197's triggered
moves lack the G3 validate-or-decline ladder). Baseline + A4-1 gate re-running on the fixed build;
gate numbers land in the next entry.

**2026-07-09 — #200 + #203 FIXED (Chris-authorized engine-core changes); POOL 8/8 GREEN.**
#203 first (its verification needed #200's livelock alive): Task.Yield at the activation boundary
+ Choose Action entry - the livelock then idled to a clean watchdog Fault at DEFAULT stacks
instead of killing the process; bench hashes unchanged on both matrices (outcome-neutral). Then
#200: instrumentation of the bounce branch revealed the real state - the Orc Bikers' Rocket-Mod
is Limited+Deadly and SPENT, and Deadly-first gating ran before Limited-spent gating, so the
empty rocket locked out every other weapon while the Shoot gate (no Deadly gating) said
"fireable". Fix: gating order swapped AND gate/stage now share one pipeline (ApplyTargetGating)
so they can never disagree again; 2 regression pins. Orks mirror now plays 4 full rounds (3.5s).
**All 8 pool mirrors complete**; suite 1511/1511; builtin hashes stable (pool-army trajectories
legitimately shifted - wrongly-locked-out units now shoot). Both items archived. The pool is
ready for A4's first benchmark baseline. Also filed at Chris's request: #204 (save-roll beats
for Rending vs non-Rending groups pace too close together - presentation only).

**2026-07-09 — BENCHMARK POOL DELIVERED by Chris; 7/8 validated; #200/#203 filed off the 8th.**
Eight 2k armies now in `FdgLab/armies/` (moved out of the engine submodule per D3): Alien Hives
horde melee, Battle Brothers elite shooting, Dark Elf Raiders transport, Dwarf Guilds
ambush/scout, High Elf Fleets caster, HDF tough/vehicle, Orks horde mixed, Robot Legions mixed.
**Throughput (G6, measured):** 2k mirrors run 1.4-2.5s wall each, 200-420 decisions - barely
above the tiny test armies; the 5-15x slowdown fear was wrong; Phase C/D volumes are unthreatened.
7/8 play clean full-length mirrors with real objective scores. The Orks mirror exposed two
engine bugs, filed: **#200** (Choose Action offers Shoot with zero fireable targets ->
deterministic AI livelocks; GetCanShoot lacks the target gate GetCanCast already has) and
**#203** (stage transitions chain synchronously; stack depth grows with game length; the loop -
and eventually any long game - kills the process with an uncatchable StackOverflow;
DOTNET_DefaultStackSize=0x4000000 is the lab's interim shield). Both fixes are engine-core
(outside Ai/Tactician) -> awaiting Chris's go per D2. Pool baseline matrix + A4 start once #200
is resolved (or run 7-army in the interim).

**2026-07-09 — A3c-2 DONE; A3 COMPLETE (all of A3a/b/c verified).** M11 MoveToCast (spell-token
holders + army spells via TableState.Armies; goal just inside the best affordable spell's range
of its affinity target; Self-affinity skipped; LoS not modeled - recorded; one candidate per
activation) and M12 DeliverCargo (loaded transports - IsTransport + GetOccupants - route toward
the nearest unowned objective as the cargo-plan proxy) complete the confirmed vocabulary. Float-
margin bug fixed on the way: movers take the epsilon, validators keep the full budget (the
ResolverGuide gotcha, caught by the DeliverCargo test at exactly one ladder halving). **The A3
feasibility gate metric PASSES:** new FdgLab instrument (`probes --feasibility`) shadow-runs the
generator at every real movement decision of benchmark games (JSON-path interception; decision-
neutral - the solo bot still plays): builtin mirror 597/597 activations with a valid non-Hold
candidate, builtin-vs-builtin-basic 464/464 - **100% vs the >= 95% gate**, zero generator
faults. Suite 1509/1509 (4 M11/M12 tests added). Engine `6ad58b5`; lab instrument in the
superproject commit. **Next: A4 (greedy decision policy)** - replace delegated resolvers one
request type at a time, benchmark after each; needs Chris's 2k army pool for meaningful scores.

**2026-07-09 — A3c-1 (MacroActionGenerator, M1-M10) DONE.** `Ai/Tactician/MacroAction.cs` +
`MacroActionGenerator.cs`: goal enumeration per confirmed Appendix A - Hold (always), objective
advance/rush (both budgets), EngageAtRange with the three bands (SafeShooting/kite exists only
when own reach exceeds the enemy's threat envelope; endpoint may open the distance - verified),
ChargeToContact (solo-style explicit-end-gap construction when the lane is clear, path-planner
route otherwise; feasibility graded by ACHIEVED gap), FallBack, SeekCoverFrom (far side of the
nearest Cover piece), Block (LINE spread perpendicular to the LANE via the new lineAxis
parameter - the first draft spread across the approach and the test caught it), Escort
(interpose toward the ward's nearest threat), Concentrate. Every move is ladder-built (G3);
every candidate carries feasibility (Reachable/BudgetClipped/Blocked) + a G12 rationale string.
Diversity-preserving pruning: rank-by-feasibility within family, round-robin across families,
round 0 completes even past the budget (>=1 per family guaranteed - tested at budget 6). Two
planner fixes shaken out by the tests: ClampRepackStep pre-clamp in BuildPathCandidate (first
candidates were over-budget and the ladder halved real moves - Concentrate under-moved), and the
charge construction above. **Verified:** 10 tests incl. the GATING generator-level hallway probe
(objective beyond a 4" corridor -> traversing candidate emitted, >6" progress) and
every-emitted-move-passes-ValidatePaths. Suite 1505/1505; bench hash unchanged (B05AA1D810364C6B,
solo-rules untouched). **Sub-slice split (G7), recorded:** A3c-2 = M11 MoveToCast + M12
DeliverCargo (need casting/transport queries) + the benchmark-sampled >=95% feasibility metric
(shadow-generator instrument in FdgLab). Next after that: A4 greedy policy.

**2026-07-09 — APPENDIX A v2 CONFIRMED by Chris (the A3c gate, plan sec. 5). A3c is go.**
One edit folded in at his direction: mid-game MoveToEmbark cut from M12 (post-deployment
embarking almost never useful - seen once, transport had Flying; revival condition recorded in
the appendix: gate on transport mobility >> cargo mobility). Deploy-time embark stays.
Also decided with him: **benchmark pool = ~8 armies at 2,000 points, uniform** (his argument
carried: real games are 2k+, strategy differs with scale - objective spread vs concentration -
big games pass through small-force regimes as attrition bites, and low-point lists under-sample
novel units). Chris is building the armies now; suggested archetypes given (the sec. 6.1 six +
a transport list + a second-faction repeat). Throughput cost to be measured on the first real
2k army (G6). C-gate rider recorded: one held-out pair at a different point level probes
generalization across game size. Plan doc updated in the same commit (appendix header, M12
entry, sec. 5 trigger marked satisfied, sec. 6.1 pool spec). Bycatch this exchange: Army Forge
gained an editable points limit (was hard-coded 1000; superproject `00132d3`).

**2026-07-09 — A3b (grid pathfinding) DONE.** `Ai/Tactician/GridPathfinder.cs`: `TerrainGrid`
(1" cells over the table, blocked/difficult by degenerate swept-disc tests, inflated by base
radius - the validator's own Minkowski semantics), A* (8-connected, no corner cutting, octile
heuristic, difficult cells x2 as a route PREFERENCE - the rules-true 6" whole-move cap is applied
by the caller), string-pulled polylines, `AdvanceAlongPath` (arc-length walk reporting passed
waypoints + difficult crossings). `MovementPlanner.BuildPathCandidate` (all models share the
path's interior waypoints - the unit funnels through corridors - and fan into Grid/Line formation
at the endpoint; arc length is the ladder's backoff knob) and `PlanMoveToward` (grid -> path ->
difficult cap -> G3 ladder; straight-line fallback when unreachable or flying). Multi-leg
`ModelMoveEntry.Positions` carries the corridor legs, so the engine validates the true route.
**Verified:** 7 authored-terrain tests - straight-when-clear, routes-around-wall (no leg clips
impassible), THREADS THE 4" CORRIDOR (plan D5's canonical failure of angular skirting), sealed
goal -> null (infeasible, not wrong), mid-leg budget stop, difficult-route 6" cap end-to-end,
corridor composition passes MovementUtilities.ValidatePaths and gains >4" toward the goal. Suite
1479/1479; bench hash unchanged (B05AA1D810364C6B - solo-rules untouched, as intended: nothing
calls PlanMoveToward until A3c/A4). Perf note (G6): grid built per query, a few thousand point
tests - optimize only on profiler evidence. NEXT IS THE HARD GATE: Appendix A v2 confirmation
with Chris before A3c (plan sec. 5) - A3c must not start without it.

**2026-07-09 — A3a (MovementPlanner extraction) DONE.** `Ai/Tactician/MovementPlanner.cs`: the
solo-rules move-construction mechanics moved verbatim behind shared primitives - `BuildCandidate`
(single-step vs formation re-pack, with the step<=0 -> StayInPlace degenerate preserved exactly,
dead models' zero-length paths included), `RefineStepTowardGap` (measure-and-correct, 3
iterations), `ValidateWithBackoff` (the G3 ladder: halve to min step -> reform-in-place -> hold
exact), `StayInPlace`/`HoldExactPositions`/`LiveEnemyFootprints`/`MinEnemyGap`, tuning constants.
`AiDefineMovementResolver` keeps only policy (archetype, nearest-enemy targeting, terrain
skirting, difficult-terrain clamp) and delegates the mechanics. NEW: `PackLine` + the
`EFormation {Grid, Line}` flag (Appendix A M8's barrier shape; perpendicular-to-move by default),
with rank-wrap so a long line never breaks the 9" coherency rule.
**Pinned (D1):** the 8 AiDefineMovementResolver tests + 7 CohesiveFormation tests green unchanged;
suite 1472/1472 (+3 PackLine tests); and the decisive instrument - 200-game benchmark outcome
hashes on both matrices, captured fresh immediately before the refactor and re-run after:
builtin `B05AA1D810364C6B`, builtin-basic `F4318EF0D91161F5`, BOTH IDENTICAL pre/post (they also
still match the #198-era values, so #196/#197's parallel landings didn't shift these
trajectories either). Deferred, recorded: `AiConsolidationMoveResolver` still owns its own
consolidation logic - migrate onto the planner only if A4 needs consolidation policy (avoid
speculative churn). Next: A3b (grid pathfinding), then the HARD GATE - Appendix A confirmation
with Chris before A3c.

**2026-07-09 — A2 (TacticalAnalysis) DONE.** `Ai/Tactician/TacticalAnalysis.cs`: mobility queries
(Advance/Rush reuse `MovementRuleQueries`; `ChargeDistanceAgainst` composes the unit's charge
budget + the target-conditioned query exactly as DefinePathStage does - first draft wrongly fed
the BASE charge into the per-target query and the Fast test caught it); `ThreatRangeAgainst`
(max of advance+longest-effective-weapon-range and charge reach - the M4 kite band's input);
`ExpectedShootingAt` (CombatMath at a hypothetical distance/cover); objective projection
(`ProjectObjectives`/`ProjectedScore` mirroring ReconcileObjectivesStage: base-edge distance
within 3", sticky owner, contest-to-neutral, Shaken/reserve-arrival/Aircraft exclusions - the
radius + rules are a MIRROR of that stage's privates, noted in both files); `UnitValue` (runtime
units carry no point cost - UnitFileEntry.PointCost never reaches UnitData - so it is the plan's
f(wounds, quality, weapon output): sqrt(durability x (1+output)) vs a Q4/D4 reference).
**Verified:** 10 tests on authored states - base/Fast move+charge distances, threat ranges,
seize/contest/sticky/edge-distance/exclusion projection cases, value ordering on real HDF stat
lines (Infantry>Recruits, Storm Troopers>Veterans, Tank>all), value falls with casualties. Suite
1462/1462. **Honest calibration note:** the book prices Recruits (10 @ 75) BELOW GRUNT Robots
(5 @ 80) where the formula ranks them the other way - quality is weighted harder by the book than
by this v1; revisit only if A4's value-weighted targeting misreads benchmarks (G2). Special rules
deliberately don't contribute to UnitValue yet (recorded gap).

**2026-07-09 — A1 (CombatMath) DONE.** `Ai/Tactician/CombatMath.cs`: `EstimateShooting` (all
in-range weapon batches), `EstimateMelee` (impact hits, Counter strike-first swap - which also
strips the charger's IsCharging, exactly as the engine's role swap does - swings per weapon batch,
return strikes from survivors only, fatigue, Fear-adjusted resolution margin), `EstimateVolley`
(the pinned core). **Design refinement over the plan (G10 note added to sec. 8):** the "~15 named
rules" became *definition-driven* math - CombatMath mirrors the stages' arithmetic skeleton and
delegates all rule effects to the engine's own `RuleEvaluator.EvaluateAllNamed` (read-only: no
log spam, no one-shot-grant spending) with the same contexts/participants/sinks the stages use.
So the plan's candidates AND their ~hundreds of data-authored clone instances (Lacerate, Crack,
Shred variants, gated "when shooting/in melee" families...) all price themselves identically to
the engine, by construction. ("Poison" from the candidate list does not exist in the engine.)
**Verified:** 60 pin tests (`CombatMathPinTests`) drive the REAL stage chain per case and assert
|delta| <= max(0.05, 2%) - in practice exact: Q2-6 x D2-6 sweep, AP sweep, cover, Reliable,
Stealth both sides of 9", Shielded, Fortified (AP2+AP0), Rending+Regen, Crack, Regeneration,
Unstoppable, Bane, Lacerate, Shred, Surge, Relentless both ranges, Blast cap (big+small unit),
Deadly vs 1W and Tough(3), melee swing, Furious charge-gated both ways, Thrust, Fatigued-token
6s-only, plus composition tests (impact math, Counter flag + charge strip, Fear margin, survivor
return strikes, out-of-range = 0). Mutation-verified: naive Deadly multiply and skipped Bane
reroll each turn their pins red. Suite 1442/1442.
**Coverage table (what prices itself vs what does not):**
- Modeled (sink-folded at the 7 combat hooks): rollModifier, qualityFloor, addExtraHit,
  multiplyHits, perHitSaveModifier, reduceArmorPenetration, reroll(save), addExtraWound,
  multiplyWounds (clump-confined), ignoreWoundOnRoll, ignoreRule, ignoreCover, chargeImpactHits,
  reduceImpactDicePerModel, strikeFirst, extraMeleeWoundCount, setMaxWounds (via stats), fatigue.
- Modeled at runtime IF the caller passes the game's evaluator (token read-back needs its rule
  resolver): aura/addRule-granted rules. A bare evaluator prices static rules only.
- NOT priced (surfaced per-call via `AttackEstimate.Notes` where detectable): granted one-shot
  roll-modifier tokens (engine's only accessor consumes them - a Peek API is an engine-seam ask),
  target Mark claiming (mutates tokens), Takedown priced best-case vs healthiest model,
  per-volley casualty carry-over inside one attack, melee in-range subset (assumes all living
  carriers reach post pile-in), morale/movement/deployment/casting hooks (other slices' scope).
**Deferred, recorded:** book-wide generated attacker/defender matrix sweep (6.3's full form) -
the hand-built matrix covers every core combat rule; the sweep needs app-side book loading and
lands with the FdgLab probe tooling (A2+). Full-MeleeStage composition pin (PileIn geometry etc.)
- component math is stage-pinned; composition is covered by analytic tests.

**2026-07-09 — A0 (Tactician scaffold) DONE.** Phase A begun. Engine: `Ai/Tactician/`
(`TacticianOptions`, `TacticianResolverRegistryFactory` — A0 delegates every request wholesale to
the unmodified solo-rules resolvers), `EAiProfile { SoloRules, Tactician }` + `AiProfileFactory`
(the single profile->AI dispatch; moved the enum from FdgLab into the engine, per plan sec. 3).
App: `--ai-profile <solorules|tactician>` on the headless + `--scenario` paths (lobby selection
stays deferred to A6); FdgLab `smoke --profile-a/--profile-b`; `bench` per-side profile flags
deliberately deferred to A4 (first benchmark that needs them). Verified per plan A0: new
`TacticianScaffoldTests` (rich armies, seed 24601: Tactician game == solo-rules game, fingerprint
equality; plus self-reproducibility) — suite 1382/1382; seeded headless CLI transcripts
solo-vs-tactician byte-identical modulo per-run PlayerID GUIDs on BOTH a completing seed (42,
4 rounds) and a faulting one (5150); lab smoke tactician-vs-tactician matches solorules exactly.
Test-fixture refactor: shared `Tests/Doubles/TestArmies.cs` + `GameFingerprints.cs` extracted from
DeterminismTests (pure move). Bycatch: **#199 filed** (AutoFill faults on a ~0.0555 fractional
wound, deterministic at seed 31415, profile-independent) and a **deterministic #159 repro** (piped
headless seed 5150, noted in #159 — points at the CLI AutoAdvance as a submitter). Next: A1
CombatMath.

**2026-07-09 — #198 fixed same day; P3 (#194) gate now 3/3, all prerequisites COMPLETE.** Root cause
was a single unseeded `new System.Random()` in `PlaceTerrainStage`'s auto-layout thinning (Chris
called the terrain theory; the async-race suspicion was a red herring). Found via FdgLab's new
`GameTracer` (position-write trace interleaved with the log). Every determinism instrument now
agrees: 200-game bench hashes identical across runs on both army sets, rich-army engine test
(mutation-verified) pins it, seeded CLI runs byte-identical. **Phase B's replayable-rollout
prerequisite is met early** - the B0 spike no longer carries #198 risk. Also: zero #159 faults in
1,200 deterministic games (see #159 - old crash trajectories were fed by random zone terrain).
The ladder is clear: next is Phase A (A0 Tactician scaffold), per plan sec. 8.

**2026-07-09 — P3 (#194) shipped; gate 2/3.** FdgLab exists and works: 200-game seeded matrix in 38s
Debug (**5.25 games/s, ~450k games/day** at DOP 16 — comfortably above the plan's Phase C/D
assumptions), zero hangs, exactly symmetric mirror results, faults ~0.5-1% (all = #159, for which the
harness found an 8/10 seeded repro: `fdglab smoke --seed 1027 --repeat 10`). The harness's first real
catch is **#198**: seeded games are NOT run-to-run deterministic on rich army paths (movement paths
differ; ambush arrival flips) — #193 covered RNGs, but something timing- or identity-hash-ordered
remains. Consequences for the ladder: **Phase A can proceed** (win-rate statistics are unbiased noise;
the bench outcome hash simply won't match between runs yet), but **#198 must close before Phase B**
(search rollouts must replay exactly) — slot it with or before the B0 spike, which was already going
to stare at the same async-void plumbing. Baseline solo-rules-vs-solo-rules report archived in
`FdgLab/reports/` conventions; builtin mirror A-score 50.0% exact.

**2026-07-09 — P2 (#193) done, archived.** Determinism is now a tested engine invariant: same seed +
same build => identical game, and that holds with 16 games running concurrently in one process (the
cross-talk detector plan sec. 6.4 asked for). #194's benchmark can therefore trust its aggregates on
day one, which is why the order was swapped. Three things worth carrying into #194:
(1) **AI seeds key on slot ID, not PlayerID** — GUIDs are per-run; `GameRunner`'s `GameSpec` must pass
`(seed, slotID)` the same way, or seeded benchmarks silently drift.
(2) **Benchmark fingerprints must include objectives**, not just models. The solo-rules bot ignores
objectives, so a model-only comparison is blind to objective-placement nondeterminism (a mutation test
proved it). Same trap will apply to any FdgLab state hashing.
(3) **#195 filed and now fixed** (engine `a19e6ab`): resumed games played four MORE rounds instead of
finishing the four-round game. Resume is now round-count-correct, so Phase B's `SimulationService` and
the scenario probes can rely on it. Remaining prereq: #194.

**2026-07-09 — P1 (#192) done, archived.** Engine `9b1c0ba`. `GameResult` + `FDGServer.OnGameCompleted`
land the reward/benchmark signal the whole ladder depends on. Two findings worth carrying forward:
(1) the default headless game ends `Tie` with `scores=[0, 0]` because **all four objectives stay
neutral all game** — neither the CLI-EOF player nor the solo-rules bot ever moves within 3". That is
the baseline #191 exists to beat, and it means early benchmarks will be tie-heavy until Phase A's
objective awareness lands; the `score = wins + 0.5 * ties` metric (plan G4) already handles this, but
expect low signal from A0/A1 comparisons. (2) `EGameOutcome.Fault` is now emitted by the disconnect
and engine-fault paths, so #194's watchdog can distinguish a real tie from a broken game for free.
Remaining prereqs: #193, #194.

**2026-07-09 — Appendix A v2.** Chris reviewed the vocabulary and contributed seven plays:
bodyguard/escort, kite, mass (death ball), fatigue bait, block, move-to-cast, transport delivery.
Integrated as: new intents M9 Escort / M10 Concentrate / M11 MoveToCast / M12 DeliverCargo+
MoveToEmbark; kite folded into M4 as the SafeShooting band; ScreenLane generalized into M8
Block(e, asset); fatigue bait became the generator-wide *diversity-preserving pruning* rule
(sacrificial candidates must survive to be searched) rather than an intent. New implementation
flags: line-formation mode for the formation packer (A3a), fatigue in CombatMath features (A1) +
concentration features (C1), verify whether Cast permits same-activation movement (A5). v2
awaits Chris's confirmation of the refined form before A3c (see plan sec. 5).

**2026-07-09 — filed.** Plan authored during the Fable window from a three-agent codebase
exploration (existing AI map, engine interface assessment, special-rules variance) + hardware
check. Signed off: new-option-not-replacement, engine-side bot, in-repo FdgLab, Python+ONNX,
search-over-macro-actions with ML as evaluation. Next actions: (1) Chris edits Appendix A
vocabulary; (2) fresh-session dry-run review of the plan doc for ambiguity; (3) Chris curates
the benchmark army pool (~8 armies, archetype spread); (4) start #192/#193/#194.

## Outcome

(open)
