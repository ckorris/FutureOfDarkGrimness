# 365 — Cover as a habit: two-tier positioning for the Tactician

**Status**: open (slice 1 SHIPPED 2026-08-06; slice 2 handoff plan below, not started)
**Related**: #363 (facet 3 is REPLACED by this, see below), #364 (melee path-vs-straight-line,
still open), #191 (Tactician umbrella), #194 (FdgLab)

## Goal

The Tactician travels the way a squad crosses an urban street: it hugs cover on the way to its
goal, automatically, without anticipating where the enemy will be - and it never lets that habit
stop it doing the thing it came to do.

## The principle (Chris, 2026-08-06)

**Cover is a habit, not a plan.** It shapes HOW a unit travels, never WHETHER it pursues its goal.
Exactly one thing may interrupt a goal: a threat large enough that the unit will not achieve
anything at all.

Two supporting facts about this game, both verified in the engine:

- `GameWideConstants.NUMBER_OF_ROUNDS = 4`. Every unit dies at the end anyway; a unit's worth is
  what it does in four rounds. So goals dominate safety by a wide margin.
- `ReconcileObjectivesStage` scores at END OF ROUND on 3" presence, and ownership PERSISTS when
  uncontested. So dying costs you a marker you were taking or contesting, and costs nothing for
  one you already hold safely.

## Why facet 3 was wrong (and it is being replaced, not extended)

`HasLineOfSight(enemy_now, endpoint)` asks "can that model, standing exactly there, see this
spot". The question that matters for a THREAT is "can anything shoot me from that direction after
it moves". Worse, the answer was binary, so the score had a cliff - and a cliff can only produce
two behaviours, ignore cover or hide in it. It cannot produce "take the slightly bent route",
because bending a path 3" does not change a boolean. That is why `BlockedThreatShare` tuning was
unsatisfying at both 0.2 and 0.4: it was tuning the height of a cliff.

**Offense is a fact** (I shoot from HERE, NOW - exact LoS, hard zero, facets 1+2 stand).
**Threat is a forecast** (they shoot from somewhere, later - never a boolean).

## Tier 1 - the wall-hugging reflex (slice 1)

A bounded tiebreaker: `+ MoveCoverHabit * coverShare`, where `coverShare` in [0,1] is the
threat-weighted fraction of enemy SHOOTING that has no lane to the endpoint.

- **Weight is threat TO THIS UNIT** (Chris): reuse the per-enemy `EstimateShooting(enemy -> us)`
  the Score loop already computes, so a tank's AP4 Deadly(3) pair dominates the share when we are
  the tank and ten little guys dominate it when we are infantry. Existence is not threat.
- **Bounded by construction.** Cover can never move a score by more than `MoveCoverHabit`, so
  "never interrupts the goal" is a property, not a hope. Calibrating that one number IS the
  exchange-rate conversation (pins 4/5 below).
- **A share, not a boolean**, so it is smooth: sliding along a wall continuously changes how much
  of the incoming is muffled. Gradient -> bent routes instead of hiding.
- **Empty bearings are worth nothing** - an enemy that is not there contributes no mass, so
  blockers toward an empty table edge earn zero. (Chris's empty street: you hug the wall the noise
  is on.)
- **The engaged target is excluded.** If the plan is to shoot T you have chosen exposure to T
  deliberately, and its cost belongs to the retaliation term, not the habit. Without this the
  habit taxes the very thing the action exists to do - Chris's "will it refuse to shoot?" worry.
  Falls out nicely: LoS is symmetric but this measure is not, so the best firing position becomes
  the one exposed to your target and shadowed from everyone else.
- Threat terms otherwise go back to no sight discount at all (pre-#363 pricing, which measured
  85.70% - the best of the four gate runs). `BlockedThreatShare` is deleted.
