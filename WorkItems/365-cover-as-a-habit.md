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
   Shaken unit spends its activation recovering. VERIFY the engine actually enforces
   idle-recovery first (known-stubs rule), then discount. Cheap sharpening of every threat term.
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
