# 355 - A unit with Impact but no melee weapon could never charge, so Impact was dead on 138 units

**Status:** in progress (opened 2026-08-05)

## Goal

`ChooseActionStage.GetCanCharge` refuses a charge when `GetMeleeWeapons().Count == 0`. Impact(X),
Heavy Impact(X) and Ravage(X) all fire at `Melee_OnChargeContact` - on the charge itself, not off a
weapon - so a unit whose only melee contribution IS the impact can never make the charge that would
trigger it.

Owner's rule (2026-08-05): **a unit with no melee weapons but an Impact of some kind needs to be
able to charge, but not strike back.**

## Scope: this is not an edge case

Surveying the 47 bundled books: **138 units** carry a charge-contact rule and have **no melee
weapon at base** - every APC, tank, speeder and buggy in nearly every faction (`Battle Brothers`
APC/Battle Tank/Attack Speeder, `Human Defense Force` Battle Tank ... ). Impact is currently
unreachable on all of them. A further ~38 units can trade their melee weapons away by upgrade,
which is how the reported case arose (#354's Ripjawdactyl Riders took "Replace all Energy Shields
and CCWs -> Shock Pistol").

## What already works (do not rebuild)

- `ResolveImpactHitsStage` is already written for this - its own comment reads "Impact hits come
  from a weaponless charge", and it models them as a synthetic AP-carrying attack.
- `OfferStrikeBackStage` already skips when no in-range DEFENDER has a melee weapon, so "but not
  strike back" needs no new code: an impact-only unit still cannot strike back when charged.
- `DetermineMeleeWinnerStage` already counts impact wounds - `SetDefender` snapshots the defender's
  wounds before impact resolves.
- `ChooseMeleeDefenderStage` has no melee-weapon requirement.
- `CombatMath.EstimateMelee` already models impact wounds (including Counter's dice reduction), so
  the AI's damage estimate for an impact-only charge is already non-zero.

## Decisions

- **Owner ruling 2026-08-05: the gate asks the hook, not a name list.** A unit may charge if it has
  a melee weapon OR any attached rule declares a passive `HookEntry` at `Melee_OnChargeContact` with
  `Seat == Actor`. The hook list already IS the declaration of "I do something on charge contact",
  so any rule authored there in future enables the charge with no engine change, and core `Counter`
  is excluded for free (its charge-contact entry is at the `Subject` seat - defensive). Rejected: a
  name check like `AircraftRules.IsAircraft` (hardcoding, the thing #197 spent two dozen rules
  avoiding), a dry-run of the hook (the gate runs before a defender exists), and a new
  `EnablesCharge` flag on the definition (duplicates the hook list and can drift from it).
- **Owner ruling 2026-08-05: a contact-but-unarmed melee resolves in full.**
  `DetermineInRangeAttackersStage` had ONE exit for two different situations. Split it: no model in
  melee range at all (defender wiped by impact, pile-in failed) keeps today's path to fatigue ->
  consolidate; models in contact but none carrying a melee weapon routes to `offerStrikeBack` -
  the same path #320 already uses for "the player held every weapon back". The defender strikes
  back if it can, the winner is determined from wounds dealt, the loser tests morale. Rejected:
  impact-only with no counter-attack, which would let a tank ram armed infantry with impunity.
- **Owner ruling 2026-08-05: the AI ships in the same slice.** Eight AI sites gate on
  `GetMeleeWeapons().Count > 0`; without them the rule would be human-only. Owner chose this over
  the one-slice-at-a-time default.

## Notes

### 2026-08-05 - implemented (engine `fa9c5fa`)

**Gate.** `Rules/Dispatch/ChargeContactRules.cs`: `ActsOnChargeContact` reads whether any rule on
the unit, its living models, or their weapons declares a passive `HookEntry` at
`Melee_OnChargeContact` with `Seat == Actor`; `CanFightInMelee` is that OR a melee weapon.
`ChooseActionStage.GetCanCharge` asks it. Conditions are deliberately NOT evaluated - the gate runs
before a defender is picked, so a condition reading the target cannot be answered yet, and a rule
that then declines to fire just means a charge with no impact hits, which the melee flow handles.

**Routing.** `DetermineInRangeAttackersStage` splits its single "no attackers" exit on
`inRange.Count`: nobody in contact keeps `OnNoAttackersInRange` -> fatigue -> consolidate; models in
contact with an empty swing pool take the new `OnAttackersInRangeUnarmed` ->
`DetermineInRangeDefendersStage`, which records the strike-back-eligible defenders and then routes
to `OfferStrikeBackStage` via `ToStrikeBackUnopposed` instead of the weapon offer. That stage owns
the branch because the strike-back needs the in-range defenders IT records, and because
`ChooseMeleeWeaponStage` throws on an empty pool. The extra-attack window (#197 P16) is skipped on
this path on purpose - an extra ATTACK for a unit making none.

**AI (same slice, owner's call).** Six melee-capability checks now share `CanFightInMelee`:
`DeploymentMatchup.OutputValue`, `MacroActionGenerator` (charge macro-action generation),
`TacticalAnalysis.ThreatRangeAgainst`, `TacticianRangedAttackResolver` (does this target threaten
us), and both `TacticianPlanner` sites (transport evacuation, cargo disembark-to-charge). The
SoloRules `AiDefineMovementResolver` lets an impact-only unit seek contact when it is already within
this move. Two deliberate non-changes: `AiUnitClassifier` keeps calling these units `Shooting`
(`Hybrid` would make a tank RUSH, forfeiting the guns that are its real output, across the whole
table), and `CombatMath`'s `defenderStrikesFirst` still requires a real melee weapon (Counter
strikes FIRST, which needs something to swing). One fix taken while there: the AI's impact estimate
hardcoded `armorPenetration: 0` where the live stage reads `impact.ArmorPenetration`, understating
every Heavy Impact ram by its AP(1).

**Tests.** `Tests/ImpactOnlyChargeRuleIntegrationTests.cs` (10): the predicate (Impact / Ravage /
the shipped Heavy Impact shape all qualify by hook, Counter's Subject-seat entry does not, a dead
carrier's rule stops counting, a melee weapon alone still suffices) and the routing (in-contact
unarmed takes the new exit, the defender stage skips the weapon offer while still recording
strike-back models, an armed attacker is unaffected, nobody-in-contact still ends the melee).
`MeleeInRangeIntegrationTests.InRangeButOnlyRangedWeapon_*` updated - it asserted the OLD shared
exit, which is precisely the behavior this item changed.

**Verified:** engine suite 2852 green, app suite 1086 green, `dotnet build` clean, default headless
smoke exits 0. Live confirmation, `--seed 7 --army "armies/2k - Orks - Horde Mixed.fdgarmy"`: an
Assault Buggy (Impact(3), no melee weapon) charged Orc Warriors -> 3 impact wounds killed a model ->
"the melee resolves with no attacks from the attacker" -> Orc Warriors struck back with a Heavy Claw
-> "Attackers won melee 3 vs. 0" -> morale test -> fatigue -> consolidate. A vehicle-heavy HDF game
rammed 7 times in 4 rounds.

**Not done / open questions:**
- **A loaded transport can still charge.** No gate existed before and none was added, so an APC full
  of infantry may ram. Wants an owner ruling; if it should be blocked, the gate is one line in
  `GetCanCharge`.
- **No GUI hand-verify yet** - the charge option's availability and its reason string were only
  exercised headless.
- **The AI's ram valuation is inherited, not tuned.** `CombatMath.EstimateMelee` already priced
  impact wounds, so the AI now sees a real number for a ram, but no weight was added for "ramming
  costs the tank its shooting this activation" - it may ram when shooting scores better.
