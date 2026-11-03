# 354 - A saved .fdgarmy freezes its rule definitions, so newly implemented rules go inert

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
$ dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless --army "armies/3k - Saurian Starhost.fdgarmy"
[rules] Skipping unimplemented special rule 'Heavy Impact(3)' on unit 'Ripjawdactyl Riders'.
```

## Blast radius (local lists, surveyed 2026-08-05)

**9 lists silently drop a rule the engine implements today:**

| List | Faction | Inert |
|---|---|---|
| `3k - Saurian Starhost.fdgarmy` (root + `armies/`) | Saurian Starhost | Heavy Impact |
| `armies/3k - Eternal Dynasty.fdgarmy` | Eternal Dynasty | Vengeance |
| `armies/2k - Alien Hives - Horde Melee.fdgarmy` | Alien Hives | Piercing Growth |
| `armies/3k - DAO Union.fdgarmy` | DAO Union | Ambush Beacon |
| `armies/3k - Goblin Reclaimers.fdgarmy` | Goblin Reclaimers | Instinctive |
| `armies/High Elf Fleets 2k - Caster-Heavy.fdgarmy` | High Elf Fleets | Piercing Spotter |
| `armies/2k - Human Defense Force - Tough and Vehicle-Heavy.fdgarmy` | Human Defense Force | Extended Buff Range, Mobile Artillery |
| `armies/2k - Robot Legions - Mixed.fdgarmy` | Robot Legions | Casting Buff |

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

### 2026-08-05 - implemented (engine `e44dd1f`, superproject `19b0a14`)

Engine: `SaveLoad/CurrentRulebook.cs` is the install point (`ICurrentRulebook`: per-faction
definitions + a name-known query); the host fills it, and with nothing installed the engine
behaves exactly as before this item. `ArmyListRuleResolution.RegisterEmbeddedDefinitions` now
registers `EffectiveDefinitions` = backfill (names the army does NOT define) then the army's
own, so the army always wins on a name it has. `ERuleDropReason.OutdatedList` classifies a name
the rulebook defines but the resolver could not find.

App: `FdgRaylib/Import/BundledBookRulebook.cs` serves it from `Assets/Books` - the faction's
book for definitions (cached, negative results too), `GdfRuleSupplement.json` for the
name-known query. Installed in `Program.cs` before the headless branch. `RuleLoadWarnings`
gained `SummarizeOutdated`; the misauthored count excludes both no-definition reasons; the
army builder pane shows the new line.

**Found while implementing - the resume snapshot needed the same fix.**
`GameBootstrap.CreateArmy` persisted `armyListFile.RuleDefinitions` onto `ArmyData` for #095's
resume replay. Attachments carry their full definition and survive regardless, but a resumed
game's BY-NAME lookups (a `RuleGrant` token, a unit created mid-game via Spawn/Split) rebuild
from that snapshot - a backfilled rule missing from it would be dead on resume. Now persists
`EffectiveDefinitions`. Pinned by `BackfilledDefinition_SurvivesIntoTheResumeSnapshot`.

Tests: `Tests/ArmyRulebookBackfillIntegrationTests.cs` (7) drives the real launch path against
a stub rulebook - the pre-fix drop still pinned, backfill attaches, the army's own definition
wins over the rulebook's, no-faction-match classifies as `OutdatedList`, an unknown name stays
`Unimplemented`, audit agrees with launch, resume keeps it. `ArmyRuleAuditParityTests` gained a
`Grudge` reference + an installed stub so `Audit_CoversEveryDropReason` keeps covering every
reason. App side: `BundledBookRulebookTests` runs against the REAL shipped assets (both
reported cases resolve; the stale Saurian shape audits clean), `RuleLoadWarningsTests` +5
including the ASCII pin.

Verified: engine 2842 green, app 1086 green, `dotnet build` clean, default headless smoke
exits 0. Both reported armies load with **zero** rule drops and play to a result:
`--headless --army "armies/3k - Saurian Starhost.fdgarmy"` and the Eternal Dynasty 3k list.

**Residual, not a bug in this fix:** the reported `armies/3k - Saurian Starhost.fdgarmy` bought
"Replace all Energy Shields and CCWs -> Shock Pistol", so its Ripjawdactyl Riders carry no
melee weapon and never charge - Heavy Impact resolves and attaches now, but that list can
never trigger it. A `--trace-rules` run shows them only ever as the Subject of someone else's
charge. Confirmed as a legitimate book upgrade option, not a compiler fault.

**Deliberately NOT done** (owner ruling was gap-fill only): the 10 lists pinning a superseded
COPY of a definition (Armor, Bounding, Infiltrate, Mischievous, Mobile Artillery) still play by
their frozen wiring, and no tool re-stamps `.fdgarmy` files on disk - the `dist/` armies cut
2026-08-03 still carry the stale sets. A `--refresh-army-rules <fileOrDir>` retrofit, mirroring
`--retrofit-effects`, is the shape that would close both if they ever matter.
