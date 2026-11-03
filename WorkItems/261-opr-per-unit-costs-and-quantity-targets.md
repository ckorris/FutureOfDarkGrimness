# 261 — Import mispriced 39% of upgrades, and quantity-prefixed replace targets never matched

**Status**: in-progress (both fixes landed + books re-priced; awaiting GUI hand-verify)
**Related**: #219 (introduced the flat-first cost order this corrects), #241 (share-link importer),
#153/#156 (the Forge), #262 (the residual reconciliation gap this work exposed)

## Goal

A list imported from Army Forge prices and equips the same way Army Forge does. Done = (a) every upgrade
option is charged its per-unit price; (b) a "Replace 2x X" swap finds its targets, consumes all of them,
and is charged; (c) the bundled books carry the corrected prices; (d) regression tests pin both.

Reported 2026-07-23 (Chris) against a real 2985-pt High Elf Fleets list (`share?id=UGDhJMMH0QBP`) that
imported as 2750, with the Anti-Gravity Tank showing Stealth at 65 (should be 30) and the Prism Cannon at
55 (should be 45), and with the Prism Cannon upgrade unpickable after being cleared ("no Rapid Shard
Cannons to replace" on a unit carrying two).

## Evidence (2026-07-23)

**Defect 1 - flat cost read in preference to the per-unit cost.** OPR publishes an option's price twice: a
flat top-level `cost` and a `costs[]` array keyed by unit id, because the same option costs differently on
different units. Army Forge charges the per-unit number. `OprBookImporter.MapOption` read
`o.Cost ?? o.Costs?...` - flat first - so the per-unit price was consulted only when the flat key was
absent. Measured across all 47 bundled books (8434 (unit, option) pairs, fetched live):

| both present, agree | both present, DISAGREE | per-unit only | flat only | neither |
|---|---|---|---|---|
| 1603 | **3314** | 3517 | 0 | 0 |

Every pair has a per-unit entry, so `costs[]` is always authoritative and complete; 3314 options (39%) were
charged the wrong number. Some flat values are outright junk (negative prices: Wormhole Daemons' Exalted
Plague Spear at -10). #219's test only ever exercised options whose flat cost was ABSENT, which is why the
conflict case survived it.

**Defect 2 - quantity-prefixed replace targets.** OPR writes a swap's multiplicity into the target TEXT:
the Anti-Gravity Tank's section is `targets: ["2x Rapid Shard Cannon"]` while the weapon is
`Rapid Shard Cannon` x2. Nothing stripped the prefix, so `TargetMatches` compared the whole string against
weapon names and never hit. Consequences, all from one miss: the swap applied nothing (both cannons stayed,
no Prism Cannon), cost nothing (-45 pts), and `AvailableApplications` returned 0, so the Forge greyed the
section out as "(none to replace)" and the user could not re-pick the upgrade after clearing it. 10 such
targets across 6 books (Alien Hives, DAO Union, Eternal Dynasty, High Elf Fleets, Robot Legions, Wolf
Brothers).

The user reported these as two separate problems; for the Anti-Gravity Tank they are one defect.

## Notes

- 2026-07-23: Both fixed in the engine (`d023a54`).
  - `OprBookImporter.MapOption`: per-unit entry wins, flat scalar is the fallback. Only an option with
    neither key stays `CostUnpriced`.
  - `ListCompiler`: new `ParseTarget` splits `"2x Name"` into (name, per-application count). `MatchedCount`
    divides matched copies by it (2 cannons afford exactly one 2x swap), `RemoveTarget` consumes that many
    per application (so both cannons go, not one), and `AttachRuleToWeapons` uses the parsed name.
  - Re-priced all 47 bundled books through the fixed importer (`--import-book FdgRaylib/Assets/Books`).
    The Anti-Gravity Tank now reads 20 / 45 / 195 / 10 / 30 / 40 / 45 / 30 - every number matching the
    Army Forge screenshot.
  - Reported list: 2750 -> 2860 (cost fix) -> 2905 (target fix), against Army Forge's 2985. The verbatim
    (non-Forge) import already totalled 2985 and still does. The residual 80 is #262, not this item.
  - Verified: engine 1903/1903 (9 new), app-side 447/447, `dotnet build` 0 errors, headless smoke exits 0.

## Decisions

- **Fix target multiplicity in the compiler, not the importer.** Stripping "2x" at import would have made
  the section replace one cannon instead of two - right price, wrong loadout - and would have needed every
  book regenerated to take effect. Parsing it where targets are matched keeps the book data as OPR wrote it
  and fixes pricing, loadout, and the greyed-out UI together.
- **`ParseTarget` requires digits and a following space.** "xeno Blade" and "2xRapid" are left alone; a
  weapon name must never be silently truncated by an over-eager prefix rule.
- **Books are regenerated artifacts, so the fix had to land in the importer first.** Editing prices into
  the `.fdgbook` snapshots would have been wiped by the next `--import-book`.

## Outcome

_(pending hand-verification)_

Hand-verify checks:
1. Anti-Gravity Tank in the Forge: Prism Cannon 45, Spinner 195, Hologram Field 30, the four Rapid options
   10 / 30 / 40 / 45.
2. Select Prism Cannon, clear it, select it again - the section is never greyed out and the two Rapid Shard
   Cannons disappear from the weapon list when it is taken.
3. Re-import the reported share link: Forge total 2905 (the 80-pt gap is #262's Shard Carbine question).
4. Spot-check one other prefixed unit (Wolf Brothers' Wolf Combat Walker, "Replace 2x Walker Fist").
