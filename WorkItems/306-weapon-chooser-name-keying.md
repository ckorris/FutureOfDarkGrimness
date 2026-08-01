# 306 — The weapon choosers key by weapon NAME, and fault on a duplicate

**Status**: todo
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

- 2026-07-31: Filed out of #197 on its close. Recorded in that item's hygiene section since the Sergeant
  slice (2026-07-29); it is not #197 work.

## Decisions

_(none yet)_

## Outcome

_(written when the item closes)_
