# 103 — Caster cast assist (friendly +1 / enemy −1)

**Status**: done 2026-06-30 (awaiting GUI hand-verification)
**Related**: #033 (Caster framework — spun off from its last tracked slice), #034 (spell content)

> **Renumbered 2026-06-22.** Opened as #094, but origin/master had already assigned #094 to *group-move
> coherency repair* (and a separate parallel item to #095). Per the never-reuse rule this item yields and
> takes #103. Earlier commit messages / the pushed branch reference "#094" — they predate the renumber.

## Goal
When a Caster declares a spell + target(s), other friendly Caster units within 18" may spend their own
spell tokens to modify the cast roll by ±1 per token, before it resolves. "Done" = casting a spell offers
each eligible friendly Caster's controller the chance to contribute, those tokens are spent, and the net
modifier shifts the 4+ result — proven by an integration test and visible in a real game.

This was the last tracked slice inside #033; broken out to its own item (2026-06-21, at the user's
request) because it's an optional, networking-sensitive, multi-unit decision loop that's independent of
the (now complete) core casting framework.

## What exists to build on
- `CastSpellStage` (`.../MainUnitActionStage/CastSpellStage/CastSpellStage.cs`) resolves the cast with a
  single `GameContext.DiceRoller.RollDecisive()` 4+ check. There's an explicit insertion point comment
  there: *"±1 friendly-Caster assist — a #033 follow-up — would adjust here."* The assist slots in
  **after target selection, before the roll**.
- `EHookID.Casting_OnSpellAssistOffered` is **defined but unwired** (reserved in the Caster hook block) —
  this is its consumer.
- Token economy is done: friendly Casters carry `TokenType.SpellTokens`; spending is `RemoveTokens`.
- Eligibility helpers exist: `SpellTargeting` already finds units by affinity + range; the same
  team/affinity + distance machinery (`DistanceUtilities`, team lookup) can find friendly Casters in 18".
- Player decisions ride the existing request/resolver infra (e.g. a `YesNoRequest` per assister, or a
  count request); CLI + GUI + AI resolvers already exist for those shapes.

## Decisions (resolved 2026-06-30, with the user)
- **Scope: friendly +1 AND enemy −1.** Chose the fuller OPR rule over friendly-only. Enemy Casters within
  18" may spend tokens for −1 each; the enemy player is prompted (open-information over the network — the
  cast declaration is visible to the opponent — accepted as intended for the fuller rule).
