# 153 — Army-Forge-style catalog army builder

**Status**: blocked (proposal only — awaiting a go decision; OPR-data path additionally gated on OnePageRules clearance)
**Related**: #106 (army builder authoring UX), #107 (combined squads), #003 (force-org validation), #059 (embedded rule definitions), #033/#034 (spells), `ArmyBuilderScreen`, `.fdgarmy` / `ArmyListFile`

## Goal

Add a **catalog-driven** army builder to FdgRaylib modelled on OnePageRules' Army Forge: pick a faction "book", add pre-statted units from its roster, customise each unit through grouped, costed **upgrade options** (never typing raw stats), with a live points total validated against a limit — then compile the result into the `.fdgarmy` the game already plays. "Done" = a player can build a legal list end-to-end from a book and launch a game with it, with no engine changes.

This is a multi-phase epic. It will fragment into per-slice items when picked up. This file is the master plan.

---

## TL;DR — the load-bearing insight

Our `.fdgarmy` (`ArmyListFile` = `List<UnitFileEntry>`, each a fully-resolved unit: name, model count, quality, defense, weapons-with-rules, point cost, embedded rule/spell definitions) is **already the compile target**. `FDGServer.CreateArmyDataFromArmyFile` builds runtime `UnitData` straight from a `UnitFileEntry` (`FDGServer.cs:208–216`). So an Army-Forge-like builder is:

```
   .fdgbook (catalog) ──┐
                        ├─►  Compiler  ─►  .fdgarmy  ──►  [existing game load path, unchanged]
   builder selections ──┘                 │
                                          └─ the SAME .fdgarmy also embeds the selections + a book slice,
                                             so the builder can reopen and re-edit the exact same file.
```

Everything new lives **app-side**: a catalog data format, a compiler, and a UI. The engine submodule stays untouched — including the single-file "embed the editable list inside the `.fdgarmy`" design (see Persistence, below), which works precisely because the engine's deserializer ignores unknown JSON. That is what makes this tractable and keeps it off the read-only submodule.

---

## Background — how Army Forge actually works

Verified 2026-06-30 by reading Army Forge's public JSON API (the site is a client-rendered Next.js SPA; the list page HTML is an empty shell, so data comes from `GET /api/army-books/<uid>` and `GET /api/share/<id>`). Concrete reference: the **Alien Hives** book (`w7qor7b2kuifcyvk`, v3.5.3, 41 units, 38 upgrade packages / 56 sections).

**Interface / flow:** game system → army book (faction) → points limit → add units from roster → configure each unit via upgrade sections → live `total / limit` header → save / share (`/view/<slug>?listId=<id>`) / export (TTS, PDF).

