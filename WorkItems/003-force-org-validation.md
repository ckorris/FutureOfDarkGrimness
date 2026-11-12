# 003 — Force organization validation (optional rule: hero/unit/copy/cost caps)

**Status**: implemented 2026-06-25 — engine suite 779/0, full build clean, headless smoke clean (4/5 runs; the 1 failure was the known intermittent AI-movement cohesion flake, unrelated). **Awaiting GUI hand-verification** in the Army Builder.
**Related**: #006 (Hero rule — Hero detection shared), #059 (army-file rule entries)
**Branch** (both repos): `003-force-org-validation` — submodule + superproject branched from master.

## Goal
Surface army-composition problems against the GDF force-organization caps as **advisory warnings only** — visible in the Army Builder, never blocking save or launch (explicit user requirement 2026-06-25). "Done" = an over-cap army shows a clear warning per violated cap but can still be saved and played.

## Notes
- **2026-06-25 — design decisions (user sign-off).** (1) **Caps + thresholds (all four, GDF defaults):** Cost = `TotalPoints > PointsLimit`; Hero = `#heroes > floor(PointsLimit / 500)` (1 per 500 pts); Copy = any same-named unit appears `> 3` times; Unit = an army with units but **0 non-Hero units**. (2) **Architecture = engine validator + builder display:** a pure `FDG.SaveLoad.ForceOrgValidator` returning warning strings (unit-testable, reusable by the lobby later), rendered by the Army Builder.
- **2026-06-25 — implemented.**
  - **Engine:** `ForceOrgValidator.Validate(ArmyListFile) → IReadOnlyList<string>` — one warning per exceeded cap, in display order; empty = within all caps; never throws/blocks. Constants `POINTS_PER_HERO = 500`, `MAX_COPIES_PER_UNIT = 3`. Public `IsHero(UnitFileEntry)` is the single Hero detector (recurses through `SpecialRuleEntry_Alias`). Empty army → no warnings (it's unfinished, not mis-composed); blank-named units are skipped in the copy check so half-built entries don't read as duplicates.
  - **App:** `ArmyBuilderScreen.DrawArmyHeader` calls `ForceOrgValidator.Validate(_army)` after the points total and renders each warning via the existing `Warn()` helper (amber `! …`). `UnitHasHero` now delegates to `ForceOrgValidator.IsHero` — deleted the duplicate `RuleEntryIsHero` so there's one Hero definition across engine + UI.
  - **Tests:** `ForceOrgValidatorTests` ×11 — clean army, empty army, over-points (+ at-exactly-limit boundary), too-many-heroes (+ at-allowance), too-many-copies (+ blank-named not counted), all-heroes → no-non-hero-units, multiple-violations-all-reported, and `IsHero` through an alias. Engine suite 768→779/0.

## Decisions
- **Advisory, not a gate.** No code path blocks on the result — it's pure presentation. This matches the user's "warn, don't block" requirement and keeps the rule genuinely *optional* without needing a settings toggle.
- **Engine-side pure validator over app-only.** Keeps the caps with the army data model, gets engine-suite coverage, and makes lobby reuse a cheap follow-up (the lobby already reads `ArmyListFile.TotalPoints`). Cost: a small additive submodule change.
- **Hero detection unified.** Rather than duplicate the alias-recursing "is Hero" switch in both the engine and the builder, the builder now delegates to `ForceOrgValidator.IsHero` — one source of truth (same reasoning as #022's melee cylinder).
- **"Unit" cap = ≥1 non-Hero unit**, guarded to skip the empty army. Chosen from the three readings the user was offered; flags the "all-heroes, no troops" composition without nagging a freshly-opened empty list.

## Outcome
_(written on close — pending GUI hand-verification)_

## Deferred / follow-ups
- **Lobby display** of the same warnings (the engine validator makes this cheap) — not wired in this slice to keep it to one vertical slice.
- Per-unit point-share caps (e.g. "no single unit > 1/3 of the army") were not in the agreed set; open a new item if wanted.
