# 342 - A saved .fdgarmy freezes its rule definitions, so newly implemented rules go inert

**Status:** in progress (opened 2026-08-05)

## Goal

A compiled `.fdgarmy` embeds a copy of its book's `RuleDefinitions` at save time
(`ListCompiler.cs:36`). Army load resolves rule names against the core catalog **plus that
embedded copy only** - nothing consults the bundled book. So when the engine later implements a
rule and the book gains its definition, every list saved before that day keeps referencing the
name with nothing behind it, and the rule silently does nothing for the rest of the file's life.

Reported from a live game: "Loading said that two special rules aren't implemented: Heavy
Impact(3) and Vengeance ... But I thought 197 said all special rules were implemented."

#197 is correct - `--rule-coverage FdgRaylib/Assets/Books` still reports `Total references:
13870 / Dead: 0`. Both rules are implemented, authored, and embedded in the shipped books
(Heavy Impact `27fcf08` 2026-07-23, Vengeance `3458ddb` 2026-07-30). The lists in play were
saved before those commits.

Reproduced:

```
$ dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless --army "armies/Saurian Starhost 3k.fdgarmy"
[rules] Skipping unimplemented special rule 'Heavy Impact(3)' on unit 'Ripjawdactyl Riders'.
```

## Blast radius (local lists, surveyed 2026-08-05)

**9 lists silently drop a rule the engine implements today:**

| List | Faction | Inert |
|---|---|---|
| `Saurian Starhost 3k.fdgarmy` (root + `armies/`) | Saurian Starhost | Heavy Impact |
| `armies/Eternal Dynasty 3k.fdgarmy` | Eternal Dynasty | Vengeance |
| `armies/Alien Hives 2k - Horde Melee.fdgarmy` | Alien Hives | Piercing Growth |
| `armies/DAO Union 3k.fdgarmy` | DAO Union | Ambush Beacon |
| `armies/Goblin Reclaimers 3k.fdgarmy` | Goblin Reclaimers | Instinctive |
| `armies/High Elf Fleets 2k - Caster-Heavy.fdgarmy` | High Elf Fleets | Piercing Spotter |
| `armies/Human Defense Force 2k - Tough and Vehicle-Heavy.fdgarmy` | Human Defense Force | Extended Buff Range, Mobile Artillery |
| `armies/Robot Legions 2k - Mixed.fdgarmy` | Robot Legions | Casting Buff |

The `dist/` builds cut 2026-08-03 ship the same stale armies.

**10 lists pin an outdated VERSION of a rule** - an embedded definition overrides core by name
(`RegisterOrReplace`), so these play by superseded wiring for Armor, Bounding, Infiltrate,
Mischievous, Mobile Artillery. **Deliberately NOT fixed here** - see Decisions.

## Decisions

- **Owner ruling 2026-08-05: gap-fill only, not a refresh.** On load, match the army's faction
  to a bundled book and register the book's definitions for names the file does **not** define;
  a definition the file already carries is never replaced. So a list's existing rules behave
  exactly as they did when saved, and only genuinely absent ones are filled in. The 10 lists
  pinning drifted copies keep their old wiring - that is the accepted cost of the conservative
  option, and the alternative ("book wins") was considered and rejected in the same ruling.
- **Owner ruling 2026-08-05: split the drop message.** `ERuleDropReason.Unimplemented` claimed
  "not implemented" for a name that is in fact implemented - exactly the confusion that opened
  this item. A new reason distinguishes "the current rulebook defines this, your saved list
  predates it" from "no definition anywhere". With the backfill in place the new reason only
  fires when the army's faction matches no bundled book (freeform / hand-authored lists), which
  is the honest residual case.
- **The name-known query reads `GdfRuleSupplement.json`, not all 47 books.** 251 supplement
  names cover every book definition except 9 per-book `"... Effect"` helpers, and those 9 are
  never referenced as a list rule entry (verified by walking every book's unit/weapon/upgrade
  references), so they can never reach the classification site.

## Notes

### 2026-08-05 - opened

Diagnosis and survey above. Implementation slice: engine install point + gap-fill + new drop
reason; app-side bundled-book source + warning copy.