**Data model (the important part):**
- **Book** = `{ name, version, units[], upgradePackages[], customRules[], customWeapons[], spells[] }`.
- **Unit** = pre-statted: `{ name, size, cost, quality, defense, weapons[], rules[], upgrades[] }`. You don't author stats; you pick a roster entry.
- **Weapon** = structured, not a string: `{ name, range, attacks, specialRules[] }` + a derived `label` (`Shredder Cannon (18", A4, Rending)`).
- **Special rule** = a reference `{ id, name, rating }` → `AP(1)`, `Blast(3)`, `Deadly(3)`. `rating` is the numeric arg.
- **Upgrade section** = `{ label, variant, affects, targets, options[] }`:
  - `variant`: **replace** (34/56 in Alien Hives) swaps an existing weapon/item; **upgrade** (22/56) adds wargear/rules. (Other OPR books also have *pick-N* and *add-models* variants — model our grammar to cover them even though Alien Hives doesn't use them.)
  - `affects`: `{ type: "any" | "all" | ... }` — how many models in the unit the choice touches.
  - `targets`: which existing weapon/item the section replaces (e.g. `["Heavy Razor Claw"]`).
  - `options[]`: each `{ label, cost, gains[] }`. **gains** are `ArmyBookWeapon` (95), `ArmyBookItem` (58), or `ArmyBookRule` (2) — a chosen option grafts these onto the unit and adds `cost` to the running total.

So: *a book is a bag of units; a unit is base stats + a list of upgrade sections; picking an option mutates a working copy and sums cost.* That mutate-and-sum step is the compiler.

## Where our app is today

`ArmyBuilderScreen` (~980 lines) is the **inverse** model — a freeform stat editor. You type Models / Quality / Defense / Points / weapons / rules per unit and set `PointCost` yourself; `ArmyListFile.TotalPoints` just sums the fields you typed. #106 added a read-only stat-block preview + Duplicate; #107 (open) wants "Combined" squads. There is **no** catalog, roster, upgrade group, or auto-costing. It's excellent for authoring arbitrary units (which we still want — see "keep both", below) but it is not the guided, points-validated Army Forge experience.

## Licensing / OPR-clearance framing

Two separable things, so a green-light on one need not wait on the other:
1. **The tool** (catalog format + compiler + UI) — our own IP, buildable now if you want the mechanic.
2. **The data** (actual OPR army books / rules text / point values) — this is what OPR clearance governs. OPR publishes game rules under Creative Commons, and the Army Forge data is reachable via public API, but *shipping or importing their army-book content* is the part to clear with them.

**Design consequence:** make the catalog format **game-agnostic and source-neutral** so it can hold (a) our own FDG factions we author, or (b) imported OPR books — with the OPR-import path as an isolated adapter. Then "build the tool" and "populate it with OPR data" are independent switches.

## OPR versioning strategy (decided 2026-07-01)

**Constraint (user):** OPR's Army Forge API serves only the *current* version and updates army data + rules **simultaneously**, with **no legacy versions hosted anywhere**. The next update is expected **September 2026**. So the API is a *moving target*, not a stable dependency — if it advances to rules our engine can't yet handle, a live-API design would simply break.

**Strategy:** the API is a **one-time ingestion tool**, never a runtime dependency.
- **Snapshot now → local files.** Import the *current* online versions into **bundled local `.fdgbook` files** shipped with the app (a checked-in "book library"). The app builds armies from these local snapshots, offline, against a known-good version.
- **Advance on our schedule.** When OPR updates (Sept 2026), we stay deliberately **one version behind** until we (a) re-run the importer to capture the new snapshot and (b) confirm the engine handles any new rules. We are never *broken* by an upstream change — only intentionally behind.
- **Time-sensitive:** capture the **entire current book set before the September update**, or that version is gone for good. This is the one genuinely deadline-bound task in the whole feature.
- Each bundled `.fdgbook` records the source **OPR version string** (e.g. `V3.5.3`) for provenance and to detect drift. (`BookFile.Version` already exists.)
- **Scope of capture:** rules/stat data only (unit profiles, weapons, upgrade options, point costs) — **not** the books' background/lore prose, which we neither need nor want to reproduce. This keeps the snapshot to the game-mechanics data and off the copyrightable creative text.
- This composes with decision 6 (each *army* also embeds a full book snapshot): the **library** freezes versions for *authoring new* lists; the **per-army embed** freezes the version for *re-editing an old* list. Two layers, same principle.

---

## Target architecture

Pieces (the backend lives in the **engine submodule** under `FDG.ArmyBuilding`; only the screen is in `FdgRaylib/`):

- `ArmyBuilding/Catalog/` — the `.fdgbook` model + loader (`BookFile`, `RosterUnit`, `UpgradeSection`, `UpgradeOption`, `Gain`).
- `ArmyBuilding/List/` — the *selection* model (`BuilderList`): what the user is editing — unit *instances*, each = a `RosterUnit` ref + chosen option ids + model count. **Not a separate file** (see Persistence): it is embedded inside the compiled `.fdgarmy`.
- `ArmyBuilding/Compiler/` — `ListCompiler.Compile(BookFile, BuilderList) → ArmyListFile` (which then also carries the `BuilderList` + book slice back inside it). Pure, deterministic, unit-testable headless. **This is the heart of the feature.**
- `ArmyBuilding/Import/` — the OPR `/api/army-books/<uid>` → `.fdgbook` adapter (built in parallel per the data-source decision; *content use* stays clearance-gated).
- `ArmyBuilding/Validation/` — points-limit, min/max model counts, per-section legality, force-org (#003).
- `Rendering/ArmyForgeScreen.cs` — the three-pane UI (new screen; keep the freeform `ArmyBuilderScreen` alongside).

**Where do the catalog types live — app or engine?** **Engine submodule** (`FDG.ArmyBuilding`), *decided 2026-07-01* (was app-side; reversed — see decision 4). Portability: any app on the FDG library reuses the whole define/compile/import backend; the app is only the GUI. It also sits naturally next to its output (`ArmyListFile` in `FDG.SaveLoad`) and the types it subclasses. Only `ArmyForgeScreen` + its presentation formatters stay in `FdgRaylib/`.

### Persistence — one self-contained `.fdgarmy`, editable list embedded (decided)

No separate `.fdglist` file. The builder saves a **single `.fdgarmy`** that is both playable and re-editable:
- The top-level `Units[]` (+ embedded rule/spell defs) are the compiled, playable army — read by `FDGServer` exactly as today.
- An extra top-level block carries the **editable state**: the `BuilderList` selections **plus a full snapshot of the source `.fdgbook`** (the entire book as it existed when the army was authored — every unit, section, rule, and cost, not just the referenced ones). The army is therefore a self-contained, version-frozen artifact.

**No engine/submodule change required.** Verified 2026-07-01: `RuleJson.Options` (`RuleJson.cs:9`) leaves `UnmappedMemberHandling` at STJ's default (Skip), so the engine deserializing the file into `ArmyListFile` **silently ignores** the extra block. Mechanics: the app serializes/deserializes an app-side extended type (e.g. `BuiltArmyFile : ArmyListFile { BuilderList Selections; BookFile Book; }` — or an equivalent wrapper) using the same STJ options; the engine reads the base `ArmyListFile` view. Only the app ever *writes* `.fdgarmy` (the engine never re-saves it), so the extra block can't be silently dropped in normal play.

**Version-stability (why full book, decided 2026-07-01):** because the whole book is embedded, an old army re-opens and re-edits against the *book it was built with*, not whatever the current catalog has become. Play is doubly frozen — the compiled `Units[]` + `RuleDefinitions`/`Spells` already lock behaviour, and the embedded book locks the *options and costs* you'd see on re-edit. On open, **the embedded book is authoritative**: the builder never silently re-points the army at a newer catalog book. A later slice can offer an explicit, opt-in "**migrate this army to the current book version**" action (re-resolve selections against today's book, surface any options/costs that changed) — but never automatically. A hand-authored or legacy `.fdgarmy` with no embedded block still plays; the builder just can't re-edit it (offer "duplicate into freeform" instead). Cost is a larger file (a full book ≈ 150–200 KB of JSON, e.g. Alien Hives ≈ 168 KB) — negligible for a local army file, and the price of reproducibility.

### `.fdgbook` data model (proposed)

```csharp
class BookFile {
  string Name; string Faction; string Version;
  List<RuleDefinition> RuleDefinitions;   // reuse #059 embedded-rule types
  List<SpellDefinition> Spells;            // reuse #033 spell types
  List<RosterUnit> Units;
}
class RosterUnit {
  string Id; string Name;
  int MinModels; int MaxModels; int BaseModelCount;   // fixed-size or ranged
  int Quality; int Defense; BaseFileEntry Base;
  int BasePointCost;                                   // cost at BaseModelCount, no upgrades
  int PointsPerExtraModel;                             // for add-models variant, if used
  List<WeaponFileEntry> Weapons;                       // reuse existing type
  List<SpecialRuleEntry> Rules;                        // reuse existing type
  List<UpgradeSection> Sections;
}
enum UpgradeVariant { Replace, Upgrade, PickN, AddModels }
class UpgradeSection {
  string Label; UpgradeVariant Variant;
  AffectsSpec Affects;        // one model / any / all / count
  List<string> Targets;       // weapon/item names this replaces (Replace only)
  int MinPicks; int MaxPicks; // PickN
  List<UpgradeOption> Options;
}
class UpgradeOption {
  string Id; string Label; int Cost;
  List<Gain> Gains;           // weapons / rules / (items→rules) to graft on
}
```

Note we can **reuse** `WeaponFileEntry`, `SpecialRuleEntry`, `BaseFileEntry`, `RuleDefinition`, `SpellDefinition` — the vocabulary already exists. `Gain` is the only genuinely new leaf, and it's a thin union over "add weapon / add rule / set stat".

### The compiler contract

`Compile(book, list) → ArmyListFile`:
1. For each list unit instance: clone its `RosterUnit` into a working `UnitFileEntry` at the chosen model count (start from base weapons/rules, `PointCost = BasePointCost + extraModels * PointsPerExtraModel`).
2. Apply each selected option in order: `Replace` removes `Targets` from the unit's weapon/rule set and adds the option's `Gains`; `Upgrade`/`PickN` add gains; `AddModels` bumps count/cost. Accumulate `option.Cost` into `PointCost`.
3. Carry embedded `RuleDefinitions`/`Spells` from the book onto the `ArmyListFile` (the #059/#033 rehydration path already registers them at load).
4. Wrap the result as the app-side extended file: the compiled `Units[]` (playable) **plus** the `BuilderList` selections and a **full snapshot of the source book** (editable, version-frozen). To `FDGServer` this is indistinguishable from a hand-authored `.fdgarmy`; to the builder it round-trips back into an editable session.

Properties to test: **compile is total and deterministic**; a compiled army re-loads and plays (headless smoke); and **round-trip** — save → reopen in builder → the reconstructed `BuilderList` equals the original (and recompiling yields the same `Units[]`).

---

## Audit findings (2026-07-01, deeper model pass)

Two codebase probes + a PDF check tightened the plan:

1. **Per-model weapons are aggregate, round-robin-distributed (viable, with a caveat).** `UnitData` (`UnitData.cs:134–180`) expands each `WeaponFileEntry` by `Quantity` into individual `Weapon`s (after sorting by `Quantity` ascending) and round-robins them across models; each `ModelData` owns its `Weapons` list (`ModelData.cs:64`). Heterogeneous units work, but only *implicitly* via counts — there is **no explicit "model 3 carries the plasma" field**. So the compiler operates on **aggregate weapon quantities**: "replace one X with Y" = `X.Quantity−−, Y.Quantity++`, which distributes cleanly. OPR's "Replace one/any/all X" grammar is itself aggregate, so this fits — but *exact per-model loadout control* is a fidelity limit we accept (documented in non-goals).
2. **A compiled `.fdgarmy` needs almost no new launch wiring.** The lobby already has a per-player **"Load Army"** button (`LobbyScreen.cs:243` → `TryLoadArmyForPlayer` → `UpdateArmyListFile`), and headless `ArmyLoader` loads a file too. Because our output *is* a `.fdgarmy`, P2's "play it" reduces to **Save → Load Army** through the existing path; a dedicated "Use my built army" hand-off button is a nice-to-have, not required. Networked players already ship their army via `ArmyListUpdateMessage.FromArmy()`.
3. **The one hard launch gate: rule validity.** Embedded `RuleDefinitions` must pass `RuleValidator.Validate()` or army load **throws** `RuleValidationException` (`ArmyListRuleResolution.cs:31–51`); unit rule *references* with no matching definition are silently skipped with a debug warning (`:70–79`). ⇒ the compiler must carry the book's (already-valid) rule/spell definitions onto the army, and a compiler test must assert a compiled army with rules **loads without throwing**. Force-org caps (`ForceOrgValidator`) are advisory and never block launch — so points/legality enforcement is entirely *our* UI's job (P4).
4. **`BuiltArmyFile` must be serialized as the derived type.** `ArmyBuilderScreen.Save()` does `JsonSerializer.Serialize(_army, RuleJson.Options)` with `_army` typed `ArmyListFile`; STJ serializes by the *declared* type, so the embed only survives if the forge screen serializes a `BuiltArmyFile`-typed value (and `Deserialize<BuiltArmyFile>` to reopen). `Deserialize<ArmyListFile>` on the same file skips the block — confirming the no-engine-change claim end to end. `SpecialRuleEntry` is polymorphic (`_Core`/`_CoreNumeric`) and already handled by `RuleJson.Options`.
5. **Cost scaling for `any`/`all` sections.** OPR "Replace/Upgrade **any** X" applies 0..N times, cost = `count × option.cost`; "**all**" applies to every eligible model. So an `UpgradeOption` needs a per-application cost and the UI a count stepper for `any`; the compiler multiplies cost by applied count. (A flat per-section cost only covers the `one`/pick-one case.)
6. **Import source:** the OPR **JSON API** (`/api/army-books/<uid>`) returns the exact book, cleanly structured — vastly easier than parsing the two-column PDF tables. Used as a **one-time ingestion tool** to snapshot the current version into bundled local `.fdgbook` files (see "OPR versioning strategy") — **not** a runtime dependency. PDFs (in `../GDF Armies/`, all 47, V3.5.3) are the human ground-truth for verifying a snapshot.
7. **New deferred-fidelity items (recorded, not silently cut):** inter-upgrade **dependencies / mutual exclusivity** ("if you took X you can't take Y") — our data model has no `requires` predicate yet (P4/P5); **hero-join UI** — `UnitFileEntry.Id`/`JoinsUnitId` (#006) let a Hero attach to a unit; the forge must expose that (P3/P5); **`ArmyBookItem`→rules mapping** — OPR "items" (58 gains in Alien Hives) are named bundles of rules/weapons; `Gain` must model an item as such (P0b import). The embedded book need **not** travel over the network (only compiled `Units[]` matter for play) — keeps `ArmyListUpdateMessage` lean.

## Phased delivery plan (vertical slices)

Per house rule — one slice at a time, each with an integration test mirroring the nearest `*SerializationTests` / builder test, verified green, committed, ledgered. Each slice below is independently shippable and leaves the app working.

- **P0 — Catalog format + compiler + round-trip file + tests (no UI).** Define `.fdgbook` + `BuilderList` types and the app-side extended `.fdgarmy` wrapper (STJ round-trip like `ArmyListFileSerializationTests`), and `ListCompiler`. Ship **one hand-authored FDG demo book** (2–3 units, a couple of replace/upgrade sections). Tests: build a `BuilderList` in code → compile → assert the `ArmyListFile` matches an expected hand-written one; **reopen the saved `.fdgarmy` → reconstructed `BuilderList` equals the original**; the *engine* loads the same file ignoring the embedded block; headless smoke plays it. *Done-when:* compiler + round-trip are covered and a compiled army plays via `printf | dotnet run -- --headless`.
- **P0b — OPR import adapter + snapshot the current book library (TIME-SENSITIVE, before ~Sept 2026).** Map OPR's `/api/army-books/<uid>` JSON → our `.fdgbook`, then run it once to capture the **current** version of the book set into bundled local `.fdgbook` files (checked in under `FdgRaylib/Assets/Books/`). Stat/rule data only, not lore prose; stamp each with the OPR version. This both pressure-tests the format against real data *and* freezes the current version before the API advances (see "OPR versioning strategy"). *Done-when:* the current books import to valid `.fdgbook` files that compile playable armies, and the snapshot is committed.
- **P1 — Read-only book viewer + points header.** New `ArmyForgeScreen`: left pane lists the book's roster; center shows a selected unit's stat block (reuse #106's read-only block); top shows `0 / limit`. No mutation yet. *Done-when:* screen loads a book and renders roster + stats.
- **P2 — Add / remove units → compile → save → reopen → play.** Clicking a roster entry adds an instance; remove/duplicate; live points via the compiler; **Save writes the single embedded `.fdgarmy`, Load reopens it back into an editable session**; a "Launch" path routes the compiled army into the existing `LobbyScreen`/`ArmyLoader` flow. *Done-when:* a no-upgrades list built in the GUI saves, reopens, and launches a game.
- **P3 — Upgrade sections (replace + upgrade) with auto-costing.** Per selected unit, render its sections; `Replace` = radio/dropdown (one of N), `Upgrade` = toggles; selecting re-compiles and updates points + stat block live. *Done-when:* choosing options changes weapons/rules/points and the compiled army reflects them in-game.
- **P4 — Validation + legality.** Points-limit enforcement (block/flag over-limit), min/max model counts, per-section pick limits, and force-org (#003) hooks. Surface violations inline (Army Forge shows a red invalid marker). *Done-when:* an illegal list is clearly flagged and can't launch.
- **P5 — pick-N / add-models variants + Combined squads (#107).** The remaining upgrade variants and the "doubled-up" merged unit, which falls out naturally as an `AddModels`-style compile step. *Done-when:* a unit with a variable model count and a combined squad compile correctly.
- **P6 (optional) — share / export.** Shareable list link and/or PDF/text export, mirroring Army Forge's view/print.

Slices P1–P4 are the core product; P0 must precede all of them; P0b runs alongside P0/P1; P5+ are extensions.

## Integration points (real files)

- `FdgRaylib/Rendering/ArmyForgeScreen.cs` — **new** screen. Keep `ArmyBuilderScreen` (freeform) for authoring/one-offs; add a menu entry to choose builder mode.
- `FdgRaylib/Program.cs` — wire the new screen into the screen graph (MainMenu → ArmyForge), same pattern as `ArmyBuilder`.
- `FdgRaylib/Cli/ArmyLoader.cs` + `LobbyScreen` — the compiled `ArmyListFile` feeds the *existing* army-selection path; no new game plumbing.
- `FDGServer.CreateArmyDataFromArmyFile` — **unchanged**; it already consumes `UnitFileEntry`.
- Reuse: `WeaponFileEntry`, `SpecialRuleEntry`, `BaseFileEntry`, `RuleDefinition` (#059), `SpellDefinition` (#033), `RuleJson.Options` for STJ.

## Testing strategy

- **Serialization:** `.fdgbook`/`.fdglist` round-trip tests mirroring `ArmyListFileSerializationTests`.
- **Compiler:** table-driven — (book + selections) → expected `ArmyListFile`; cover replace/upgrade/pick-N/add-models and cost accumulation; a "compile is total" fuzz (random legal selections never throw).
- **Play-through:** headless smoke on a compiled army (exit 0 + expected log line), per house rule for playable paths.
- **UI:** follow #106's `ArmyBuilderScreenTests` approach for the new screen's logic.

## Decisions (locked 2026-07-01)

1. **Scope depth:** ✅ Full catalog + upgrades — build P0–P4 as the real target.
2. **Data source:** ✅ Both — hand-author FDG books AND import OPR books, but OPR import is a **one-time snapshot into bundled local `.fdgbook` files** (never a live-API runtime dependency; see "OPR versioning strategy"). Time-sensitive: capture the current version before the ~Sept 2026 OPR update.
3. **Screen strategy:** ✅ New `ArmyForgeScreen` alongside the freeform `ArmyBuilderScreen` (kept).
4. **Catalog location:** ✅ **Engine submodule** (`FDG.ArmyBuilding`) — *reversed 2026-07-01*. Originally chosen app-side to avoid touching the read-only submodule, but the user's portability requirement (any app on the library reuses the backend; the app is *only* the GUI) makes engine-side correct. The app keeps only `ArmyForgeScreen` + its formatters. Engine changes authorized for this feature; submodule-first commit cadence.
5. **Persistence:** ✅ A single `.fdgarmy` with the editable `BuilderList` (+ book) embedded — no separate file, and no engine change (engine ignores the extra block).
6. **Embedded-book granularity:** ✅ **Full book snapshot** — embed the entire source book, so an old army stays reproducible and re-editable as-authored even after the catalog book changes in a later version. Embedded book is authoritative on open; migrating to a newer book is an explicit opt-in action, never automatic.

### Still open (sub-decisions, not blockers)
- **Should the freeform `ArmyBuilderScreen` be able to open an embedded `.fdgarmy`** for off-catalog tweaks, and if so does a manual edit invalidate the embedded selections? Lean: allow open, and mark the embedded block stale on manual edit.

## Explicit non-goals / deferrals (record, don't cut silently)

- No engine changes at all (by design — the embedded-list file works precisely *because* the engine ignores unknown JSON).
- OPR *content distribution* is out of scope until OPR clears it; the import adapter (P0b) may still be built and format-tested against public data.
- share / export (P6) is optional and out of the core.
- Not deleting or replacing the freeform `ArmyBuilderScreen` (kept alongside).
- Faction-level special detachment rules / army-wide upgrades beyond per-unit sections are out of scope until a book needs them.
- **Explicit per-model loadout control** is out of scope — weapon assignment is aggregate (counts), realized by the engine's round-robin (audit finding 1). Fine for OPR-style upgrades.
- **Inter-upgrade dependencies / mutual exclusivity** (`requires`/`excludes` predicates) are deferred to P4/P5 (audit finding 7); many books don't need them, and the compiler stays correct without them (it just won't *prevent* an illegal combo until then).

## Rough effort (relative)

P0 (M, compiler + round-trip file is the risk) · P0b (M, adapter, runs in parallel) · P1 (S) · P2 (M) · P3 (M–L, most UI) · P4 (M) · P5 (M) · P6 (S–M, export). Core (P0–P4) is the bulk of the value.

## Notes

- 2026-07-01: **P3 landed — interactive upgrades; core army builder is complete.** The config pane's upgrade sections are now editable for the selected list unit: single-select (`Replace`/`Upgrade` Affects=One) as mutually-exclusive checkboxes, `Replace-All` as on/off, `AddModels`/`Any` as a numeric stepper (clamped to a computed max). Each toggle writes an `UpgradeChoice` into the `BuilderUnit`; the per-frame recompile re-costs + re-statblocks live. Testable seams `IsCounted`/`ChoiceCount`/`IsChosen`/`SetChoice` — 4 new tests proving the UI path reproduces the compiler's costs (warriors 106, gunners 165). **End-to-end verified:** a fully customized army (plasma + 2 added warriors + 3 missiles, 271 pts) built via the choice path plays a full headless game to exit 0. App 62/0, engine 976/0, build 0 warnings. **Deferred (recorded):** MaxPicks>1 caps, hero-join UI, points-limit *enforcement* (header flags over-limit red but doesn't block) → P4. **Awaiting GUI hand-verification** (P1+P2+P3). Next: P4 validation/legality, then P0b OPR snapshot.
- 2026-07-01: **P2 landed — list-building in `ArmyForgeScreen`.** The list pane is real: select a roster unit → "+ Add to list", per-row remove (`x`), and the points header live-recompiles via `ListCompiler` every frame (red when over limit). **Save** writes the single embedded `.fdgarmy` (serialized as `BuiltArmyFile` so the selections + full book ride along); **Load** deserializes as `BuiltArmyFile` and `AdoptLoaded` reopens it into an editable session (a plain army with no embed is refused with a hint → use the Army Builder). The compiled `.fdgarmy` loads straight into the lobby's existing "Load Army" — no new launch wiring. Config pane shows the selected list unit's compiled stats; **upgrade-option *editing* is still read-only (P3).** Testable seams `AddToList`/`RemoveFromList`/`Compile`/`AdoptLoaded` — 5 new app tests incl. a Save→Load round-trip (185 pts base). App 58/0, engine 976/0 (untouched), build 0 warnings, headless exit 0. **Awaiting GUI hand-verification** (P1+P2 together). Next: P3 — wire upgrade options to mutate + re-cost.
- 2026-07-01: **Architecture correction — backend moved into the engine submodule (`FDG.ArmyBuilding`).** User flagged that army definition/compile/import must be library-portable (any app reuses it; the app is only the GUI). I had wrongly built it all app-side (decision 4, to dodge the read-only submodule). Reversed with the user's authorization: `BookModel`/`BuilderList`/`BuiltArmyFile`/`ListCompiler`/`DemoBook` → `FutureOfDarkGrimness/ArmyBuilding/` (namespace `FDG.ArmyBuilding`); backend tests → engine `FDG.Tests`; only `ArmyForgeScreen` + its formatters remain in `FdgRaylib/` (usings → `FDG.ArmyBuilding`). Engine 976/0 (+7 moved tests), app 53/0, headless smoke exit 0 (compiled army still plays). Submodule-first cadence: submodule branch `153-army-forge-builder` committed first, then the superproject bumps the gitlink. Future OPR importer (P0b) also lands engine-side.
- 2026-07-01: **P1 landed — `ArmyForgeScreen` (three-pane, user's chosen layout), wired into the app.** New `IAppScreen`: roster pane (browse the book's units) | list pane (empty placeholder; building is P2) | config pane (selected unit's stat line + weapons + rules + upgrade sections, read-only) + right-aligned points header. Loads `DemoBook`. Wired: `RaylibRenderer.ArmyForge`, a MainMenu "Army Forge" button (menu now 6 buttons; start/gap tightened), `Program.cs` nav + Back. Reuses `ArmyBuilderScreen.WeaponSummary` + `SpecialRuleEntry.PrintableName`. 4 `ArmyForgeScreenTests` on the pure formatters. App 60/0, build 0 warnings, headless exit 0. **Awaiting GUI hand-verification** (no display in the build env — please eyeball the screen + Back nav). Next: P2 — add/remove units, live compile, Save `.fdgarmy`.
- 2026-07-01: **P0 slice 2 landed — the `ListCompiler` (the heart).** `DemoBook` (our own FDG "Dark Vanguard" — original IP, core rules only) + `ListCompiler.Compile(book, list) → BuiltArmyFile`: aggregate weapon-count ops (replace-one, replace-all cost-scaled by target count, add-models count-scaled with the added model's default weapon, upgrade→rule grant), carries rule/spell defs for the RuleValidator gate, embeds selections + full book. 4 `ArmyForgeCompilerTests` (deterministic loadouts + costs). **Headless smoke: the compiled army played a full 4-round game to exit 0**, with the Missile Launcher's `AP(2)`/`Blast(3)` resolving in the real engine — strongest end-to-end proof. App 56/0, engine 969/0, build clean. Compiler-first per user (defers OPR snapshot, still ahead of the ~Sept deadline). Next: P1 `ArmyForgeScreen` UI. Data-source decision refined → OPR import is a one-time snapshot to a bundled local `.fdgbook` library (see "OPR versioning strategy").
- 2026-07-01: **BUILD STARTED (user green-lit). P0 slice 1 landed** — `FdgRaylib/ArmyBuilding/` catalog + selection + embedded-file types (`BookFile`/`RosterUnit`/`UpgradeSection`/`UpgradeOption`, `BuilderList`/`BuilderUnit`/`UpgradeChoice`, `BuiltArmyFile : ArmyListFile`) and `ArmyForgeSerializationTests` (3 tests). Proven: the format round-trips through `RuleJson.Options`, and the **engine reads a builder-saved file as a plain `ArmyListFile`, dropping the embedded `selections`/`book`** — the no-engine-change claim, now under test. App suite 52/0, engine 969/0 (untouched), full build clean 0 warnings. Next: P0 slice 2 = `ListCompiler`. Deeper audit (2 codebase probes + PDF check) folded into the plan first — see "Audit findings (2026-07-01)".
- 2026-07-01: Locked the embedded-book granularity (decision 6) to **full book snapshot** at user's request — an old army stays reproducible/re-editable as-authored even after the catalog book drifts; embedded book is authoritative on open, migration is explicit opt-in. Still a proposal — not building yet.
- 2026-07-01: Refined after locking the four scoping decisions (see Decisions): full scope; both data sources in parallel; new screen keeping freeform; single self-contained `.fdgarmy` with the editable list embedded. Verified the embed needs **no submodule change** — `RuleJson.Options` (`RuleJson.cs:9`) leaves STJ's `UnmappedMemberHandling` at the default (Skip), so the engine ignores the extra block. Still a proposal — not building yet.
- 2026-06-30: Plan authored at user's request as a hold-until-go artifact. User is undecided on whether to build at all; a green-light (and, for OPR data, OPR clearance) unblocks it. Investigated Army Forge's live API + our army-file/runtime seam to ground the plan; no code written.
