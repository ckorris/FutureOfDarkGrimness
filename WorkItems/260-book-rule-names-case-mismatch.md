# 260 — Five bundled-book rule names differ from the catalog only by case, so they never resolve

**Status**: todo (found, not fixed — needs a fork decision, and the likely fix is engine-side)
**Related**: #259 (surfaced it — the rule-tooltip glossary reads these as unknown), #241 (its import modal
already reports the same rules as "not enforced by the engine")

## Goal

A rule the bundled books put on a unit should fire in play. Done = these five names resolve at army load
(or are deliberately, visibly declared unimplemented), with a lint test that fails if a bundled book ever
again references a name that differs from a catalog/supplement name only by case.

## Evidence (2026-07-23)

`CoreRuleCatalog` spells these in sentence case; every bundled `.fdgbook` spells them in title case. The
engine's `RuleResolver` is case-sensitive (documented on `CoreRuleCatalog.UnitAura`), and
`ArmyListRuleResolution` skips a name it cannot resolve, so each of these is inert in play today:

| Book spelling | Catalog spelling | Book references |
|---|---|---|
| `Bane in Melee` | `Bane in melee` | 8 |
| `Rending in Melee` | `Rending in melee` | 3 |
| `Shred when Shooting` | `Shred when shooting` | 3 |
| `Unstoppable in Melee` | `Unstoppable in melee` | 2 |
| `Shred in Melee` | `Shred in melee` | 1 |

Neither `GdfRuleSupplement.json` nor any book's own `ruleDefinitions` defines the title-case spelling, and
nothing normalizes case at import (`OprBookImporter`) or at load — checked both.

Not to be confused with `Rending when shooting`, which the supplement defines in lower case and which does
resolve.

## Fork (needs sign-off before building)

1. **Rename the catalog rules to title case** — engine change (submodule; needs explicit authorization),
   touches integration tests that reference the names, and any saved army carrying the old spelling would
   then stop resolving.
2. **Add title-case aliases to the supplement** — app-side data only, no engine change, but leaves two
   spellings live forever.
3. **Case-insensitive resolution in `RuleResolver`** — fixes this whole class of drift at once; the widest
   blast radius, and the case-sensitivity looks deliberate.

Whichever wins, add the lint test (app-side, alongside `RuleSupplementLintTests`) so it cannot recur.

## Outcome

_(open)_
