# 151 — Token display metadata + visual taxonomy

**Status**: in-progress
**Related**: token system (`Rules/Tokens/`, `Rules/Foundation/TokenType.cs`), #096 (transport visuals — owns `EmbarkedIn`), #087 (custom rule authoring — future author of token display metadata), #053 (placeholder-asset precedent), #033/#034 (spell tokens / casting)

## Goal

Make every token on a unit/model legible at a glance in the GUI: a small colored shape per token, drawn under the unit name (unit-scoped tokens) or over each model (model-scoped tokens), where the **color reads good/bad for the bearer** and the **shape distinguishes tokens from one another**, consistently across runs. Hovering a token shows its name + a plain-language description of what it does.

This is split into **two steps**. This item is the umbrella; **Step 1 (engine metadata + resolution, this file's spec)** is what we build first and is fully unit-testable in the engine suite with no rendering. **Step 2 (the GUI rendering)** will fragment into its own numbered item when Step 1 lands.

The guiding seam: **the engine says what a token *means* (semantic display data); the app decides what it *looks like* (pixels, palette, hashing, layout).**

---

## Scope split

- **Step 1 — engine (submodule), this spec.** New display-metadata types; a token-definition catalog that becomes the **single source of truth for each engine-known token type** (gameplay defaults + display, Option B), with the fixed-singleton creation sites refactored to construct through it; a `Valence` field on `SpecialRuleDefinition`; the resolution chains; and the `Describe()` synthesis — all producing a plain `TokenDisplayInfo` struct. No Raylib, no pixels. Tested in the engine NUnit suite. The creation-site refactor is **behavior-preserving** (suite stays green).
- **Step 2 — app (`FdgRaylib`), deferred to a follow-up item.** Curated palette per valence band, deterministic hash → color/shape mapping, the two-tier layout (first-class status line + generic chip row), count badges, valence sort, and the hover tooltip. Plus the dev "show all tokens" toggle and first-class bespoke icons (placeholdered à la #053).

---

## Step 1 spec (engine / submodule)

### New types (all in `Rules/Foundation` or `Rules/Tokens`)

- **`enum EValence { Positive, Negative, Neutral }`** — effect on the **bearer** (bearer-relative; see Decisions). Drives the color band and the on-model sort.
- **`enum ETokenProminence { Normal, FirstClass, Invisible }`** — single axis bundling visibility + prominence (forbids the nonsensical invisible-and-first-class combo). `FirstClass` → drawn in the prominent status tier with bespoke iconography; `Invisible` → not drawn at all (defeated by an app-side dev toggle).
- **`enum ETokenColor { … }` / `enum ETokenShape { … }`** — *named* palette/shape slots for authored overrides. Enum-valued, **not RGB**, so the engine never learns pixels. The app maps these enums to actual colors/shape-drawers.
- **`record TokenDisplayInfo`** — the merged, display-ready struct the engine hands the GUI:
  - `string DisplayId` — the **identity key** for hashing + the chip's stable look. For a `RuleGrant`-payload token this is the *granted rule name* (so "gains Regeneration" and "gains Stealth" get different chips); otherwise `TokenType.Id`.
  - `string Name` — human label (e.g. "Shaken", "+1 to Hit").
  - `string Description` — hover text from `Describe()` (e.g. "+1 to Hit rolls, this round").
  - `EValence Valence`
  - `ETokenProminence Prominence`
  - `ETokenColor? ColorOverride`, `ETokenShape? ShapeOverride` — null ⇒ app derives from `DisplayId` hash.
  - `int Count` — magnitude for the number badge.
  - `bool IsModelScoped` — placement hint (under unit name vs. over the model).
  - `string LifetimeText` — for hover (synthesized from `ClearTrigger`).
  - `UnitID? OwnerUnitID` — for hover ("placed by …"); app resolves friendly/enemy via team lookup.

### Token-definition catalog — the single source of truth for a token *type* (Option B)

A static **`TokenDefinitionCatalog`** (mirrors `CoreRuleCatalog`) keyed by `TokenType.Id` is the **one place** an engine-known token type is defined. It holds, together, both the **type-intrinsic gameplay defaults** and the **display metadata**:

- gameplay: default `ClearTrigger` (for fixed-lifetime types), stacking rule / cap, singleton-ness;
- display: `Name`, `Description` template, `Valence` (when fixed by type), `Prominence`, optional `ColorOverride`/`ShapeOverride`.

**Creation goes through the catalog.** The scattered `new Token(TokenType.X, …)` sites for the fixed singletons (Shaken's `ManualOnly` is currently typed out in *both* `MoraleUtilities` and `TransportUtilities`) are refactored to construct via a single factory — `catalog.Create(type, count = 1, clearOverride = null, payload = null, owner = null)` — so each fixed attribute lives in exactly one place. Behavior-preserving; retires the duplicated-trigger hazard.

**Not every attribute can be centralized — by nature, not oversight.** The test: *would two tokens of this type, created in two different situations, necessarily share this field?* Yes → catalog (type-intrinsic). Could differ → stays at the (single) creation site (instance-contextual):
- **`Count`** — the live value is state (SpellTokens is 3 now, 5 next). The *cap* is type-intrinsic and may live in the catalog; the value can't.
- **`Payload`** — the per-use parameterization (`RuleGrant("Regeneration")` vs `RuleGrant("Stealth")`; `StatModifier(+1)` vs `(−1)`). Catalog pins the schema, never the value.
- **`OwnerUnitID`** — a runtime link to a specific live unit (which transport carries this `EmbarkedIn`); can't exist until creation.
- **`ClearTrigger` for the generic carriers** — `RuleGrant`/roll-modifier/`AbilityUsed:*` lifetimes are contributed by the *granting effect*, not the type: `AbilityUsed:X` is Activation/Round/Manual per the ability's cost (`RuleEvaluator.EmitCostOps`); a `HitRollModifier` is `RoundEnd` vs `AttackEnd` per the spell's `ELifetime` (`Effect.ClearTriggerFor`). Fixing these per-type would need one token type per (rule × lifetime × delta) — a combinatorial explosion that defeats the data-driven design. These already have a *single decider* each (the two mapping functions), so there's no literal to duplicate and no divergence risk.

This is the **same type-vs-instance boundary** used for display: valence for carriers is *derived from payload* (the catalog can't know +1 vs −1), while valence for Shaken is a fixed catalog entry. Same line, drawn the same way, for gameplay and display.

Unknown/unregistered token types fall through to safe defaults (Neutral / Normal / visible). Data-driven custom token *types* (#087) will register here later; out of scope for Step 1.

Builtin entries:

| `TokenType.Id` | Valence | Prominence | Clear-trigger | Overrides |
|---|---|---|---|---|
| `Shaken` | Negative | FirstClass | ManualOnly | — |
| `Fatigued` | Negative | FirstClass | RoundEnd | — |
| `SpellTokens` | Positive | FirstClass | per grant (cap 6) | Color = Blue |
| `HitRollModifier` | from sign(Δ) | Normal | per grant | — |
| `SaveRollModifier` | from sign(Δ) | Normal | per grant | — |
| `MoraleRollModifier` | from sign(Δ) | Normal | per grant | — |
| `RuleGrant` | from granted rule | Normal | per grant | — |
| `ArrivedFromReserve` | Neutral | Invisible | RoundEnd | — |
| `EmbarkedIn` | Neutral | Invisible | ManualOnly | — |
| `AbilityUsed:*` (prefix) | Neutral | Invisible | per cost | — |

### Resolution chains (engine helpers — keep all game logic out of the GUI)

**Valence** — `ResolveValence(Token, ruleRegistry)`:
1. `Payload is StatModifier sm` → `sign(sm.Delta)` (`>0` Positive, `<0` Negative, `0` Neutral).
2. `Payload is RuleGrant rg` → `ruleRegistry[rg.RuleName]?.Valence ?? Neutral`.
3. else → `TokenDefinitionCatalog[Type.Id]?.Valence ?? Neutral` (`AbilityUsed:*` matched by prefix).

**Prominence/visibility** — registry lookup by `Type.Id` (prefix for `AbilityUsed:`), default `Normal`. App's dev "show all" toggle overrides `Invisible` at draw time only.

**DisplayId (hash key + identity)** — `RuleGrant` ⇒ `payload.RuleName`; else `Type.Id`. This is what makes same-type carriers (different granted rules) look different.

**Color / Shape** — *app-side* (Step 2): override enum if present, else hash(`DisplayId`) → index into the valence band's curated palette (color) / the full shape set (shape, valence-independent). Engine only supplies the override + valence + DisplayId.

### `Describe()` synthesis

Pure helpers (precedent: `SpellText`, `SightRuleLabel`) that turn a `Token` into human strings, consumed verbatim by the GUI:
- `string TokenDisplay.DescribeName(Token, ruleRegistry)` → short label.
- `string TokenDisplay.DescribeDetail(Token, ruleRegistry)` → full hover line (e.g. `HitRollModifier + StatModifier(+1) + RoundEnd` → "+1 to Hit, this round").
- `string TokenDisplay.DescribeLifetime(TokenClearTrigger)` → e.g. "this round" / "until removed" / "next time it applies".
- `TokenDisplayInfo TokenDisplay.Resolve(Token, ruleRegistry, TokenDefinitionCatalog)` → the merged struct (single entry point the GUI calls).

### Engine/app seam (what Step 2 consumes)

App calls `TokenDisplay.Resolve(token, …)` per token on a unit/model and renders the returned `TokenDisplayInfo`. The app owns: `ETokenColor`/`ETokenShape` → pixels, the curated palette, the `DisplayId` hash, layout/tiers/sort/badges, and the tooltip. The engine owns everything semantic and is unit-tested independently.

---

## Decisions (locked with user, 2026-06-30)

- **Catalog is the single source of truth for token types (Option B).** Before this, there was no token *definition* at all — `TokenType` is just id constants and each token's real attributes (clear-trigger especially) were hardcoded at scattered `new Token(...)` sites (Shaken's `ManualOnly` duplicated across two files). A display-only registry would have *added* a third source. So the catalog holds gameplay defaults + display together and fixed-singleton creation routes through it. Carve-out: genuinely instance-contextual attributes (count value, payload, owner, carrier clear-triggers) stay at their single creation/derivation site — see the catalog section's type-vs-instance test.
- **Bearer-relative valence.** Color reads good/bad for the unit the token is *on*, not for the viewer. A positive-looking icon on an enemy naturally reads as bad-for-you — and this is the *cheap* option: valence becomes a static property of the token/grant, computed once, no per-viewer logic. No new field on the `Token` instance is needed — every instance-varying case (modifier sign, granted-rule identity) is derivable from the payload the token already carries.
- **Neutral = muted hue, not gray.** Gray-on-gray is the worst discrimination and Neutral is the *default* valence, so the common case must stay distinct. Neutral uses the full hue wheel at low saturation; Positive = vivid cool (blue/green/purple); Negative = vivid warm (red/orange/yellow/pink).
- **Probabilistic distinctness, no local tie-breaking.** Hash into a wide curated combo space so co-resident collisions are rare; do **not** disambiguate per-unit (it would break cross-game consistency). Count badge + hover are the last-resort disambiguators. Hash the **stable id** (`TokenType.Id` / granted rule name), not a renamable display name.
- **Two display tiers, not size-mixing.** First-class tokens (Shaken/Fatigued/SpellTokens) live in a prominent status tier with bespoke icons; generic hash chips live in a separate row. "First-class" implies bespoke iconography, not merely a bigger square.
- **`Describe()` is Step 1.** The hover-text synthesis is logic, lives in the engine, returns strings the GUI prints.
- **Overrides are enum-valued, not RGB.** Keeps pixels out of the submodule; "spell tokens are blue" is authored *intent* (`ETokenColor.Blue`), the app maps it.
- **Submodule changes are authorized** for this item (user OK'd putting display metadata in the engine).

## Deferred / Step 2 (follow-up item, not yet numbered)

- GUI rendering: chip row + first-class status tier, count badges, valence sort (sort key `(valenceRank, hashOfId)` for frame/run stability), hover tooltip.
- Curated palette design per valence band (CVD-survivable; include cool blue/purple in Positive and warm orange/yellow in Negative so it doesn't rely on red/green alone).
- First-class **bespoke icons** (Shaken/Fatigued/SpellTokens) — asset dependency; placeholder with a simple glyph now, drop in art later (#053 pattern).
- Dev "show all tokens" toggle (reveals Invisible tokens for rules-engine debugging).
- **Model-token clutter** (N models each carrying the same model-scoped token = N identical chips) — note, defer; may aggregate later.

## Notes

- 2026-06-30: Spec updated to Option B (catalog = single source of truth for token types, with the type-vs-instance carve-out) per user sign-off. Proceeding to implement Step 1; submodule desync to be resolved first (branch + ff to pinned `7bac353`, leave `README-WIP.md`).
- 2026-06-30: Item opened and branch `151-token-display-metadata` created (superproject). Wrote the Step 1 spec after a design discussion (token inventory comb-through → category axes → valence feasibility). Submodule desynced from the superproject pin; left untouched pending the engine-code phase. No code yet; awaiting sign-off (since granted).

## Outcome

(TBD)