- **Melee reach is the habit's second signal, with the opposite sign** (Chris, after slice 1's first
  cut shipped without it), each half normalised against its OWN kind of threat:

      habit = MoveCoverHabit x ( shadowed share of enemy GUNS - reachable share of enemy SWORDS )

  Each share in [0, 1], so the term is in [-1, 1] and its magnitude is still capped. A wall is worth
  something against bullets and nothing against swords.
  Deliberately crude: REACH only, with no attempt to decide whether terrain makes the charge safe
  (that is #364, and over-fearing is the safe direction). Exempted for an enemy we would happily
  charge - `MeleeApproachAgainst(...).Margin > 0` makes its melee an opportunity, not a threat, the
  same exemption the projected-melee branch already makes for a staged charge.

### Rejected: bearing bins

First draft binned enemies into 8 bearings and tested one ray per occupied bin to the bin's
mass-weighted centroid. Cheaper (<=8 tests vs N enemies) but WRONG in a common case: two enemies
flanking a building fall in one bin whose centroid lands INSIDE the building, reading "blocked"
when both have clear lanes. Per-enemy tests aggregated into a threat-weighted share cost the same
as the `ThreatSightFactor` they replace, use no fictitious points, and have no bin-boundary
discontinuity. The coarseness that matters is "does not anticipate enemy MOVEMENT", which the
share model already satisfies - it measures present geometry and only ever spends a bounded
tiebreaker on it.

## Tier 2 - the lethality gate (slice 2 - actionable handoff plan below)

The only term allowed to interrupt a goal:

> penalty = P(destroyed) x (what this unit would still have done)

Pricing the FORFEITED CONTRIBUTION rather than the death is what makes the doomed unit behave
(Chris): if it dies whatever it does, P is ~1 on every candidate, the term is near-constant, it
cancels in the argmax, and the goal term wins - the 2-of-10 remnant rushes the objective and soaks
a volley instead of freezing in cover. It also absorbs the earlier "useless to keep a unit alive
that will not achieve anything" idea, which had been a separate third term.

Triggers, both rules-grounded (`MoraleUtilities`: a failed test Shakens, or ROUTS a unit at half
strength or less; `GetIsAtHalfStrength` / `WoundsLeftUnitAtHalfStrength` already exist):

- **Wipeout** - expected wounds >= remaining wounds.
- **Morale knee** - expected wounds cross the half-strength line, turning a future failed test
  from suppression into deletion. Weighted by P(fail) from quality: a 3+ shrugs, a 5+ is a coin
  flip.

Curve ~0 below the knee, steep above, so "lose 2 of 10 to take an objective" is free.

**Cover optimism is cheap in Tier 1 and expensive in Tier 2.** Being wrong in the tiebreaker costs
a slightly different equally-good route; being wrong in the gate gets the unit deleted. So Tier 2
discounts `coverShare` hard - a blocked bearing still counts most of its mass. One scalar could
never serve both roles, which is exactly why 0.4 felt arbitrary and 0.2 felt like noise.

**Round decay is NOT a separate mechanism.** It is emergent from "expected remaining contribution"
being horizon-limited (Chris's correction: dying is not free in round 4 if you were contesting).

## Handoff plan for slice 2 (2026-08-06, written for the implementing session)

Chris signed off on the 2026-08-06 audit and this plan. Order of work: **2a first** (small,
de-risks the habit under the gate), then 2b. One slice at a time, verify + commit + ledger note
per slice, per the working conventions.

### Slice 2a - fix the melee denominator (a defect, found by audit, not by pins or pool)

`MeleeThreatTotal()` sums EVERY melee-capable enemy on the table; the numerator counts only what
reaches the endpoint. Against a melee-heavy army (say 8 melee units) one pack reaching the covered
corridor side is ~1/8 of the denominator: penalty ~ -0.006 against a cover bonus of up to +0.05 -
numerically adjacent to the 1b corridor failure this half of the habit exists to prevent. Masked in
the pins because both CorridorScene variants have exactly ONE melee enemy.

Fix: restrict the denominator to melee enemies whose threat could matter THIS activation - mirror
the numerator's reach test, but against the whole candidate envelope instead of one endpoint:

    relevant iff TacticalAnalysis.MeleeThreatReach(enemy, self, _evaluator)
                 >= Distance(now, enemyPos) - TacticalAnalysis.RushDistance(self, _evaluator) - 1f

(`now` = current centroid; RushDistance = the largest move any candidate makes - swap in a tighter
envelope helper if one exists; the -1f mirrors the numerator's slack.) Still endpoint-independent,
so the `_meleeThreatTotal` cache shape is untouched.

**Pin 17**: CorridorScene plus a second powerful melee unit ~40" away that reaches neither
endpoint. Assert the shadowed+charged side still LOSES to open+safe - and that pins 14/15/16 stay
green (the distant blob must change nothing they assert).

### Slice 2b - the lethality gate

The one term allowed to interrupt a goal. In `Score`, after the habit:

    substantive -= TacticianWeights.MoveLethality * pDestroyed * ForfeitedContribution();

Two new weights (plain `public static float`, so `TrySet` picks them up for free):
`MoveLethality` (calibrated below) and `LethalityBlockedDiscount` (default ~0.8f).

**killWounds** - accumulated per candidate inside the EXISTING enemy loop; the estimates are
already computed there, add no new EstimateShooting/EstimateMelee calls:

    shootW_e   = incoming.ExpectedWounds * (hasLane ? 1f : LethalityBlockedDiscount)
    meleeW_e   = the RAW EstimateMelee(...).AttackerAttack.ExpectedWounds when the reach test
                 passes (NOT the 0.5-scaled `meleeThreat` local - that factor is threat
                 pricing, not a wounds estimate)
    killWounds += Math.Max(shootW_e, meleeW_e) * share_e

- RAW wounds, never `ValueFraction` - it clamps at min(1, wounds/remaining), which saturates
  exactly where the gate must discriminate: 1.2x lethal and 3x lethal look identical through it.
- SUM across enemies, max(shoot, melee) per enemy: one activation does one or the other, and
  they ALL activate. Do not copy retaliation's Math.Max aggregation - a lethality gate blind to
  convergent fire is blind to the main thing it exists to see.
- `share_e` = the existing one-ply reply share (ours/(ours + BestAlternativeTargetValue), with
  the RetaliationShareFloor) - the enemy may prefer another target.
- Cover counts at MOST of its mass (the discount, not zero): optimism is cheap in the habit and
  expensive here. This asymmetry is why one scalar could never serve both tiers.
- NO engaged-target exemption (unlike the habit): the unit we chose to fight can kill us, and
  chosen exposure is still exposure when pricing death.

**pDestroyed(killWounds)** - piecewise, CONTINUOUS in killWounds, no steps anywhere (the facet-3
lesson: a cliff in the goal-interrupting term is the worst possible place for one). All inputs
live on UnitData already:

    woundsToHalf = max(0f, self.RemainingWounds - self.MaxWounds / 2f)
                   (the engine's half-strength line - RemainingWounds * 2 <= MaxWounds;
                    MaxWounds is Tough-aware at creation)
    woundsToWipe = self.RemainingWounds
    pMoraleFail  = clamp((self.Quality - 1) / 6f, 0f, 1f)
                   = 1f when self.Tokens.HasToken(TokenType.Shaken) - MoraleUtilities
                   short-circuits a Shaken unit's tests to auto-fail, no die rolled
                   (Fearless's 4+ reroll halves the effective fail chance - fine to DEFER;
                   record the deferral in the notes if so, never cut it silently)

Shape: 0 below the knee ("lose 2 of 10 is exactly free"); ramps up to pMoraleFail as killWounds
crosses woundsToHalf; linear from pMoraleFail at the knee to 1.0 at woundsToWipe; 1 beyond. Ramp
widths are implementation detail - the constraints are continuity and the pins.

**ForfeitedContribution()** - endpoint-INDEPENDENT, cached per activation (reset next to
`_meleeThreatTotal` in BeginActivation). This is the load-bearing choice: across candidates the
penalty varies only through pDestroyed, so a surrounded unit (P ~ 1 everywhere) sees a constant,
it cancels in the argmax, and the goal wins - pin 12's rush. Two halves, both in the value
currency everything else uses:

- Attrition half, horizon-DECAYED: UnitValue(self)/100 x roundsRemaining/totalRounds.
- Objective half, NOT decayed (Chris's correction - round-4 death costs the marker you were
  contesting): when the unit holds/contests a marker, or stands within one move of a relevant
  one, add a flip-scale amount in the MoveObjective currency, urgency-scaled (urgency RISES
  late, which is correct here). Reuse the marker helpers the planner already has
  (`_markerContestable`, TacticalAnalysis's objective-eligibility gate).
- Round decay stays EMERGENT from these two. Never a scalar.

**Do NOT** (each looks like an improvement and re-breaks a settled decision):

- Do not nullify the candidate's own goal on expected death ("it dies before end of round, so
  the rush scores nothing") - that re-introduces the freeze and contradicts drawn-fire
  neutrality. Pin 12 enforces this.
- Do not use ValueFraction anywhere in the gate.
- Do not retune MoveRetaliation as part of this. The gate's sum partially compensates for
  retaliation's Max-blindness to convergent fire; revisit retaliation only after Tier 2 plays.
- Charge candidates: the defender's immediate return strike is already priced in the offense
  margin, and the gate adds its NEXT-activation threat on top. The slight overlap is accepted -
  no exemption; the pins arbitrate.

**Calibration** - measured, not chosen, same method as Tier 1: an `[Explicit]` Calibrate harness
in the new fixture prints the bracket, and MoveLethality sits at its geometric centre.

- Floor (pin 7): at pDestroyed ~ 1 with a fresh unit's contribution, the penalty must beat the
  goal side of a wipeout walk - up to ~1.3 x 0.75 for a late flip, plus the 0.05 reachable
  bonus, plus approach terms.
- Ceiling (pins 6/12): must never stop the profitable rush. P = 0 below the knee by
  construction; the ramp region is what needs the headroom.
- The MoveReachableBonus sign gate (`substantive > 0`) shifts the effective bracket exactly as
  it did for Tier 1 - a penalty pushing a candidate below zero also strips 0.05. Measure WITH
  it, as Tier 1's harness did.

**Pins**: 6, 7, 8, 9, 10, 12 from the table below. New fixture `TacticianLethalityGateTests.cs`,
`[TestFixture, NonParallelizable]`, snapshot/restore the static weights in TearDown - copy the
TacticianCoverHabitTests pattern verbatim. Pin 10's scene must keep the unit OFF-marker so the
attrition half alone carries the round-1-vs-round-4 contrast (a contesting unit's objective half
correctly does not decay, which would mask it).

**Verify + ship** (per slice, 2a and 2b separately): engine suite green, full `dotnet build`,
headless smoke exit 0; submodule-first commit cadence; dated note here, newest on top. After 2b:
one 640-game pool run as the formality regression net (same pool/seeds/options as the slice-1
gate) - expect flat; compare PAIRED game-by-game, never by hash equality (#210: DOP-16
determinism is per-binary, and every A/B rebuilds).

### After Tier 2 - queued wins, explicitly OUT of this item's scope

1. **Shaken enemies are priced as full threats** in retaliation and projected threat, though a
   Shaken unit spends its activation recovering. VERIFY the engine actually enforces idle-recovery
   first (known-stubs rule), then discount. Cheap sharpening of every threat term.
   **The discount must be split by whether that enemy has already activated this round** (Chris,
   2026-08-06): a Shaken enemy that has NOT yet activated can recover this round and shoot early
   next round, quite possibly before our unit acts again - so its threat is delayed by roughly one
   activation, not removed, and it earns only a small discount. One that has ALREADY activated
   burns its NEXT activation recovering, which buys us a whole extra activation of safety, so it
   earns a large one. `IGameProgress.UnactivatedUnits` already carries exactly this fact and is
   already replicated to both host and client - its own docstring notes the tactical overlay uses
   it to show only unactivated enemies as projecting threat, so the precedent is set.
2. **FdgLab terrain lever**: GameRunner hardcodes GameSettings.GetDefault(); the engine supports
   ETerrainPlacementMode.LoadFromFile. The missing instrument for all cover work - without it
   every gate on this family of changes is structurally a coin flip.
3. **#364** - melee path-vs-straight-line reach (open item; over-fearing today).
4. **RepresentativeCenterZ classification** - possible bug (5 of 6 blocking pieces read as
   deployment-zone furniture), map-wide impact, needs its own decision.
5. **Retaliation sum-with-share** - the principled fix for the Max weakness; biggest and most
   dangerous lever, only after Tier 2 plays.

## Tests - the pins ARE the spec

Constructed decision cases, not mass games (Chris): the exchange rate should be reviewable in one
screen instead of emergent from six weights.

| # | case | expected | slice |
|---|---|---|---|
| 1 | equal progress, one endpoint shadowed | picks shadowed | 1 |
| 2 | blockers toward an empty table edge | no preference | 1 |
| 3 | open board, no terrain | scores bit-identical to pre-change | 1 |
| 4 | 12" progress exposed vs 10" shadowed | picks shadowed | 1 |
| 5 | 12" progress exposed vs 4" shadowed | picks exposed | 1 |
| 6 | objective reachable, expect to lose 2 of 10 | goes anyway | 2 |
| 7 | expected wipeout | balks | 2 |
| 8 | same casualties, full strength vs pushed past half | only the second balks | 2 |
| 9 | same scenario, quality 3+ vs 5+ | 5+ balks first | 2 |
| 10 | lethal scenario, round 1 vs round 4 | balks, then goes | 2 |
| 11 | good shot available vs cover with no shot | takes the shot | 1 |
| 12 | 2 of 10 left, surrounded, objective in reach | rushes it, does not freeze | 2 |
| 13 | one enemy engaged, others on a flank | prefers the spot exposed only to the target | 1 |
| 14 | covered side within charge reach vs open side outside it | picks the open side | 1b |
| 15 | same, checking the habit itself | lands at exactly zero (withheld, not double-charged) | 1c |
| 16 | melee reaches BOTH sides equally | still prefers the shadowed one | 1c |
| 17 | corridor + a distant melee blob reaching neither endpoint | shadowed+charged still loses; 14/15/16 unchanged | 2a |
| 18 | cheap chaff vs a gunline / cheap body on a charge lane | still tarpits, still screens (existing pins, kept green by netting) | 2b |

Pins 4 and 5 jointly DEFINE `MoveCoverHabit`. Pin 3 means the change provably cannot disturb the
existing bench pool, which demotes the 640-game gate to a formality run once at the end.

## Known limitations (recorded, not built)

- **Endpoints, not paths.** The scorer prices where a unit STOPS. A route that ends in cover but
  crosses open ground scores the same as one that hugs walls the whole way, so "move from cover to
  cover" is approximated by "stop in cover". Making the path itself count means scoring inside
  `MovementPlanner` - much larger, deliberately out of scope.
- **Drawing fire is not modelled.** Chris: a doomed remnant rushing an objective has positive
  value because it forces a gun to fire at it instead of at a real threat. Tier 2's formulation
  makes this neutral rather than penalised, which is as far as this item goes.
- Terrain-dense bench layouts (the original #365 scope, superseded): FdgLab has no terrain lever
  (`GameRunner` hardcodes `GameSettings.GetDefault()`) though the engine supports
  `ETerrainPlacementMode.LoadFromFile`. Useful for eyeballing a game, NOT the instrument for this
  work. See the terrain audit below.

## Terrain audit - why the bench pool cannot measure this (2026-08-06)

`AutoFromLayout` over `DefaultTerrainPool`: 6 `Blocking | Impassible` pieces, but
`PlaceTerrainStage.RepresentativeCenterZ` averages a composite's parts' centre-Z (not the AABB
centre), which puts FIVE of them inside a deployment zone, where `DeploymentZonePlacementChance`
= 0.4 drops them 60% of the time - and deployment zones are BEHIND the armies. Only the Central
building (6x4 = 24 sq in, jittered up to 10") is reliably on the table. Expected map: **~3.0
blocking pieces, 77.6 sq in = 2.2% of the 72x48 table.** The rest of the pool (2 forests, 2
sandbag lines, mine field, rubble) is Cover/Difficult/Dangerous, which `HasLineOfSight` ignores
entirely - it reads the Blocking flag only.

Open question, deliberately NOT folded in: is the `RepresentativeCenterZ` classification itself a
bug? Five of six solid pieces being treated as deployment-zone furniture looks unintended, but
changing it changes how every generated map plays, which `DefaultTerrainPool`'s comment says it
does not want to do silently. Separate call from anything here.

## Notes (newest first)

- 2026-08-06 (Tier 2 review on Fable - PROPOSED next step, awaiting sign-off; nothing built). The
  full-history review sharpened the post-mortem into a structural claim: **in an argmax over one
  unit's candidates, any term of the form `f(threat at endpoint) x candidate-constant` is just
  another retaliation term** - the constant cannot change which candidate wins, only f can, so the
  term's whole effect is its threat gradient, reshaped by the knee. "Goals dominate except at
  certain death" is expressible additively ONLY as a term that is ~zero on almost every candidate
  and large on the few that are near-certain death - a rare VETO, not a curve. Every measured
  variant (three aggregations, W 0.4-1.7) was a curve, so the concept was never tested.

  Implied-P extraction from the replayed flip (penalty / W / (forfeit-banked), the charges pin the
  P=1 ceiling): every move candidate sat at P 0.17-0.42; only the two charges hit 1.0, and they
  were not the argmax winners with the gate off. **A wipeout-only veto would have been silent on
  that entire activation** - the flip would not have happened.

  The pin conflict dissolves with the knee. Pin 9 (quality discrimination at the knee) is the
  single reason the weight needed >= 1.0, and >= 1.0 is what every pool run condemns. A veto needs
  only pin 7's floor: W x forfeit > the goal margin at P=1, which the existing calibration data
  puts at W >= ~0.5; ~0.7-0.8 has comfortable margin and fires on a handful of candidates per game
  instead of 27 of 28.

  **Refined slice 2c (proposed):**
  - `ProbabilityLost` collapses to one continuous ramp near wipeout (0 below ~0.8 x remaining
    wounds, 1 at ~remaining). No knee, no smear-from-quarter-health, no quality term, no
    shoot/melee split - delete `LethalityShakenSeverity` and the split accumulators (the GF v3.5.1
    rout-is-melee-only finding stays recorded here; it simply has no seat in a wipeout-only veto).
  - KEEP: `RankedThreat` decay aggregation (bounded 2x worst - the veto's estimator must not
    overestimate, and false vetoes are its one failure mode), `ForfeitedContribution` with the
    banked-value netting (the doomed-remnant cancellation and the chaff/tarpit exemption both live
    there), pins 6, 7, 12, 17, 19.
  - **Pin 10 SURVIVES**, correcting the earlier claim that it dies: round decay lives in the
    forfeiture (attrition half is zero in round 4), not in the P-curve, so "balks round 1, goes
    round 4" still holds at P=1.
  - Pins 8 and 9 are the explicit scope cut: the morale knee and quality scaling ARE sub-wipeout
    discrimination, which is precisely what the pool forbids at any weight that lets them resolve.
    The knee idea is not dead - if it ever returns, its natural home is retaliation's response
    curve (a magnitude adjustment to an existing threat term), never a goal-overriding term.
  - Recalibrate W with the existing Calibrate harness, then ONE 640-game pool run. Accept only
    within ~1 sigma of the 85.39% gate-off baseline; otherwise revert 2b entirely, keep 2a, close
    Tier 2 as measured-and-rejected. No third redesign either way - the spend is capped.
  - The deeper alternative, named for the record and deferred: the score's true accounting error is
    crediting objective flips the unit will not survive to collect (scoring is at END of round);
    the faithful fix multiplies the objective credit by P(survive to reconcile) from the enemies
    yet to activate (`IGameProgress.UnactivatedUnits` carries this). Rejected for now: it is
    multiplicative surgery on the most-tuned term in the scorer and it re-opens the remnant-freeze
    unless handled with care.

- 2026-08-06 (game-level post-mortem before dropping Tier 2 - Chris: "it might be that the
  implementation is bad, not the concept". **He is right, and the distinction matters.**) Replayed a
  flipped game at `--dop 1` with `smoke --log-decisions`: Alien Hives vs Battle Brothers, seed 1000,
  which goes 3-0 with the gate off and 1-1 with it on. Reproduced exactly both ways.

  What the gate actually does in a real game, measured over that one game's 28-32 Tactician
  activations:

  | | gate off | gate on |
  |---|---|---|
  | plan lines that differ | - | **27 of 28** |
  | activations whose BEST candidate scores below zero | 4 | **23** |

  It is not a gate. It changes essentially every decision, and it drives the best available option
  negative in about three quarters of activations. Two consequences follow, neither intended:

  - Because `ForfeitedContribution` is deliberately CONSTANT across a unit's candidates, the term
    reduces to `MoveLethality x constant x P(threat at this endpoint)`. That is a **second
    retaliation term** with a nonlinear response curve and an effective coefficient several times
    `MoveRetaliation` (0.45, itself the product of several tuning gates). Nothing about it is a
    veto; it is a threat field laid over the whole board.
  - Driving `substantive` negative almost everywhere also silently disables `MoveReachableBonus`
    for almost every candidate, since that tie-break is gated on `substantive > 0`.

  The single largest penalty in the game was **-2.04 on `ChargeToContact vs Battle Tank`** (0.2374
  becoming -1.8066) - a Horde Melee army's unit refusing the charge that is the entire reason the
  army exists. Its own high `UnitValue` inflates its forfeiture, so under this formulation **the
  better the unit, the more cowardly it becomes**. Worth keeping in mind for any redesign: value as
  a multiplier on reluctance has that perverse edge.

  Also worth recording because it corrects an assumption in the earlier analysis: the unit does NOT
  freeze. It still moved and still rushed a marker - just the nearer one at (36,34) instead of
  pushing to (34,13). The pool damage is not paralysis, it is systematically trading ambition for
  safety on nearly every activation.

  **So the concept was never tested.** "A rare veto that fires only on genuine certain death" and
  "a continuous threat field over every candidate" are different things, and only the second one was
  measured. That does not resurrect the design as built - the numbers stand - but it does mean the
  -14pp is evidence against the IMPLEMENTATION, not against Chris's original idea.

- 2026-08-06 (ranked decay measured - and the finding is now about the GATE, not the aggregation).
  Chris chose diminishing-returns aggregation over the share, for two good reasons: a share depends
  on how many units YOUR army brought (a 3-unit elite list and a 12-unit horde get ~4x different
  treatment from the same threat), and it requires predicting the opponent's target choice - the
  same class of guess this item ruled out of scope for Tier 1. Implemented as `RankedThreat`: rank
  the enemies that can reach the endpoint, weight the worst fully, the next by
  `LethalityFocusDecay` (0.5), the next by its square. Bounded at 2x the worst single threat.

  **It scored 71.17% - worse than both share variants.** Which, on reflection, is exactly what the
  mechanism predicts: the share was dividing by ~8, and decay divides by nothing, so decay PERCEIVES
  MORE THREAT than the share-normalised sum did (~5.8 wounds where the share gave ~1.9 on the same
  board). Every result now lines up on one axis:

  | variant | perceived threat | pool |
  |---|---|---|
  | gate off (slice 2a only) | - | **85.39%** |
  | MoveLethality 0.4 | sum x share | 84.92% |
  | MoveLethality 0.8 | sum x share | 81.56% |
  | share normalised over our army, W=1.7 | lowest | 77.89% |
  | retaliation's share, W=1.7 | higher | 75.55% |
  | ranked decay, no share, W=1.7 | highest | 71.17% |

  **The harm scales monotonically with how much threat the gate perceives, across every aggregation
  tried.** That is not an aggregation bug - it is the gate itself. Making the Tactician more
  cautious loses games, and the only settings that are pool-safe are the ones where the term barely
  fires. The pins need `MoveLethality >= 1.0` for pin 9 to resolve at all, and there is no
  aggregation at which W >= 1.0 is pool-neutral. **The pin floor and pool neutrality are
  incompatible.** Six carefully constructed cases say the gate behaves as designed; 640 games say
  the design costs between 1 and 14 percentage points depending on how loudly it speaks.

  Honest caveat, recorded so nobody over-reads this: the pool opponent is `solorules`, and caution
  may simply be undervalued against a bot that walks into you. But it is the only instrument there
  is, and "it might do better against an opponent we cannot measure" is not a case for shipping a
  term that overrides goals.

- 2026-08-06 (a second structural reason the gate bites too often, independent of the share). The
  free zone is far narrower than the design intent reads. With `smear = 0.25 x MaxWounds`, a fresh
  unit's ramp starts at `kneeStart = (rem - max/2) - 0.25 x max = 0.25 x max` - so P leaves zero at
  **a quarter of the unit's health**, not at the half-strength knee. Chris's "lose 2 of 10 and take
  the objective" clears it only just (0.20 against 0.25); 3 of 10 is already priced. Combined with a
  forfeiture around 1.1 for a valuable unit near a marker, even P = 0.3 yields a ~0.56 penalty,
  which is the size of the entire objective term - so the gate overrides goals at casualty levels
  that are entirely ordinary. Narrowing the smear widens the free zone but sharpens the curve back
  toward the cliff it exists to avoid; that tension is unresolved and belongs to any redesign.

- 2026-08-06 (the share fix was real but NOT sufficient - the gate is harmful for deeper reasons).
  Correcting the targeting share to `ours / (ours + every other unit of ours)` recovered 2.3pp of
  the 9.8 and no more: **77.89%** against **85.39%** with the gate off. So the diagnosis below was
  right about the mechanism and wrong about it being the whole story - a summed threat term
  penalises FORWARD movement no matter how carefully the share is priced, because advancing is
  precisely what brings more enemies into range.

  | variant | pool | vs gate-off |
  |---|---|---|
  | gate off (slice 2a only) | 85.39% | - |
  | gate, retaliation's share (first cut) | 75.55% | -9.84pp |
  | gate, share normalised over all friendlies | 77.89% | -7.50pp |

  Two implementation errors were found and fixed on the way, both mine, neither the cause:
  - The corrected pool first measured "us" at our CURRENT position while the numerator measured us
    at the ENDPOINT, so the ratio could exceed 1 and needed a clamp. It sums the other units only
    now, which is well-formed by construction.
  - `Squad` in the gate fixture laid models out downward from the centre, so a 60-model gunline's
    centroid drifted ~5" and fell out of range of the very endpoint the scene existed to threaten -
    the scene scored nothing at all and read as "the gate does not fire". Model blocks are centred
    symmetrically now. Worth remembering when writing scenes with large units.

  **Chris's objection, which lands (2026-08-06):** "when I play the real game, I think less about
  what my opponent will choose to shoot, and I just try to make it difficult regardless." Predicting
  the opponent's TARGET CHOICE is the same class of guess as predicting their MOVEMENT, which this
  very item ruled out of scope for Tier 1 - "offense is a fact, threat is a forecast". Tier 2
  imported exactly that kind of prediction without anyone noticing it contradicts the principle two
  slices earlier.

  The counter-argument, recorded because it constrains any redesign: the share is not decoration,
  it is the only thing keeping a SUM from being nonsense. With no discount at all, three to six
  enemies reach a typical forward endpoint and their combined raw wounds are several times the
  unit's remaining wounds, so P is 1 on every advance and the gate applies its full penalty to every
  forward move - strictly worse than either row above. "Remove the share" therefore only works if
  the SUM goes too, which is the worst-single-threat variant now being measured.

- 2026-08-06 (the gate's first pool run was a REGRESSION, and why - kept as the record even though
  the cause is fixed below). Tier 2 as first written scored **75.55%** against 85.39% with the gate
  switched off: **-9.8pp, z -6.14, all eight armies worse**, 205 flips of which 142 worsened. Not
  noise, and the opposite of the "expect flat" the handoff plan predicted. A weight sweep on the
  same binary (`--weights MoveLethality=...`, so the only difference is the scalar) showed the
  damage is monotonic and the term is only harmless when it is nearly inert:

  | MoveLethality | pool score | vs gate-off |
  |---|---|---|
  | 0 (gate off, slice 2a only) | 85.39% | - |
  | 0.4 | 84.92% | -0.47pp |
  | 0.8 | 81.56% | -3.83pp |
  | 1.7 (shipped by pins) | 75.55% | -9.84pp |

  Since the pins need >= 1.0 to resolve pin 9 at all, "tune it down" was not available: the
  aggregation was wrong, not the magnitude.

  **Cause.** The gate summed raw wounds across enemies but weighted each by RETALIATION's share,
  `ours/(ours + BestAlternativeTargetValue)` with a 0.25 pessimism floor. That share is built for a
  term that takes a MAX over enemies - and `BestAlternativeTargetValue` is itself a Max over
  friendlies, the single best other target - so it sits near 0.5 for a typical unit. Summed over
  eight enemies it modelled roughly HALF THE ENEMY ARMY shooting one squad every round, which put
  killWounds past the half-strength knee on ordinary forward moves and turned the gate into a
  second, far larger retaliation term on every advance. The per-army table reads exactly that way:
  the worst losses were the Caster-Heavy (-13.8pp) and Horde Melee (-13.1pp) lists, the ones that
  most need to cross open ground.

  The audit finding that produced this ("retaliation's Math.Max underprices convergent fire, so the
  gate must SUM") was right about Max and wrong to reuse Max's share underneath a sum.

  **Every pin missed it, and that is the reusable lesson.** All six gate scenes have exactly ONE
  friendly unit, where the targeting share is 1.0 by construction, so not one of them exercised the
  share at all - the term they were calibrating was invisible to them. Constructed pins verify the
  decision you thought to construct; they cannot tell you the model is mispriced in a shape you did
  not think of. **Pin 19 now covers it** (a squad among five equally shootable friendlies must be
  gated far less than a lone one), and it fails on the pre-fix build for the right reason.

- 2026-08-06 (slice 2b SHIPPED - Tier 2, the lethality gate). `MoveLethality = 1.7`,
  `LethalityBlockedDiscount = 0.8`, `LethalityShakenSeverity = 0.6`. Pins 6-10 and 12 green in the
  new `TacticianLethalityGateTests` (6 + an Explicit Calibrate). Suite 2917 green, full build,
  headless smoke exit 0. **Three things came out different from the handoff plan, all of them
  because the plan told me to check rather than assume:**

  1. **Shooting cannot delete a unit by breaking it.** The plan's morale knee assumed crossing half
     strength turns a failed test "from suppression into deletion". The engine says otherwise, and
     correctly: `ResolveRangedMoraleStage` - "a non-melee morale failure never Routs - Rout is a
     melee-only result, GF v3.5.1"; only `AssignMeleeMoralePenaltyStage` Routs. So the gate tracks
     `killWoundsShoot` and `killWoundsMelee` apart and blends the knee's severity by which kind is
     doing the wounding. Pricing a gunline as though it could delete would have units flinching
     away from fire they should walk through.
  2. **Getting Shaken is not cheap** (Chris, mid-slice): "you lose at least a quarter of the unit's
     lifetime potential instantly." Three concrete costs - an activation burned recovering (one of
     four), counting toward NO objective while Shaken, and auto-failing the next morale test, which
     makes a later melee loss a certain Rout. `LethalityShakenSeverity = 0.6` blends a total loss of
     the objective half against a ~quarter loss of the attrition half. **Deferred, not cut:**
     pricing those two halves separately is the more faithful model and wants its own probability
     track and pins.
  3. **The gate nets against value the move already banked** - damage dealt from the endpoint and a
     body interposed on a charge lane; NOT objective deltas (ReconcileObjectivesStage scores at END
     of round, so a unit that dies first never collects) and NOT approach credit. This was NOT in
     the plan and was forced by measurement: without it the gate billed a unit for a death the plan
     was already paying for, `CheapChaff_ChargesTheGunline_ToTarpitIt` and
     `CheapUnit_ScreensTheValuableShooters_FromTheHorde` both broke from ~1.05 upward, pin 9 needed
     >= 1.0, and the weight had exactly ONE admissible value - overfitting, not calibration. Netting
     removed that ceiling (chaff/screen pass to 4.0+) and opened the real bracket below. Same shape
     as pin 15's Tier 1 double-charge, in the other direction.

  **Calibration (measured, printed by `Calibrate`).** Smallest gunline that makes a unit refuse a
  marker it could otherwise take:

  | W | fresh Q4 | worn Q4 | fresh Q3 | fresh Q5 | |
  |---|---|---|---|---|---|
  | 0.8 | 30 | 20 | 30 | 30 | pin 9 cannot resolve - 3+ and 5+ balk together |
  | 1.0 | 28 | 18 | 28 | 26 | **floor**: quality starts to decide |
  | 1.5 | 22 | 14 | 24 | 22 | |
  | 2.0 | 20 | 12 | 22 | 18 | |
  | 3.0 | 18 | 10 | 20 | 16 | **ceiling**: a worn unit freezes for half of what it has left |
  | 6.0 | 14 | 6 | 16 | 14 | |

  1.7 is the geometric centre, ~1.7x clear of both ends. Below 1.0 pin 9 reads BACKWARDS - a 3+ unit
  is worth more, so raw value outweighs the morale odds and the veteran balks first. Note the fresh
  column flattens near 14 at any weight: P is identically zero below the knee, so the gate cannot
  make a healthy unit flinch at ordinary casualties however it is tuned - Chris's "lose 2 of 10 and
  take the objective" is a property of the curve's SHAPE, not of the number.

  - Pins 8 and 9 are threshold-ordering pins (smallest gunline that makes each unit balk) rather
    than single hand-tuned scenes. A flip test needs the ungated margin to land between two gate
    costs that differ by ~25%, which is knife-edge and specifies nothing robust; the threshold form
    is literally "5+ balks first" and survives retuning. Each still asserts one concrete decision at
    a volley between the two thresholds, and checks the ungated scene goes, so the flip provably
    belongs to the gate and not to retaliation.
  - Quality only bites AT the knee, so pin 9 uses FRESH squads. A unit already deep past half
    strength is near-certain to break whatever its Quality (P 0.84 vs 0.88), and the pin cannot see
    a 5% difference.
  - Pin 10 is deliberately objective-free: with no marker in play the forfeiture is the attrition
    half alone, which is the half that decays. 20 guns, round 1 BALKS (-0.1531) and round 4 GOES
    (+0.1182), ungated both GO - round decay emergent from the horizon, never its own scalar.
  - Pin 12 (doomed remnant): ungated +0.8209, gated +0.8709. The gate slightly FAVOURS the rush,
    because P is a shade higher standing still - the cancellation working as designed.
  - **Test-infrastructure bug found and fixed in both fixtures.** `private static readonly float
    Shipped... = TacticianWeights.X;` on a beforefieldinit type initialises on FIRST ACCESS, and the
    first access is inside TearDown (or Calibrate's restore) - AFTER a test has zeroed the weight.
    It captured 0 as "the shipped default" and silently disabled the gate for the rest of the run;
    it cost a full calibration pass reading numbers that meant nothing. Both fixtures now capture in
    `[OneTimeSetUp]`, which is ordered before any test body. The cover-habit fixture had the same
    latent bug and was passing only by luck of type-load timing.

- 2026-08-06 (slice 2a SHIPPED). `MeleeThreatTotal()` now skips melee enemies that could not reach
  us this activation - `MeleeThreatReach(enemy, self) < Distance(now, enemyCentroid) -
  RushDistance(self) - 1`, mirroring the numerator's reach test against the whole candidate
  envelope rather than one endpoint. Endpoint-independent, so the per-activation cache is
  unchanged. **Pin 17 was verified to FAIL on the pre-fix binary before being accepted**: corridor
  plus three sword squads massed on x=25, z=42..48, shadowed+charged scored **0.1202** against
  open+safe **0.1052** - the habit flipping exactly the decision case 14 exists to protect - and
  the correct order returns with the fix. Pins 14/15/16 stayed green throughout (they have one
  melee enemy, which is why they never saw this). Suite 2911 green, full build, headless smoke
  exit 0.

- 2026-08-06 (audit before the Tier 2 handoff; Chris agreed with all findings). (1) The melee
  denominator dilutes against melee-heavy armies - promoted to slice 2a, see the handoff plan.
  (2) The centroid ray is all-or-nothing PER ENEMY (one test decides a whole unit's mass
  muffled/not) - accepted for a bounded habit; the gate discounts rather than zeroes blocked
  lanes for exactly this reason. (3) Endpoints-not-paths - already recorded under limitations.
  (4) Retaliation's Math.Max underprices convergent fire (every enemy activates each round; Max
  prices one) - the gate must SUM; retaliation itself stays untouched. (5) ValueFraction
  saturates at min(1, wounds/remaining), which disqualifies it for the gate. (6) The
  MoveReachableBonus sign gate shifts every calibration bracket. (7) The bench pool measures
  nothing about this work (2.2% blocking terrain) - slice 1 shipped on argument (85.16% vs the
  85.70% control, z -0.54, flips 44/48 - a coin flip, as predicted); pins are the spec, the pool
  is only the net, and finding (1) came from inspection, not from either.

- 2026-08-06 (slice 1c - the two halves must NOT share a denominator). Chris, on the clamped form
  proposed after 1b: "it would be pretty often that melee could reach you whether you choose one
  side of the corridor or the other... under the presence of a heavy melee threat it can't avoid,
  would it still position itself to at least be shot up less?" **No - and both of the shared-
  denominator forms fail it, for the same underlying reason.**

  | form | melee reaches ONE side (corridor) | melee reaches BOTH sides |
  |---|---|---|
  | `k(S_blocked - M)/(S_total + M)` (1b, shipped briefly) | works | cover signal DILUTED by M in the denominator |
  | `k x max(0, S_blocked - M)/total` (proposed after 1b) | works | cover signal DESTROYED - both clamp to 0 |
  | `k(S_blocked/S_total - M_reach/M_total)` (shipped) | works | works |

  The principle Chris is pointing at is general: **a threat that is equal at every candidate is a
  constant and must cancel in the argmax.** Sharing a denominator prevents that cancellation, and
  clamping actively destroys the other signal. Normalised apart, an unavoidable melee threat shifts
  every candidate alike and the shooting-cover decision survives intact - which is the same shape as
  Tier 2's doomed-unit reasoning (price the DIFFERENCE, not the danger).
  - `MeleeThreatTotal()` is the melee denominator: endpoint-independent (it asks how hard they hit,
    not from where), so it is cached per activation, not per candidate.
  - Pin 15 rewritten: the habit lands at exactly ZERO on the covered-but-charged endpoint, not
    negative. Going negative was double-counting - retaliation already charges for melee, and
    nothing else credits cover, which is the asymmetry that justifies the habit existing at all.
  - Pin 16 added (Chris's case): swordsmen equidistant from both endpoints, cover still decides.
    It fails under BOTH rejected forms, which is why it is worth its own pin.

- 2026-08-06 (hoist verified behaviour-neutral, as promised rather than asserted). The offense term
  and the cover share were doing the same `HasLineOfSight(end, enemyPos, TerrainSnapshot())` call
  per (candidate x enemy); hoisting it to one shared `hasLane` makes this net CHEAPER than the
  facet-3 code it replaces. Verified by building both forms and running the same matchup:
  - At `--dop 16`: hashes differ, **8 of 10 games**. Alarming until checked.
  - At `--dop 1` (#210's documented-exact mode): **identical hash `A5236375796FBCDA`.** Neutral.
  - **Datum for #210, and a correction to its model:** the 2026-08-06 note there records a
    bit-identical 640-game repeat and infers the race may have narrowed. It has not. That repeat
    used the SAME BINARY. Two binaries differing only by a provably neutral refactor diverge on 8/10
    games of one tactician cell - so DOP-16 determinism holds per-binary, and any A/B that rebuilds
    (which is every A/B) carries schedule noise. Cross-binary comparisons at DOP 16 must be read as
    paired samples, never as hash equality.

- 2026-08-06 (slice 1b - melee folded into the habit). Chris's corridor: "it chooses the right
  side of a corridor instead of the left, but there's a pack of really powerful swordsmen that can
  now reach it". **Reproduced, and slice 1 as first shipped got it wrong.** Scene: two endpoints at
  equal objective progress, the left shadowed from a gunline and inside the swordsmen's charge
  reach, the right in the open and outside it.

  | | shadowed + charged | open + safe |
  |---|---|---|
  | habit off | 0.07314 | 0.09899 |
  | habit ON, sight-only (as first shipped) | **0.12314** (share +1.000) | 0.09899 |
  | habit ON, with melee | **0.05647** (share **-0.383**) | 0.09899 |

  Sight-only, the +0.05 cover bonus flipped a decision the rest of the scorer had right. The root
  cause is worth recording because it is not obvious: **retaliation takes a `Math.Max` over enemies,
  not a sum**, so "shot at from over there" and "about to be charged by swordsmen" differ by only
  0.026 in the base score - far too little for a 0.05 habit to be safe around. The bound protects
  GOALS (progress terms are large); it did not protect other THREAT terms.
  - Fixed by making the habit's numerator net: shadowed shooting minus reachable melee, over all
    threat at that endpoint. The habit now goes genuinely NEGATIVE (-0.383) rather than merely
    withholding a bonus, which is what pins 14/15 assert separately.
  - Suggestive corroboration in the slice-1 pool: the single largest per-army move was Alien Hives
    (Horde Melee) at -3.1pp, the one melee army in the pool. Inside cell noise (~4pp at 80 games),
    so not evidence on its own - but it points the same way as the corridor case.

- 2026-08-06 (slice 1 pool gate). 640 games, same pool/seeds/options as #363's gate, Release build.
  Reports `FdgLab/reports/365-gate/`.

  | run | score | vs pre-#363 control | vs #363 facet 3 | faults |
  |---|---|---|---|---|
  | control (pre-#363) | 85.70% | - | - | 0 |
  | #363 facet 3 (share04) | 84.84% | -0.86pp | - | 0 |
  | #365 tier 1, sight-only | 85.31% | -0.39pp (z -0.43) | +0.47pp | 0 |
  | #365 tier 1b, shared denominator | 84.77% | -0.94pp (z -0.91) | -0.07pp | 0 |
  | **#365 tier 1c, shipped** | **85.16%** | **-0.55pp (z -0.54)** | **+0.32pp** | 0 |

  Shipped vs control: 92 flips, 44 improved / 48 worsened - a coin flip. Shipped vs 1b: +0.39pp
  (z +0.67), also not significant; 1c was chosen on the argument, not the number.

  Flat against the control (73 flips, 36 improved / 37 worsened - a coin flip, which is what "the
  maps are 2.2% blocking terrain" predicts) and better than the mechanism it replaces. Pin 3 says
  this pool structurally cannot show more; it is the regression net, not the measurement.

- 2026-08-06 (slice 1 SHIPPED - Tier 1, the wall-hugging reflex). `BlockedThreatShare` deleted;
  the four threat sight gates removed (Score's retaliation, the projected-threat forecast,
  `BestAlternativeTargetValue`, `WantsDisembark`), so incoming fire is priced THROUGH terrain
  again - pre-#363 pricing, which measured 85.70%, the best of that item's four gate runs. Cover
  now earns `TacticianWeights.MoveCoverHabit * coverShare` instead, accumulated inside the existing
  enemy loop from the `incomingValue` already computed there, so it costs one LoS test per
  (candidate x enemy) - exactly what `ThreatSightFactor` cost - and adds no new estimate.
  - **Calibration (measured, not chosen).** `TacticianCoverHabitTests.Calibrate` (Explicit) prints
    the bracket on a scene where all three endpoints sit on a circle around the gunline, so
    incoming fire is provably identical and only progress and cover differ:

    | quantity | value |
    |---|---|
    | 2" of progress (Chris's acceptable detour) | 0.0229 |
    | 8" of progress (his unacceptable one) | 0.1526 |
    | a real 5-rifle volley (pin 11's ceiling) | 0.1686 |
    | `MoveReachableBonus`, which steps in when a candidate crosses zero | 0.0500 |

    That bonus is gated on `substantive > 0`, so it tightens the practical ceiling to ~0.1026.
    **`MoveCoverHabit = 0.05`** sits at the geometric centre of (0.0229, 0.1026), just over 2x
    clear of both ends. First attempt at 0.12 failed pins 5 and 11 - above the ceiling - which is
    the pins doing their job.
  - Worth recording: the exchange rate is a FRACTION of the route gap, not inches, because
    `ObjectiveApproach` normalises by `gapNow`. Giving up 2" when the marker is 4" away is
    correctly a much bigger deal than giving up 2" when it is 30" away.
  - Pins 1, 2, 3, 4, 5, 11, 13 green in `TacticianCoverHabitTests` (7). The fixture is
    `[NonParallelizable]` - it mutates a process-global static weight.
  - #363's own facet-3 pin (`Score_WallShadowEndpoint_PricesIncomingFireBelowOpenGround`) still
    passes unchanged, by a different mechanism: retaliation is now priced through the wall at FULL
    value and the covered endpoint wins only by the bounded bonus, so it stays underwater - the
    honest price of standing in front of a gunline you cannot answer. Its comments were rewritten
    rather than left describing a mechanism that no longer exists.
  - Verified: engine suite 2907 green; full `dotnet build`; headless smoke exit 0. Source-save
    acceptance (`BattleBrothersJustMovedAShortDistanceAndDidntShootWhy.fdgsave`, raw not rewound):
    the squad at (52.3,8.6) picks `RushObjective end=(40.9,12.2)` at 0.1059 and the wall-shadow
    `AdvanceOnObjective end=(49.7,14.0)` that started #363 sits at -0.0982. The offense-side fix
    (facets 1+2) is what kills that phantom and is untouched here.

## Decisions

- 2026-08-06: facet 3 is replaced, not extended. Offense keeps exact LoS (fact); threat never gets
  a boolean (forecast). `BlockedThreatShare` deleted.
- 2026-08-06: per-enemy LoS aggregated into a threat-weighted share, NOT bearing bins (see above).
- 2026-08-06: round decay folded into "remaining contribution" rather than being its own scalar.

## Outcome

(open)
