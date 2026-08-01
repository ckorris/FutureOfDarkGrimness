# 306 — The weapon choosers key by weapon NAME, and fault on a duplicate

**Status**: done (2026-07-31)
**Related**: found during #197 (Sergeant slice), #209 (weapon-choice order determinism), #027 (weapon special rules), slice 0 of #197 (targeted upgrades / `UpgradeSection.Targets`)

## Goal

A unit may carry two weapons with the same name and different profiles without faulting the state
machine. Done = the ranged chooser (and its melee sibling) key on weapon identity/profile rather than
`Weapon.Name`, `#209`'s deterministic ordering still holds, and a test pins a same-name split weapon
going through both choosers.

## The bug

`ChooseRangedAttackStage.BuildWeaponOptions` keys everything by name:

```csharp
Dictionary<string, WeaponOption> nameAndWeaponOptions = new Dictionary<string, WeaponOption>();
Dictionary<string, bool> weaponIgnoresLineOfSight = new Dictionary<string, bool>();
...
nameAndWeaponOptions.Add(weapon.Name, ...);   // <-- throws on a duplicate name
weaponIgnoresLineOfSight.Add(weapon.Name, losIgnoreRule != null);
```

The per-target stats, the LoS map and the range cache are all keyed the same way, and `Dictionary.Add`
throws rather than overwrites — so a duplicate name **faults the state machine mid-activation** rather
than degrading. `ChooseMeleeWeaponStage` has the same weakness and already admits it in a TODO
("we're hackedly using their stats names, which have no protection against identical names").

Two places already work around it rather than fix it:

- `CanShootAnything`'s gate dedupes by name before calling in (`GetRangedWeapons` returns one instance
  per carrying model, so duplicates are routine there).
- **#197's Sergeant slice renames marked copies** to `"Rifle (Sergeant)"` — a real behavioural
  workaround, not just cosmetics. The play probe found this the hard way: the whole green suite missed
  it and the game crashed with *"An item with the same key has already been added: Rifle"*.

## Why it is latent rather than live

The only mechanism that splits a `WeaponFileEntry` into same-name copies with different profiles is
#197 slice 0's targeted upgrades (`UpgradeSection.Targets` — an upgrade buying fewer copies than the
unit carries). The corpus has 17 such sites, all `"Upgrade Master Marksman Carbine with: Precise"`, and
all are **one-carbine heroes**: whole-entry attach, no split, no duplicate. So nothing in the shipped
books triggers it today.

It goes live the moment a book update ships a multi-copy partial weapon upgrade — a unit with 3 rifles
where 1 gets Precise. That is an ordinary thing for OPR to publish, and the failure mode is a crash.

## Suggested shape

Profile-key the chooser: key on the `Weapon` reference (or a value key of name + profile) instead of the
bare name, and audit `ChooseMeleeWeaponStage` for the same change.

**Constraint from #209**: the weapon pool is a `ConcurrentDictionary` keyed by `Weapon` identity, whose
enumeration order is identity-hash-dependent — that non-determinism broke same-seed replay, and the fix
was to present options in a deterministic *sorted* order. Any re-keying has to preserve a stable sort,
so the sort key must stay something orderable (name, then a profile discriminator), not a hash. Do not
re-key to raw reference identity without re-establishing the ordering.