- **Prompt shape: count request (0..N).** One `StringSelectionRequest` per eligible Caster ("Spend no
  tokens / 1 token (±1) / 2 tokens (±2) / …" up to their available tokens). The option's index in the list
  IS the token count (index 0 = decline), so the reply maps back with no parsing.
- **Roll application: threshold shift.** `RollDecisive().AtOrAbove(Clamp(4 − netModifier, 1, 6))` — a single
  decisive comparison, no expected-value averaging, so it stays correct under the probabilistic roller.
  Clamp keeps a natural 6 succeeding / a natural 1 failing rather than asking for an impossible face.
- **Cost timing: assist tokens spent regardless of pass/fail**, like the cast cost.
- **Prompt order: friendly helpers first, then enemy hinderers** (the casting side commits support, the
  opponent responds), each group in store order for determinism. Single pass per Caster (no alternating
  bidding war) — a faithful simplification.
- **Mechanism: direct stage logic, NOT the reserved `Casting_OnSpellAssistOffered` hook.** The assist is core
  behaviour available to every Caster (identified by `SpellTargeting.IsCaster`) with no per-rule variation to
  dispatch on, so firing a hook no rule listens to would add indirection with no benefit. The hook enum stays
  reserved. (Deviates from this file's original "via the defined-but-unwired hook" framing — recorded here so
  the change isn't silent.)
- **AI + CLI-EOF default to decline.** `AiStringSelectionResolver` matches the `DECLINE_ASSIST_CHOICE`
  sentinel and spends nothing (conservative, matching the pre-attack/Ambush skips); the CLI resolver's
  EOF default is option 0, which is the decline sentinel — so piped/headless caster games spend nothing.

## Design forks to resolve before building
- **±1 direction / who may assist.** OPR allows Caster units within 18" to spend tokens for +1 (help) or
  −1 (hinder) each. Decide: friendly-only +1 (simplest, matches the #033 one-liner) vs. also letting enemy
  Casters spend tokens for −1 (fuller rule, more decision loops, opens an open-information question over
  the network). Recommend starting friendly-only +1 and recording the enemy −1 as a further follow-up.
- **Decision shape.** Per-assister: a YesNo("spend a token to add +1?") loop, or a single "how many
  tokens?" count request per assister. Count request is fewer prompts; YesNo reuses the simplest resolver.
- **How the modifier applies to a decisive roll.** The cast is `RollDecisive()` (4+). Apply the net
  assist as a threshold shift (need `4 - assist`+) or as a post-roll result adjustment — must stay correct
  under the probabilistic roller (don't int-lock; see [[project_dice_probabilistic_invariant]]).
- **Ordering vs. the cast cost.** The caster still spends the spell's threshold to attempt; assisters
  spend *additional* tokens. Confirm assist tokens are spent regardless of pass/fail (like the cast cost).

## Deferred (carry forward, don't silently cut)
- ~~Enemy Casters opposing with −1~~ — **built** (shipped together with friendly +1).
- **Alternating token bidding** (each side reacting to the other's spend, repeatedly): not built — a single
  count prompt per Caster (friendly then enemy). A fuller reactive loop is a future refinement.
- **Smarter AI assist policy**: the AI always declines. A real policy (spend to secure a high-value cast /
  hinder an enemy's) is a future refinement.

## Notes
- 2026-07-01: **Hand-verify feedback + GUI polish** (branch `103-caster-assist`, both repos). After the user
  hand-verified round 1 (all prompts appeared, net math correct) three gaps surfaced; addressed:
  - **Cast roll was invisible.** The cast log now spells out the die + the assist math, e.g. `Sorcerer cast
    Bless — rolled 4, needed 3+ (base 4+, net +1 assist); spent 1 token` (+ each assist prompt shows the
    assister's token count). Engine `a64bc27`. A *visual* die (tumbling dice on canvas) stays out of scope —
    that's #056 (presentation beat stream).
  - **Click-to-target on the canvas** (like shooting). Made every `SelectionRequest<UnitData>` and the #100
    `CancellableSelectionRequest<UnitData>` canvas-clickable via the shared `ICanvasInteractionHandler` seam +
    `TableHitTester`: new `GuiUnitSelectionResolver` / `GuiCancellableUnitSelectionResolver` ring the valid
    units and select on click (dialog stays as fallback + Back). Covers spell targets, melee defender, and
    pre-attack targeting (the user chose "all unit picks"). App-side only.
  - **Assist highlight + line + token count.** Replaced the generic `StringSelectionRequest` assist prompt
    with a dedicated `CastAssistRequest` (carries both units + friendly flag + tokens) so the GUI can draw:
    new `GuiCastAssistResolver` rings the assister and draws a line to the caster — **blue** for a friendly
    +1, **orange** for an enemy −1 — labels the assister's token count, and offers a 0..N picker. CLI
    (`CastAssistResolver`, EOF→0) + AI (`AiCastAssistResolver`, always 0) resolvers added; the old
    `DECLINE_ASSIST_CHOICE` sentinel + AiStringSelection branch removed. Engine `0f9f8f8`.
  - Test aid `CasterCovenTest.fdgarmy` (3 standalone Casters + cheap spells) added earlier for verification.
  - Full build clean, engine suite 950/0, headless smoke exit 0. **Awaiting GUI hand-verification** of the
    polish (canvas click-to-select + blue/orange assist viz).
- 2026-06-30: **Built** on branch `103-caster-assist` (both repos). Assist window inserted in
  `CastSpellStage.Enter` after the caster commits the cast cost and before the roll: `CollectCastAssist`
  finds eligible Casters (`FindEligibleAssisters` — living, on-battlefield, ≥1 SpellToken, within
  `GameWideConstants.CASTER_ASSIST_RANGE_INCHES` (18") unit base-to-base 3D, friendly-first), prompts each
  via `AskAssistCount` (count-style `StringSelectionRequest`), spends their tokens, and sums a net modifier
  applied as a threshold shift. `IsCaster` moved from `ChooseActionStage` (private) to `SpellTargeting`
  (public, single source). `AiStringSelectionResolver` declines via the `DECLINE_ASSIST_CHOICE` sentinel.
  No app-side code change — the existing GUI/CLI `StringSelectionRequest` resolvers handle the prompt. See
  Decisions for the resolved forks. Engine commit `4ea14c3`; engine suite 950/0 (+3
  `CasterRuleIntegrationTests`: friendly rescue, enemy spoil, out-of-range gate), app build clean, headless
  smoke exit 0.
- 2026-06-21: Item opened, spun off from #033's deferred "±1 assist" slice. #033's framework (slices 0–4
  + spell-authoring UI) is on branch `033-caster`.

## Outcome
Built end-to-end (engine only): friendly Casters within 18" add +1 per token and enemy Casters subtract 1
per token to a cast roll, before it resolves; the net modifier shifts the 4+ threshold (clamped [1,6]) and
assist tokens are spent regardless of the outcome. Proven by three integration tests and green across the
suite/build/headless smoke. **Awaiting GUI hand-verification** of a real two-Caster game (friendly assist
dialog + networked enemy-hinder open-information flow).