Also worth deciding: whether the Sergeant rename stays once the chooser is profile-keyed. It has a
second, independent job — the log and the dice row self-attribute (*"Blood Squad's Sword (Sergeant)'s
Sergeant added 0.167 extra hits"*), which is genuinely useful. Probably keep it, but stop *relying* on
it for correctness.

## Notes

- 2026-07-31: Fixed and closed. One correction to the filing above: the melee stage is **not** blind to a
  rule's argument on the real path. `ResolvedRule.RequestedName` is the army entry's `PrintableName`,
  which is already `"Deadly(3)"` / `"Spawn(Spores [5])"` / `"Medical Training (Regeneration)"` — so
  labels do distinguish `Deadly(3)` from `Deadly(6)` in any loaded or resumed game. Rendering the
  arguments a second time (the option first put to Chris) would have printed `Deadly(3)(3)`. The real
  melee gap was that nothing *guaranteed* label uniqueness, so the fix is structural instead.
- 2026-07-31: Filed out of #197 on its close. Recorded in that item's hygiene section since the Sergeant
  slice (2026-07-29); it is not #197 work.

## Decisions

- **One profile key, shared by the pool and both choosers.** `WeaponProfileKey.For(IWeapon)` returns
  name + range + attacks + AP + the sorted multiset of (canonical rule name, arguments), joined with
  control-character separators. The invariant it is built around: **two weapons share a key exactly when
  `WeaponComparer` calls them equal** — the comparer the pool already dedupes by — so one pool entry is
  always one option and neither side can see a weapon the other cannot.
- **Sort on the key, not on the name (#209's constraint).** The field separator is U+0001, below every
  printable character, so ordinal ordering on the key is *name-primary*: byte-identical to the old
  `OrderBy(Weapon.Name)` whenever names are unique (every shipped book today), and merely a deterministic
  tiebreak when they are not. The old sort was not total once names could repeat; this one is. Chosen
  over re-keying to `Weapon` reference identity, which the item explicitly warned against.
- **The reply is matched by profile, after rehydrating its rules.** A remote player's choice arrives as a
  deserialized `Weapon` whose `RuleDefinitions` is `[JsonIgnore]` and travels as the persisted blob, so
  the stage calls `RehydrateRules()` (idempotent; a no-op locally) before matching. This works only
  because #258 made `SpecialRuleDefinition` equality canonical-name-based — reference equality would have
  made every rehydrated definition unequal and every remote profile match miss.
- **A failed profile match logs and degrades to the name match** rather than throwing. Faulting the state
  machine mid-activation is the exact outcome this item exists to remove, so it is never the fallback.
- **Melee label uniqueness is structural, not argued.** `StringSelectionRequest` carries strings and the
  reply IS one of them, so a label is an option's identity. Labels are now built over a profile-key-ordered
  pass (deterministic) and any repeat gets a ` #2` suffix. Display text is unchanged in every real case —
  see the correction in Notes.
- **The Sergeant rename stays, as attribution only** (Chris, 2026-07-31). Nothing depends on it for
  correctness now; `ListCompiler`'s comment says so, and the log/dice-row self-attribution it exists for
  ("Blood Squad's Sword (Sergeant)'s Sergeant added ...") is kept.
- **Deliberately NOT widened: the movement overlay's weapon sight profiles.** `WeaponSightProfileBuilder`
  dedupes by `weapon.Name` and `GuiDefineMovementResolver`'s `sightByWeapon` is name-keyed, so a split
  weapon's second profile is silently dropped from the range/threat overlay. Same defect class, but it
  degrades silently rather than faulting, and fixing it means re-keying the `WeaponRangeOverride` wire
  record and deciding how the overlay labels two same-named rings — a design call, not a mechanical
  re-key. Left open here rather than dropped quietly.

## Outcome

Both choosers key on the weapon PROFILE. The crash is gone, the right profile fires, and #209's ordering
is now a total order rather than one that happened to work while names stayed unique.

Engine changes:

- `WeaponProfileKey` + `WeaponPool.GroupByProfile` (new, in `GameObjects/Core/IWeapon.cs`). The grouper
  replaces `CombatActionContext.GetTypeSortedWeapons` — resolving that method's own
  `//TODO: Repeated in Ranged version. Move to static class.` — and is O(n) instead of the old O(n^2)
  linear scan per weapon.
- `ChooseRangedAttackStage`: options map, LoS-ignore map, per-target stats map, effective-range cache,
  Deadly-first gating, `CountEligibleCopies` and the chosen-weapon lookup are all profile-keyed. The
  option map now uses `TryAdd` (folding a repeat) rather than `Add` (throwing on one).
- `HasAnyFireableTarget` builds its pool through the shared grouper instead of its own name-dedupe, so
  the Shoot ACTION gate and the shoot STAGE cannot disagree about a split weapon (#200's invariant).
- `ChooseMeleeWeaponStage`: deterministic profile-ordered label pass + uniqueness guarantee; the
  "no protection against identical names" TODO is answered and removed.

Verification:

- `FutureOfDarkGrimness/Tests/SplitWeaponProfileTests.cs`, 11 pins: key-vs-comparer agreement, name-primary
  sorting, the split offered as two options with correct per-profile carrier counts, choosing either
  profile firing that profile at that profile's count, a wire-round-tripped choice still binding its
  profile, the shoot gate not faulting, option-order determinism, and both melee cases.
- **These pins reproduce the reported fault.** Reverted against the pre-fix stages, 6 of the 11 fail: 5
  ranged ones with `System.ArgumentException : An item with the same key has already been added. Key:
  Rifle` out of `BuildWeaponOptions`, and the melee one with two profiles collapsing to one label
  (`Expected: 2, But was: 1`). (`HasAnyFireableTarget_...DoesNotFault` passes pre-fix — the old gate
  deduped by name, which hid the split rather than crashing; it pins the gate/stage agreement, not a
  pre-fix crash.)
- Suites green: engine 2531/2531, app 854/854, full `dotnet build` clean, `printf "2\n2\n" | ... --headless`
  exits 0 with a result.
- **Play probe with a real split weapon**, the standard this bug was originally found by. Hand-authored
  `split-carbine-probe.fdgarmy` (a 3-model squad carrying 2 plain Carbines + 1 Precise Carbine, the exact
  "3 rifles, 1 upgraded" shape) run through `--scenario --headless`:
  - **pre-fix**: `[GAME ERROR] State machine faulted: System.ArgumentException: An item with the same key
    has already been added. Key: Carbine` on the first shoot.
  - **post-fix**: exit 0, plays to a result, `Chose weapon: Carbine. Count: 2.` and `Chose weapon:
    Carbine. Count: 1.` as separate volleys, with `Marksman Squad's Carbine's Precise added +1 to Hit
    rolls.` on the one-copy volley only.
  - A melee variant of the same army produced the same split on Blades (`Count: 2` plain / `Count: 1`
    Rending). Probe files are scratch, not committed (no corpus army splits a weapon today).
