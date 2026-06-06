# 042 — Special rules architecture

**Status**: in-progress (Phase 7 — dispatch complete: every passive rule, all 7 activated abilities (7c), and cross-unit token cleanup (7g) are green through `RuleEvaluator` + `TokenClearService`. **Suite 241/0 — the full Phase 7 RED baseline is green.** Behavior-level execution (Phase 8), the remaining engine-primitive sub-phases (7h: TriggeredMove/Reactivate/mid-move attack), and the JSON loader remain)
**Related**: #026, #027, #028, #029, #030, #031, #032, #033, #034 (all depend on this)

## Goal

A data-driven architecture for unit, weapon, and spell special rules — one that:

- Lets the engine emit named hook events without knowing about specific rules.
- Lets army-specific rules be authored as data records (JSON/YAML), not C# code, so balance patches and new armies don't require a rebuild.
- Lets rules reference core rules and override their parameters (e.g. "Infiltrate counts as Ambush but with min-distance 3"").
- Lets rules modify, suppress, or augment other rules at resolution time (e.g. Rending's existing pattern of removing Regeneration from the save-roll sink).
- Lets per-entity state (tokens, counters, status conditions) be the unified place rule-related state lives, so save/load is trivially serializable.
- Keeps a small Lua escape hatch on the table for future weird rules but does **not** ship one initially — the data vocabulary should cover the corpus.

## Design summary (2026-05-11)

### Approach: Plan B — data-driven `Condition × Effect` records over a named hook surface

- The engine emits **hook events** at well-defined points (PreHitRoll, PostHitRoll, PreSaveRoll, OnDeploy, OnRoundStart, OnEndOfActivation, OnUnitDestroyed, OnWoundIgnored, OnMoveThroughEnemy, OnActivationSelect, …). Initial estimate ~20–25 hooks across the full ruleset.
- A rule is a **data record** with one or more `(hook, condition, effect)` entries (plus optional cost/selector for activated abilities).
- Three trigger flavors: **passive** (always evaluates at hook), **activated** (player-chosen, with cost gate and target selector), **random** (engine rolls a die and applies one of N branches).
- **Core rules** (Tough, Regeneration, Ambush, Scout, Caster, Transport, Aircraft, Hero, the AP/Blast/Deadly family) live in C# as first-class concepts. **Army-specific rules** are data records that reference and parameterize core rules where possible. Renames are `kind: <CoreRule>` with a `displayName`.
- **Effect lifetime scopes**: this-attack, this-activation, this-round, next-trigger, permanent (aura), until-end-of-game.

### Tokens as the unifying state primitive

Per-entity tokens (model / unit / cross-unit-with-owner) hold all rule-related state. This collapses many mechanics onto one container:

- Cost gates: once-per-game = token granted at unit creation; once-per-round = token granted at OnRoundStart; once-per-activation = granted at OnActivationStart, cleared at OnActivationEnd.
- Spell tokens (Caster X) — replenished at OnRoundStart, capped at 6.
- Stacking counters (Piercing Frenzy-style "destroyed an enemy unit → +1 marker", Regenerative Strength model-scoped equivalent) — tokens with a count, read by other effects as a numeric modifier.
- Status conditions: Shaken, Fatigued (per-round), spell "next time it would apply" buffs, target tags (Unstoppable Mark) — typed tokens with a `clearOn` trigger.
- Effect-scope expiration: "this attack only", "until end of activation", "next time" all become tokens with a defined clear hook.

Token scopes:
- **Model-scoped** (Regenerative Strength markers)
- **Unit-scoped** (most cost gates, Shaken, Piercing Frenzy)
- **Cross-unit** — token lives on the *target* but has an `owner` field so cleanup tracks ownership (Unstoppable Mark, spell-applied buffs on picked targets)

Army/global counters fall out as derived counts over unit-scoped tokens.

### Engine primitives data can invoke but not define

Some rules need genuinely new engine capabilities that the data layer composes but does not implement:

- `ReactivateUnit(unit)` — for Martial Prowess-style "activate again this round" rules.
- Mid-movement attack — for Strafing-style "during a move, attack a unit you're passing through".
- Post-deployment reposition — for Vanguard, Re-Deployment, Scout (siblings).
- Parameter override of another rule on the same entity — for Harassing Boost-style "if condition, this unit's Harassing has distance 6 instead of 3".

Each is a one-time engine extension that future rules reuse.

### Vocabulary estimate (will grow with corpus)

- **Hooks** ~20–25
- **Conditions** ~6–10 (unit-has-rule, target-has-rule, action-type, unmodified-roll-equals, distance-comparison, target-stat-comparison, target-composition)
- **Effects** ~12–15 (RollModifier, Reroll, AddExtraHit, AddExtraWound, MovementBonus, IgnoreRule, AddRule, Aura, DealHits, Heal, StatModifier, TagUnit, ConditionalRoll, TriggeredMove)
- **Cost types** ~5 (OncePerActivation, OncePerRound, OncePerGame, SpellTokens(N), ConsumesToken)
- **Lifetimes** ~6

### Sample feasibility checks

Sampled GF–Battle Brothers and GF–Dark Elf Raiders army books. Every rule and spell on both pages classifies cleanly. New patterns surfaced (game-event hooks, stacking counters, random branches, mid-move attacks, re-activation) extend the vocabulary but don't break the model. No rule required a Lua escape hatch.

Recommended before locking the design: skim 2–3 more armies in different flavors (vehicle-heavy, psychic/horror, swarm) and re-run the classification. If the "needs new engine primitive" column stays in single digits across 4–5 armies, the architecture carries the whole game.

## Decisions

- **2026-05-11**: Plan B chosen over pure C# (option A) and pure Lua (option C). Reasoning: rule corpus is highly compositional; recompile-per-army is the main pain Lua would solve; Lua's downsides (debugging, networking sync, sandboxing, save/load of closures) outweigh its flexibility given the corpus we've seen.
- **2026-05-11**: Lua escape hatch deferred, not abandoned. Add later if the data vocabulary is forced past its breaking point. Plan B doesn't preclude it.
- **2026-05-11**: Core rules live in C#, army rules in data. Boundary: a rule is "core" if the engine itself references it by name (Regeneration handler, Tough wound-priority, Caster token mechanics). Everything else is data.
- **2026-05-11**: Tokens are the single state container for rule-related per-entity state. No ad-hoc state fields scattered across the engine for rule purposes. Existing engine status (Shaken, Fatigued) should migrate onto the token system.
- **2026-05-11**: Networking — initially framed as "deterministic dual-computation"; **corrected 2026-05-24**: the engine is host-authoritative (only host runs rules; client receives `StageTaskRequestMessage` and only resolves player decisions). So rule registry hashing at lobby-ready is for **shared vocabulary** (client can interpret + render anything the host sends), not for deterministic dual-computation. Same mechanism, different motivation. Asymmetric failure modes: client missing a rule = degraded render; host missing a rule = silently broken gameplay. Reject mismatches; do not auto-negotiate; do not ship registry over the wire. Army-list rule references should also validate against the host's registry at army upload with rule-name-specific errors (friendlier than a generic hash mismatch). Saves embed the host's rule hash and refuse to load against a mismatched registry.
- **2026-05-11**: Save/load implication — hook handlers must be **stateless**; all state lives in tokens, GameDataStore, and stage-machine snapshot. The hard part of save/load is the stage machine, not the rule system (which becomes trivially serializable under Plan B).

## Notes

- 2026-05-11: Initial design conversation complete. Two armies sampled and validated.
- 2026-05-11: Existing `ISpecialRule_Combat` / `ICombatEffect<TResult>` partial implementation has the right shape for combat hooks (Rending demonstrates the sink-mutation pattern). Should be salvageable, but the surface needs to grow to non-combat hooks and the rule definitions need to move from C# classes to data records.
- 2026-05-11: Effect-output model: **queue of typed `RuleOperation` records returned by effects, applied by the engine**. Not in-place mutation. Tests assert on the returned queue. Mutation tests couple to context internals; queue tests are structural and survive refactors.
- 2026-05-11: Hook surface and token system specs drafted below — see "Hook surface" and "Token system" sections.

## Hook surface (draft)

Each entry: hook ID, the moment it fires, and the context payload rules see at that moment. Effects emitted by rules are queued and applied between gather + downstream computation.

### Round-level

| Hook | Fires when | Context |
|---|---|---|
| `OnRoundStart` | Top of every round, before any activations | round number, active player list |
| `OnRoundEnd` | After ReconcileObjectives, before next round | round number |

### Activation-level

| Hook | Fires when | Context |
|---|---|---|
| `OnNextActivatorRequested` | Engine asks who activates next | candidate units, round number |
| `OnActivationStart` | Selected unit begins its activation | unit |
| `OnActionChoice` | ChooseAction is offering options | unit, candidate actions |
| `OnPreAttack` | Activation has chosen an attack action, before targets/weapons resolved | unit, action type |
| `OnEndOfActivation` | Activation completing, before stage transition | unit, wounds taken this activation |

### Movement

| Hook | Fires when | Context |
|---|---|---|
| `OnMoveActionDeclared` | Player has chosen Advance/Rush/Charge — used to compute the distance budget | unit, action type, base distance |
| `OnChargeDeclared` | Specific to Charge — fires alongside `OnMoveActionDeclared` | unit, target, base distance |
| `OnMoveThroughEnemy` | Path passes through (not just adjacent to) an enemy unit's footprint | unit, enemy unit, path point |
| `OnMoveThroughTerrain` | Path enters a terrain piece | unit, terrain piece, terrain type |
| `OnMoveResolved` | After unit has finished its move and its position is committed | unit, final path |

### Shooting

| Hook | Fires when | Context |
|---|---|---|
| `OnShootTargetsSelected` | After weapon groups + targets chosen, before any rolls | unit, weapon groups, targets |
| `OnPreHitRollCount` | Calculating how many hit rolls to make (attack count modifiers) | weapon, attacker, target, base attack count |
| `OnHitRollModifier` | Adjusting Q value / per-roll modifiers before rolling | weapon, attacker, target, modifier stack |
| `OnHitRollComplete` | Hits collected, before save flow (where Furious / Rending / Surge convert 6s) | weapon, attacker, target, hit dice |
| `OnPreSaveRollCount` | Determining how many save rolls and at what value | hits, AP, defender, weapon |
| `OnSaveRollModifier` | Adjusting Defense value / per-roll modifiers (Cover, Shielded) | defender, save needed, modifier stack |
| `OnSaveRollComplete` | After defense rolls, before applying wounds (Regeneration, Bane) | defender, failed saves |
| `OnPreApplyWound` | Each wound about to be assigned to a model | wound, defender, candidate models |
| `OnPostApplyWound` | Each wound just assigned (or absorbed) | wound, target model, was-killed flag |
| `OnUnitDestroyed` | A unit's last model died (attribution available) | destroyed unit, killer unit, hook origin (shoot/melee) |
| `OnPostShoot` | Shoot action complete | unit, target, summary |

### Melee

Reuses much of the Shooting surface — the same hooks fire with a `combatKind: Melee` flag in context, *except* where the rules genuinely differ:

| Hook | Fires when | Context |
|---|---|---|
| `OnChargeContact` | Charging unit completes its move into combat, before strikes (Furious, Thrust, Impact triggers) | attacker, defender, who-charged |
| `OnCounterTrigger` | Defender has Counter — they strike first | attacker, defender |
| `OnMeleeResolution` | Wounds tallied on both sides, before morale | attacker, defender, wounds-each |
| `OnPostMelee` | Melee fully resolved (after morale, consolidation) | both units, outcome |

### Morale

| Hook | Fires when | Context |
|---|---|---|
| `OnPreMoraleTest` | About to take a morale test, before rolling | unit, source (melee/wounds), modifier stack |
| `OnMoraleTestComplete` | Roll done, outcome about to be applied (Fearless reroll) | unit, raw result, source |
| `OnShakenApplied` | Unit becoming Shaken | unit |
| `OnShakenCleared` | Unit recovering from Shaken | unit, source (activation end / Battleborn) |

### Deployment

| Hook | Fires when | Context |
|---|---|---|
| `OnPreDeploymentSelect` | About to deploy — used to remove Ambush/Scout from selection pool | unit, deployment pool |
| `OnUnitDeployed` | A unit's deployment placement just committed | unit, position |
| `OnPostDeploymentComplete` | All non-Scout/Ambush/Re-Deploy units placed | all units |

### Casting

| Hook | Fires when | Context |
|---|---|---|
| `OnSpellCastAttempt` | Caster has declared spell + targets, before roll | caster, spell, tokens spent, targets |
| `OnSpellAssistOffered` | Friendly Caster units within 18" may contribute tokens | caster, spell, friendly casters |
| `OnSpellResolved` | Spell roll passed, applying effect | caster, spell, targets |

### Lifecycle / token bookkeeping

| Hook | Fires when | Context |
|---|---|---|
| `OnUnitCreated` | Unit first added to ITableState (army-setup time) | unit, army list source |
| `OnWoundIgnored` | A wound was absorbed by Regeneration / similar | unit, source rule, attacker |

### Open questions

- **Discriminator vs separate hooks**: Most shoot/melee hooks share a shape. Going with one hook + context flag means fewer hook IDs but more conditions check the flag. Going with two hooks duplicates the surface but lets data records target one phase cleanly. Lean toward **shared hook, context flag**, since most rules don't care which phase.
- Are `OnMoveResolved` and `OnPostShoot` redundant for Harassing's "after shooting or being in melee" trigger? Probably need a separate `OnTargetedInMelee` so the trigger fires when the unit *was attacked*, not when it attacked. Defer until we encounter the case.
- `OnNextActivatorRequested` is unusual — it's the round-level state machine asking rules a question rather than rules reacting to an event. Used only by Martial Prowess-style re-activation. Keep it; alternative is to model re-activation as a `OnRoundStart`-ish offer, which is uglier.

## Token system spec

### `Token`

```csharp
record Token(
    TokenType Type,
    int Count,                              // 1 for singletons; >1 for stacking markers
    TokenPayload? Payload,                  // tagged union; null for most
    UnitId? OwnerUnitId,                    // for cross-unit tokens; null = self-owned
    TokenClearTrigger ClearTrigger,
    HookId? CreatedAtHook                   // for debugging only
);
```

### `TokenType`

Identifier — interned string or enum. Engine-known types (small set the engine itself reads/writes): `Shaken`, `FatiguedThisRound`, `SpellTokens`. Everything else is defined by rule data; the engine doesn't know what it means, it just stores it and rules reference it by name.

### `TokenContainer`

Attached to each `IUnit` and `IModel`.

```csharp
interface ITokenContainer {
    void Add(Token token);                  // stacks if same Type + Owner
    bool Remove(TokenType type, int count = 1);
    bool Has(TokenType type);
    int  Count(TokenType type);
    IEnumerable<Token> All();
    IEnumerable<Token> WithOwner(UnitId ownerId);

    event Action<Token> OnTokenAdded;
    event Action<Token> OnTokenRemoved;
    event Action<Token> OnCountChanged;
}
```

### `TokenClearTrigger`

When a token auto-clears. Driven by the hook bus emitting the corresponding event.

| Trigger | Cleared at | Use case |
|---|---|---|
| `ManualOnly` | Never auto-clears | Permanent buffs, "next time" tokens that the consuming rule clears |
| `RoundEnd` | `OnRoundEnd` | Fatigue, per-round cost gates, per-round bookkeeping |
| `ActivationEnd` | `OnEndOfActivation` (of bearer's unit) | Versatile Attack/Reach effects, per-activation gates |
| `AttackEnd` | `OnPostShoot` / `OnPostMelee` | "This attack only" modifiers |
| `FirstTrigger` | Auto-decrement when the rule that reads it fires | Spell-applied "ignored next wound", "+1 to hit next time" |
| `UnitDestroyed` | When bearer's unit is removed | Cleanup; happens automatically via teardown |
| `OwnerDestroyed` | When the `OwnerUnitId` unit is destroyed | Cross-unit tokens (Unstoppable Mark cleanup when source dies) |
| `CustomHook(HookId)` | Named hook fires | Edge cases (e.g., next-round-only effects) |

### Token scopes

Tokens live on the entity they affect, not the entity that placed them:

- **Model-scoped** — on `IModel.Tokens`. Example: Regenerative Strength markers (one model accumulates them as *that model* ignores wounds).
- **Unit-scoped** — on `IUnit.Tokens`. Example: Piercing Frenzy markers, Shaken state, most cost gates.
- **Cross-unit** — token lives on the *target*'s container but its `OwnerUnitId` points at the *placer*. Example: Unstoppable Mark — placed by friendly unit A on enemy unit B, but the friendlies-get-Unstoppable effect reads from B's tokens. Cleanup: if A is destroyed, all tokens with `OwnerUnitId == A` are removed across the table. This invariant is the reason ownership is tracked at all.

Army/global counters fall out of unit-scoped tokens — "how many units used Martial Prowess this round" is `tableState.Units.Sum(u => u.Tokens.Count(MartialProwessUsedThisRound))`.

### Stacking semantics

- Same `TokenType` + same `OwnerUnitId` on the same container ⇒ increments `Count`.
- Different owner ⇒ separate token entry (so multiple players' Unstoppable Marks on the same enemy don't merge).
- Singletons (Shaken, FatiguedThisRound) cap at `Count = 1` enforced by the engine-known type list.

### Payload

For tokens that need to carry data beyond a count:

```csharp
abstract record TokenPayload;
record SpellEffectPayload(SpellRuleRef AppliedRule) : TokenPayload;
record IgnoreNextWoundPayload(int OnRollValue, IgnoreKind Kind) : TokenPayload;
// ...one record per payload-carrying token type
```

Tagged union, all fields immutable, trivially serializable. Tokens without payload set `Payload = null`.

### Engine integration

- `IUnit` and `IModel` gain a `Tokens` property of type `ITokenContainer`.
- A `TokenClearService` subscribes to the hook bus and walks containers at relevant hooks to clear tokens whose `ClearTrigger` matches.
- Conditions (`TokenCondition.Has`, `TokenCondition.CountGTE`) and effects (`GrantToken`, `ConsumeToken`, `ClearTokens`) live in the standard Condition/Effect vocabulary — tokens are not a special API, they're one of the things rules read and write.

### Save/load implication

Pure data — `List<Token>` per entity, fully serializable. No callbacks, no closures, no engine references. Restoring a game restores tokens verbatim, and the hook bus reattaches its `TokenClearService` against the restored state.

## Phase 1 & 2 implementation notes (2026-05-24)

Foundation data types and the token system are committed. The shipped implementation diverges from the original sketches in a handful of deliberate ways — recorded here so future sessions don't re-derive the rationale.

### Naming + folder conventions

- All foundation enums prefixed `E` (`EHookID`, `ELifetime`, `ETargetAffinity`) — project convention.
- Namespaces: `FDG.Rules.Foundation`, `FDG.Rules.Tokens`. Phase 3 folder renamed `Model/` → **`Definitions/`** to avoid collision with `IModel`.
- Engine-known token IDs exposed as `public const string` constants on `TokenType` so they're usable in attributes, switch cases, and JSON, alongside the typed `static readonly TokenType` instances.
- `EHookID` uses explicit numeric values with ~10-spare gaps per phase group; phase order is Round → Deployment → Lifecycle → Activation → Movement → Shooting → Melee → Morale → Casting.

### TokenType is identity-only; no singleton metadata

- `TokenType` is `readonly record struct (string Id)` — just an identifier wrapper, auto-generated equality on `Id`. No `IsSingleton` field.
- Singleton enforcement is **not** the container's concern (see TokenContainer note below). Considered putting `IsSingleton` on `TokenType` via a custom `Equals`-throws-on-mismatch trick to preserve JSON authorability without a registry, but ultimately dropped — the container doesn't need to know type metadata, and pushing the policy up to the effect-dispatch layer (Phase 7d) is cleaner than threading flags through the type.

### TokenContainer is owner-agnostic for removal; does NOT enforce singletons

- `RemoveTokens(type, count)` iterates across **all** matching entries regardless of `OwnerUnitID`, draining in insertion order. The "filter by owner" semantics deliberately *aren't* in the container — callers (cost gates, effect dispatch) handle owner-scoping at the layer above.
- Singleton enforcement deferred to the effect-dispatch layer (Phase 7d). The container stays a pure data structure with no semantic opinions about its contents. Failure mode is cosmetic (Shaken could stack to Count=2 if effects double-add); `HasToken` still works, clear-on-event still removes whole entries.

### Three-event semantic on `ITokenContainer`

- `OnTokenAdded` — fires when a `(Type, Owner)` pair newly appears.
- `OnTokenCountChanged` — fires when an existing entry's count changes without crossing the zero boundary (stacking on Add, partial Remove).
- `OnTokenRemoved` — fires when an entry's count reaches zero and the entry is deleted.
- Each mutation fires at most one of the three. No-op mutations (non-positive counts, missing types) fire nothing.
- Events are `[JsonIgnore]` and don't survive save/load. Observers re-attach on rehydration; **no gameplay logic** is allowed to subscribe — rules go through the hook bus.

### `Token` is a positional record with `ClearTrigger` required and non-nullable

- Final signature: `Token(TokenType Type, int Count, TokenClearTrigger ClearTrigger, TokenPayload? Payload = null, UnitID? OwnerUnitID = null, EHookID? CreatedAtHook = null)`.
- `ClearTrigger` is required to force every token to declare its lifecycle explicitly (use `TokenClearTrigger.ManualOnly` for permanent tokens).
- Stacking semantics: same `(Type, OwnerUnitID)` pair stacks `Count`; the incoming token's `Payload`/`ClearTrigger`/`CreatedAtHook` are discarded in favor of the existing entry's. Correct for "increment this count" — would need rethinking if rules ever want to merge two distinct effect-instances of the same type.

### Unit / model integration

- `UnitID` added at `GameObjects/Core/UnitID.cs`, mirroring `PlayerID` exactly. Generated in all `UnitData` constructors; `[JsonConstructor]` accepts optional `UnitID? id = null` so deserialization preserves saved IDs.
- `IUnit.Tokens` and `IModel.Tokens` added. `UnitData` and `ModelData` have `[JsonProperty] private TokenContainer _tokens = new()` backing fields plus `[JsonIgnore] public ITokenContainer Tokens => _tokens` projections.
- `TokenContainer` deserializes itself via its own `[JsonProperty] private List<Token> _tokens`.

### Test coverage

- `TokenContainerTests.cs` — 16 tests covering AddToken (new/stacking/different-owner/non-positive), RemoveTokens (partial/full/missing/over-request/across-owners/non-positive), and all queries.
- `TokenRoundTripTests.cs` — 3 tests asserting tokens (including cross-unit owner separation and `TokenClearTrigger` discriminated-union subtypes) survive the full GameDataStore JSON round-trip used by the network layer.
- `UnitIDTests.cs` — 4 tests covering UnitID assignment, uniqueness, JsonConstructor explicit-ID, and full round-trip.
- 175 tests total in suite, all green.

## Phase 3 implementation notes (2026-05-24)

Rule data shapes committed. All 13 files in `Rules/Definitions/`. Architecture deltas from the original sketch:

### Naming + structural conventions

- All enums use E-prefix (`EStatKind`, `EActionType`, `ERollKind`, plus `EHookID`/`ELifetime` from Foundation). Consistent with the established project style.
- `Definitions/` folder (not `Model/`) to avoid collision with `IModel`.
- Collections on `SpecialRuleDefinition` use `IReadOnlyList<T>`, not arrays, to prevent post-construction mutation of rule definitions.

### `IEntityRef` dropped — token operations split by target type

Original plan had a marker interface (`IEntityRef`) so a single `GrantTokenOp` could target either `IUnit` or `IModel`. Switched to two sealed subtypes — `GrantTokenToUnit(IUnit, Token)` and `GrantTokenToModel(IModel, Token)` (same pattern for `ConsumeTokensFrom...`) — because the type system enforces correctness with no marker interface and only two cases exist. `IEntityRef` is not part of the codebase.

### `Effect` vs `RuleOperation` — kept as two layers

Most subtypes map 1:1, but the layers diverge on:

- **Random amount resolution**: `Effect.Heal(DiceExpression)` becomes `RuleOperation.InvokeHeal(IModel, int)` after the dispatcher rolls the dice.
- **Conditional expansion**: `Effect.AddExtraHit(OnRollValue, Count)` produces zero or more `InsertExtraHits` operations depending on natural rolls.
- **Aura expansion**: `Effect.Aura(RuleName)` produces multiple `GrantTokenToUnit` operations, one per unit-mate.
- **Live entity binding**: engine-primitive operations (`InvokeTriggeredMove`, `InvokeReactivate`, `InvokeDealHits`) carry specific `IUnit`/`IModel` references that aren't present in the context-free Effect declaration.
- **Queue manipulation**: `RuleOperation.SuppressRule(RuleName)` removes pending operations from the dispatcher queue — has to operate on resolved Operations, not on declared Effects.

The split costs ~80% mechanical duplication for a queue that's pure data once produced — deterministic, serializable, inspectable. The TDD approach in Phase 6 depends on this: tests assert on the returned operation queue rather than mocking game state and observing side effects. Origin-rule tracking for `SuppressRule` is dispatcher sidecar metadata, not a field on the records.

### Discriminator subtypes lifted into data instead of duplicated as effects

`RollKind` (Hit / Save / Morale) and `StatKind` (Quality / Defense / Tough) carried as data on effects like `RollModifier(ERollKind Roll, int Delta)` and `StatModifier(EStatKind, int, ELifetime)` rather than spawning three sibling subtypes per roll/stat. Keeps the Effect vocabulary tight.

### Other small calls

- **`Aura` kept separate from `AddRule`**: Aura propagates to all unit-mates while bearer lives; AddRule grants to bearer only with explicit `ELifetime`. Could have collapsed via a hypothetical `ELifetime.AuraToUnit` scope but the propagation semantics differ enough to justify their own subtype.
- **`Heal` is the only Effect with a `DiceExpression` parameter**. Every other amount field is plain `int` or `float` — fixed authored values per the rule corpus surveyed so far.
- **`DiceExpression` carries both generic `DX(int Sides)` and specific `D3`/`D6`/`CoinFlip`** subtypes. Known overlap (e.g. `DX(3)` and `D3` are distinct types representing the same roll) — flagged for resolution if a future rule forces the issue.
- **`Reactivate` has no parameters**. Self-reactivation is the only case in the corpus; if a future rule reactivates someone else, add a `UnitID Target` parameter.

### Composition records

- `HookEntry(EHookID, Condition, Effect, ELifetime)` — atomic unit of passive rule wiring.
- `ActivatedAbility(EHookID TriggerHook, Cost, TargetSelector, Effect, Condition AvailableWhen)` — player-triggered abilities and spells. Spells are activated abilities with `Cost.SpellTokens`.
- `SpecialRuleDefinition(string Name, IReadOnlyList<HookEntry> Passive, IReadOnlyList<ActivatedAbility> Activated)` — top-level rule record. `Aliases` and `DisplayName` originally on this record were removed in Phase 4 (see notes below) — aliases live on the resolver instead so custom armies don't have to mutate core rule data.

### Test coverage

Phase 3 is data shapes only — no new tests. Test suite remains at 175 green. Phase 4 (hook bus) and Phase 5 (test harness) follow next; Phase 6 begins the 20-test red baseline.

## Phase 4 implementation notes (2026-05-24)

The bus skeleton landed as five files in `Rules/Dispatch/`: `IHookContext` (marker, exposes `EHookID Hook`), `IRuleHookBus` / `RuleHookBus` (stub returning empty list), and `IRuleResolver` / `ResolvedRule` / `RuleResolver` (the registry).

Two design changes from the original sketch, both driven by the question "how does a custom army register an alias for a core rule?":

- **Aliases moved off `SpecialRuleDefinition` onto the resolver.** The original shape made the canonical definition list its own aliases — but that meant a custom army had to mutate core rule data to flavor-rename Regeneration as "Healing Pods." Now the resolver exposes `RegisterAlias(alias, existingName)` and stores both keys in a single dictionary pointing at the same `SpecialRuleDefinition` instance. Core data is untouched; the army owns its names.
- **`Resolve` returns `ResolvedRule(RequestedName, Definition)` instead of a bare definition.** The wrapper exists because the two requirements pull in different directions: `Effect.IgnoreRule("Regeneration")` must catch units that authored it as "Healing Pods" (drives **identity-based** comparison on `Definition` reference), but the UI must still display "Healing Pods (Regeneration)" (needs the caller's original name preserved). The wrapper holds both.

`DisplayName` was also dropped from `SpecialRuleDefinition` in the same pass — aliases cover the rename case; the field was speculative.

Concrete hook context types (`HitRollCompleteContext` etc.) are intentionally **not** added yet. They'll be created one at a time in Phase 5/6 as red tests demand them, so we don't guess at fields that aren't yet exercised.

The stub bus is still wired to return `new List<RuleOperation>()` on every call — real dispatch logic (hook-ID indexing, scope filtering, condition evaluation, effect→operation translation) lands in Phase 7a onward.

## Phase 5 implementation notes (2026-05-28)

`Tests/RulesHarness/TestRuleHarness.cs` is the single scaffold every rule test builds on: it owns the data store, a `TestGameContext` (deterministic `FixedDiceRoller`), the `RuleResolver`, and the stub `RuleHookBus`, and exposes `Register` / `RegisterAlias`, `BuildUnit(player, modelCount, params ruleNames)`, `AttachRule(unit, definition)`, and `Fire(IHookContext)`. `RuleAssertions.HasOperation<T>()` reads the returned queue. The smoke test `HarnessFires_NoRules_ReturnsEmpty` confirms the wiring against the stub bus.

**Rule→unit linkage decided here.** The harness needs somewhere to record a unit's attached rules, and the eventual Phase 7a dispatcher needs to read them back. Chose to add the link to `IUnit` now (over a harness-private map) so the harness mirrors production:

- `IUnit` gains `IReadOnlyList<ResolvedRule> RuleDefinitions`. `UnitData` stores it in a backing list with a `[JsonIgnore]` accessor plus `AttachRuleDefinition(ResolvedRule)` — mirroring the `Tokens` backing/accessor split. Existing constructors are untouched; attachment is post-construction.
- Stores `ResolvedRule`, not a bare `SpecialRuleDefinition`, so the requested name survives for alias display ("Healing Pods (Regeneration)") — the reason `ResolvedRule` exists.
- `[JsonIgnore]` is deliberate: a unit's rules resolve from army-list rule *names* against the host registry at load (see networking note 2026-05-24), so names — not serialized definitions — are the persisted form. That load-time resolution is still an unbuilt TODO (`UnitData.GetRealSpecialRulesFromArmyList`); Phase 7/army integration wires it.
- `IModel` gets no equivalent yet — model-scoped rule attachment is deferred to the first model-scoped rule test (e.g. Regenerative Strength) in Phase 6/7.

Concrete hook contexts are still deferred; a minimal internal `TestHookContext(EHookID Hook)` record lets the smoke test fire the bus until the first payload-bearing context arrives in Phase 6.

Refactor alongside: `Tests/TerrainTestHelpers.cs` (whose contents were all generic — nothing terrain-specific) was split into `Tests/Doubles/{FixedDice,TestGameContext,NullServices,NoOpLayer}.cs`, same `FDG.Tests` namespace + `internal` visibility, so the seven consuming test files compiled unchanged. Full suite green at 176 tests.

## Phase 6 implementation notes (2026-05-28)

Scope expanded mid-phase from "20 curated shape tests" to **a unit test for every special rule in GF Core Rules v3.5.1, except AP (which stays a weapon stat)**. The 18 curated shape tests (offer/accept, auras, tokens — using illustrative *faction* rules like Mend / Piercing Frenzy / Unstoppable Mark, which are NOT core) are kept as shape coverage; core-rule coverage is additive.

Key design decisions:

- **Activated-ability offer/accept** (shape behind Mend/spells): an offer is **not** a `RuleOperation` (operations are resolved/deterministic; an offer is a pre-decision request). Surfaced via a separate `AbilityOffer` record + a two-call API: bus `GatherOffers(ctx)` / `ResolveAbility(offer, targets)`, harness `OfferAbilities` / `Accept`. `Accept` returns cost-consumption ops **and** effect ops in one queue.

- **Argument model** (per-instance rule args — Deadly(3), Tough(6), Caster(2)): the rulebook has 8 single-int rules and **zero multi-arg rules**. Modelled variadically anyway and **not int-locked**: `RuleArgument` is a closed union (`Int` now; `Str`/`Float`/`Enum` added on demand — e.g. Alien Hives' Spawn(unit-type) → `Str`). Per-instance values live on the attachment (`ResolvedRule.Arguments`) and may be supplied/overridden at reference sites — collapsing per-instance args and reference-overrides into one mechanism. Arg-driven effect fields use `ValueSource` (`Literal | Arg(index)`); fixed-value effects keep plain ints. "Cap at 2 args for now" is an authoring guideline, not a structural limit.

- **Tough is a special rule again**: `Tough(X)` sets model max wounds at creation (models default to 1); `EStatKind.Tough` is the read-back for threshold queries (Hero / Transport / Takedown). The rulebook treats Tough as both a wound count and a queried threshold, confirming the write-a-stat / read-the-stat split.

- **Queue-level now, behavior later**: Phase 6/7 assert only that the correct `RuleOperation` is **queued**; an `Effect` may be defined but its execution unimplemented. Executing effects against engine state + behavior-level tests are a deliberate second pass — **Phase 8** on the checklist.

- **Per-rule template**: each rule = inline `SpecialRuleDefinition` (HookEntry = hook + condition + effect) → fire its context → assert the queued operation. Add a new `Effect` per new authored intent; add a new `RuleOperation` only when no existing one expresses the resolved action (Effect→Operation is mostly 1:1 but can be 1→N or reuse an existing op). Contexts kept **minimal** — fields added on demand, with intent comments noting likely future additions.

Progress at the prior commit: **19 of 32 core rules covered (RED)** + 18 shape tests. Suite was 176 green, 31 RED.

### Phase 6 completion notes (2026-05-28, second session)

Added the remaining structural/engine core rules. **30 of 32 core rules now have RED tests** (Hero and Transport deliberately deferred — see below). Suite: **176 green, 42 RED** (intended baseline).

- **Authoritative source = the v3.5.1 PDF, not the OPR Community Wiki.** The wiki's GF special-rules page is an older/different cut: it had *dropped* Takedown, Artillery, and Limited and *added* Lance / Lock-On / Poison / Sniper / Entrenched, none of which are in the v3.5.1 Core Rules PDF the project targets. Confirmed the project's 32-rule corpus (excl AP) against the PDF (`GF - Core Rules v3.5.1.pdf`). When the rule text matters, read the PDF.
- **Hero and Transport(X) are skipped from the queue-level RED baseline** (decision 2026-05-28). Both are essentially static metadata with no natural runtime `RuleOperation`: Hero = deployment legality + "may take morale tests on behalf of the unit" + "uses unit's Defense until others dead"; Transport(X) = carry capacity. Inventing operations for them now would be speculative generality. They land when the #042 engine refactor defines the relevant primitives (unit-joining, morale delegation, transport embark/disembark), at which point their behavior — and any operations — get RED-tested for real.
- **New vocabulary added this session** (each only because a rule in scope needed it):
  - `Effect.GrantToken.Count` migrated from `int` → `ValueSource` so Caster(X) grants `Arg(0)` tokens; the two existing literal grants (Piercing Frenzy, Unstoppable Mark) wrap `Literal(1)`. `RuleOperation.GrantTokenToUnit` keeps an int count (resolved side).
  - New Effects + matching RuleOperations: `StrikeFirst` (Counter), `TargetIndividualModel` (Takedown), `RestrictActions(Allowed)` (Immobile; also Artillery Hold-only facet), `RangeModifier`/`ApplyRangeModifier` (Aircraft), `IgnoreTerrainEffects` (Flying + Strider), `DeferDeployment` (Ambush + Scout).
  - Reused existing vocab: Artillery's "+1 to hit >9\"" and Caster/Limited token grants need no new operation types.
  - New contexts: `RoundStartContext`, `CounterTriggerContext`, `ShootTargetsSelectedContext`, `PostShootContext`, `PreDeploymentSelectContext`. Artillery/Aircraft reuse `HitRollModifierContext`; Immobile/Flying/Strider reuse `MoveActionDeclaredContext`.
- **Multi-facet rules tested at their headline facet**, with the secondary facets noted in code comments as deferred: Counter (−1 Impact per Counter model), Aircraft (Advance-only + 30" straight-line movement), Artillery (enemies −2 from >9", Hold-only), Flying (move *through* units), Strider-vs-Flying terrain-scope distinction. These are Phase 8 execution concerns.

## Phase 7 design notes (2026-05-29) — the dispatch model

Before writing the 7-spine dispatcher, a design session worked the "which units, from
whose perspective" question (the 7-spine CRUX) from first principles instead of patching
it case-by-case. The conclusions below supersede the tentative "evaluate every referenced
unit and let hook+condition decide" lean recorded against 7-spine in the checklist.

### The fundamental framing: a rule is an egocentric statement keyed on a "when"

Every special rule is authored from the point of view of its bearer — "when *I* roll to
hit," "when *an enemy* shoots *me* from >9\"," "when *I'm* charged." The terms *I* / *the
enemy* / *the target* are meaningless until the bearer's place in the event is known. The
earlier struggle (whose dice, which side, what distance) was the symptom of never modelling
two things: **(1) the event, completely** — the full relational situation, not a thin
per-hook snapshot — and **(2) the bearer's role within it**, since the rule resolves
entirely relative to that role.

The organizing primitive is therefore the **"when"**: a perspective-anchored trigger.
"When this unit rolls to hit" and "when another unit rolls to hit this unit" are two
*different* whens over the same underlying engine event (a hit roll). Perspective is
**fused into the when**, not carried as a separate `Side`/role field — so an author
cannot express a contradictory seat (the failure mode a separable side-tag allowed). This
is the eventual rule-authoring UX: each rule starts by picking a "when" from a finite,
auditable list. It generalizes the old `ISpecialRule_Attacker`/`_Defender` split into data,
and scales past a binary actor/subject to N-role events ("when a unit I destroyed dies").

### Whens stay basic; conditions are separate

A when names only **moment × seat**. Everything quantitative or comparative (">9\"", "a
natural 6", "target has Tough") is a **condition** layered on top (the list may be empty).
Two reasons: (a) keeps the when-list finite — without this, every numeric variant becomes a
new when and the list degenerates into one-hook-per-rule; (b) conditions are the unit that
*stacks*. Firing order: **when fires the event → its conditions are evaluated (possibly
none) → if they pass, the effect is added to the queue.** Rule of thumb for the line: if
two rules trigger at the same moment from the same seat and differ only in a test, that
difference is a condition, not a new when.

### Dispatch reads only the event's named participants — no world scan

For a given when, the bus evaluates rules carried **only by the units the event already
names** — the target for defender-side whens, the attacker for attacker-side whens (exactly
the old `GetDefenderSpecialRules()` / `GetAttackerSpecialRules()` scoping, as data). It never
scans the table. Relational rules (Fear: an enemy in melee worsens my morale; Melee
Shrouding: the charged unit slows the charger) **do** read another unit's rules — but that
unit is a *participant in the same event*, read via a `TargetHasRule`-style condition. So
"rules from other units" survives, scoped to the event's cast, never the whole board.

### Auras: distance auras dropped; hero-in-unit is static promotion

The only thing that ever motivated a board-wide scan was a standing distance aura ("buff
friendlies within X\""). **The corpus has none** — so building proximity-scan machinery for
it would be speculative generality (violates the project's no-speculative-generality
principle). Decision: **drop distance auras entirely.** The real "aura" in GF is a **Hero
joined to a unit** granting the unit a rule — modelled as **static promotion** (the rule
becomes one the *unit* carries, resolved once at join time, off the event path; permanent in
v1, no removal logic). Hero is already deferred until the engine has unit-joining, so auras
can be set aside for the current phase entirely. The Phase 6 shape test "applies to the whole
unit" (test 12) collapses to "the unit carries the rule, every model benefits" — no scan, no
token, no live re-check.

Tripwire: the day a *single* rule keys on "within X\" of a non-participant," the pull model
(aura = a rule on the granter, projected onto units at event time via a live condition; **not**
a stored token — a token is *remembered state*, an aura is a *live query*, and storing a live
query as state is the maintenance trap we explicitly rejected) returns — for that rule only,
and only then.

### Effect composition / ordering — mostly free, small residue

We keep the **declarative stacking** model (effects → inspectable operation queue) over the
old manual context-mutation. The cost of going declarative is owing an explicit *resolution
model* — but it turns out small, because **if whens are pipeline points, the game's own
resolution sequence does almost all the ordering for free** (a reroll attaches to a different
when than a +1; running the engine's steps in order runs them in order). Residue:

- **Suppression is the one true cross-cutter.** `IgnoreRule`/`SuppressRule` removes *other*
  effects, so it resolves in a **first pass** (apply removals, then fold the rest) — not a
  phase architecture. (Matches the existing "Order tag for SuppressRule is dispatcher sidecar
  metadata" note in the Phase 3 notes.)
- **Set-vs-add within one when** ("set Quality to 2" beating a "+1") is the only within-step
  conflict and is rare. Resolve with a tiny fixed effect-kind precedence **only when a real
  rule pair forces it** — do not build a priority system speculatively.
- **OPEN QUESTION — feedback/cascades.** Does an effect's *generated output* re-enter the
  when-pipeline? (AddExtraHit makes hits — do those hits re-fire the hit whens and re-trigger
  Furious?) Usually "no — generated hits are auto-hits, terminal," but this must be **stated**
  before the fold is written, or stacking silently becomes recursive. Decide at 7b.

### What this means for the checklist

- 7-spine's CRUX is settled: **enumerate the event's named participants; for each, evaluate
  its rules whose when matches the firing event (perspective fused, so a wrong-seat rule
  simply doesn't match); evaluate conditions with the bearer as self; map passing effects to
  operations.** No board scan, no separable side-tag.
- The harness/tests need the event to carry its full participant cast (already true for the
  attacker/target contexts) so conditions can read the other participant. Whether `Fire`
  needs to name a bearer explicitly depends on the final when representation — to be pinned
  when 7-spine is implemented.
- 7e (Aura + parameter override) shrinks dramatically: distance auras are gone; "whole-unit"
  is static promotion. The override half (parameterized rule references) still stands.

## Phase 7 implementation notes (2026-06-03)

The 7-spine dispatcher landed, and two decisions from the design session changed shape
from what the earlier notes assumed. Both are now in code (`Rules/Dispatch/RuleEvaluator.cs`).

### The dispatcher is a direct-addressing evaluator, not a bus

Working the "which units, from whose perspective" question to its end killed the bus. A
message bus exists for pub/sub decoupling — anonymous subscribers a publisher doesn't know
about. None of that applies here: rules live on units (no registration), the caller always
knows the units involved (the event's named participants, or all units for round-level), and
the caller needs the operation queue back **synchronously** to apply it. That's a query, not
a broadcast. So dispatch is a stateless `RuleEvaluator`:

```
IReadOnlyList<RuleOperation> Evaluate(IUnit unit, ERuleSeat seat, IHookContext context)
```

The stage addresses each involved unit directly, once per seat (attacker as `Actor`, defender
as `Subject`), concatenates the queues, and applies them. Perspective is carried by
`ERuleSeat { Actor, Subject }` — a field on `HookEntry` defaulting to `Actor`; an entry fires
only when its seat matches the seat the caller is evaluating, so a defensive rule on an
attacking unit simply can't match (verified by `Stealth_OnAttacker_DoesNotApplyModifier`). It's
a class, not static functions, because it will hold injected collaborators (dice roller for
random-amount effects at 7c; resolver for name references/auras at 7e). Conditions read context
fields through tiny **capability interfaces** (`IHasDistance`, `IHasUnmodifiedHitRolls`) so the
evaluator never downcasts to a concrete context type; `EvaluateCondition`/`MapEffect` are
pattern-match switches over the closed `Condition`/`Effect` sum types (the idiomatic shape for a
tree-walking interpreter; data stays pure in the Definitions layer). The bus
(`RuleHookBus`/`Fire`/`GatherOffers`/`ResolveAbility`) survives only as scaffolding for
not-yet-migrated tests and retires when nothing references it.

### Dice results are `IDiceResults`, and dice-derived counts are `float`

The engine's dice abstraction (`Utilities/Probability`) is a **per-face histogram**
(`IDiceResults`: `At(face)` = count on that face, as a `float`), and there are two rollers behind
the same interface: the realistic one (integer counts) and the **`ProbabilisticDiceRoller`**
(every face = `rollCount / sideCount`, i.e. expected counts — an enable-able "average outcome"
mode). The Phase 6 tests had modelled rolls as `IReadOnlyList<int>` per-die lists, which is both
the wrong shape and int-locked — it silently breaks probabilistic mode (`3.5 != 6`).

Fixed: roll-bearing contexts (`HitRollCompleteContext`, `SaveRollCompleteContext`) and
`IHasUnmodifiedHitRolls` now carry `IDiceResults`; "natural 6" is `results.At(6)`; and
dice-**derived** operation counts (`InsertExtraHits`, `InsertExtraWounds`, `InvokeHeal.Amount`)
are `float`, because they come out fractional under the probabilistic roller. Authored counts
(`MultiplyHits/Wounds` multipliers, `ChargeImpactHits` dice-to-roll, `InvokeDealHits` fixed hit
count) stay `int`. **Project-wide invariant: never represent a roll, or a value derived from a
roll, as an int — everything must survive the probabilistic roller.** A "natural-N" rule's
`UnmodifiedRollEquals` condition is at most a guard (`At(N) > 0`, ~always true probabilistically);
the real quantity lives in the effect (`At(N) * Count`).

### Done so far

Stealth and Furious are green through `Evaluate` (179 pass / 40 red). Conditions implemented:
`DistanceGreaterThan`, `UnmodifiedRollEquals`. Effects: `RollModifier`, `AddExtraHit`. Remaining
rules flip `Fire -> Evaluate` as each is implemented. Rule definitions are still inline in tests
(no C# catalog / JSON yet — deliberately deferred to the future loader).

## Phase 7 implementation notes (2026-06-06) — polymorphic dispatch, validator, rule migration, bearer/arg threading

Three structural changes this session took the suite from **179 / 40** to **215 / 8** (then **231 / 8** after merging master's +16 LoS/terrain tests). Every rule that can be expressed through *passive* dispatch is now green through `RuleEvaluator`. The changes supersede parts of the 2026-06-03 notes (specifically, the `EvaluateCondition`/`MapEffect` switch is gone).

### 1. Switches → polymorphism on the records (generic-intermediate capability layer)

The 06-03 dispatcher evaluated conditions/effects with two `switch` statements in `RuleEvaluator` that downcast the context to a capability interface. Replaced with virtual methods on the records themselves: `Condition.Evaluate(RuleInvocation)` and `Effect.Apply(RuleInvocation, ops)`. `RuleEvaluator` is now just the loop — no switch, no casting, no capability knowledge.

The capability requirement is expressed via a **generic intermediate**: `CapabilityCondition<TCap>` / `CapabilityEffect<TCap>` (in `Rules/Definitions/`) where the type argument *is* the required capability. A leaf binds it once in its base clause (`DistanceGreaterThan : CapabilityCondition<IHasDistance>`); from that single binding both the typed `EvaluateCore(TCap)`/`ApplyCore(TCap, ops)` body (can't read the wrong capability) and the reflectable `RequiredCapabilities` (`[typeof(TCap)]`) derive. Capability-free primitives (Always, RollModifier, the constant effects, token/arg effects) override the untyped method on the base directly and inherit empty `RequiredCapabilities`; composites (`And`/`Or`/`Not`) union/passthrough their children's. New marker `ICapability` (Foundation) tags the capability interfaces so the validator/catalog can find them precisely.

**Why polymorphism over the switch, given a closed sum type:** the switch's only real cost was shotgun-surgery (edit the enum *and* two switch arms) — and that cost is worth removing because the eventual authoring path is **JSON via a tool that links this library** (never hand-written). The compiler never sees a tool-composed rule, so compile-time exhaustiveness was never going to guard authored rules anyway; per-record methods + a runtime validator (below) is the honest shape. Capability interfaces stay the decoupling seam (a condition needs a *capability*, not a context type), unchanged by the switch→method move.

### 2. `RuleValidator` + `HookContextCatalog` — the authored-rule safety net

Because rules will be authored as data (no compile-time check), the safety net is **validate-before-save (the tool) + validate-at-load (the engine)**, sharing one implementation. `RuleValidator.Validate(SpecialRuleDefinition)` checks, for each `HookEntry`, that the condition's and effect's `RequiredCapabilities` are all provided by the context fired at that hook; returns `RuleViolation`s. Semantics: empty requirement → always valid; unknown hook → skipped (no false positive). `HookContextCatalog` supplies "what capabilities does hook H's context provide," built **by reflection** (scan `IHookContext` implementors, read each constant `Hook` off an uninitialized instance via `RuntimeHelpers.GetUninitializedObject`) so there's no hand-maintained table to rot. 4 tests in `Tests/RuleValidatorTests.cs`. (The future tool would also drive its condition/effect dropdowns off the same capability metadata, so a mismatched pairing is *unconstructible*, not just rejected.)

### 3. `RuleInvocation` — threading bearer + arguments through dispatch

`Evaluate`/`Apply` originally received only the `IHookContext` (the world event). A class of rules also needs **who the bearer is** and **the attachment's arguments**, which can't live on the hook context (the same event is evaluated for multiple units/seats). Bundled into `RuleInvocation(IHookContext Hook, IUnit Bearer, IReadOnlyList<RuleArgument> Arguments)` (built per-rule in `RuleEvaluator`'s loop) and passed to `Evaluate`/`Apply` in place of the bare context. `ValueSource` gained a polymorphic `Resolve(arguments)` (Literal → value; Arg(i) → `arguments[i]`). This unblocked the argument-bearing effects (Deadly/Tough/Blast/Impact/Fear via `ValueSource.Arg`), the bearer-targeted token effects (Caster/Piercing Frenzy/Limited via `GrantToken`; new `TokenType.RuleGrant` constant), the `Aura` effect (one rule-grant token on the bearer; per-model expansion still deferred), and the `TokenPresent` condition (reads `Bearer.Tokens.GetTokenCount`).

The public `RuleEvaluator.Evaluate(unit, seat, context)` and harness `Evaluate(...)` signatures are unchanged — only the per-record `Evaluate`/`Apply` take `RuleInvocation`.

### Capabilities / contexts added this session

`IHasActionType` (in `Definitions`, not `Foundation`, because it references `EActionType` — a Definitions type — and Foundation must stay dependency-free), `IHasAttackerMoved` (Foundation). `HitRollCompleteContext` now also implements `IHasDistance` (Relentless's `And(UnmodifiedRollEquals, DistanceGreaterThan)`); `HitRollModifierContext` implements `IHasAttackerMoved` (Indirect); `MoveActionDeclaredContext` implements `IHasActionType` (Fast/Slow/Immobile).

### Migration status — 215 green / 8 red (pre-merge); 231 / 8 after merging master

Migrated to `Evaluate` (with their seats verified by going green): Stealth, Furious, Surge, Rending, Artillery, Reliable, Aircraft, Fast, Slow, Melee Shrouding, Bane, Thrust, Indirect, Unstoppable, Fearless, Regeneration, Counter, Takedown, Immobile, Flying, Strider, Scout, Ambush, Relentless, Deadly, Tough, Blast, Impact, Fear, Caster, Piercing Frenzy (×2), Limited, Regeneration Aura. Defensive rules (Stealth, Melee Shrouding, Regeneration, Counter, Aircraft) carry `ERuleSeat.Subject`.

**The 8 remaining reds are out of scope for *passive* dispatch:**
- **7 activated abilities** (Mend ×2, Advanced Sight, Unstoppable Mark place, Vanguard, Martial Prowess, Strafing) — still on the `GatherOffers`/`ResolveAbility` bus stub. That's **Phase 7c**: turn the offer/accept path into real dispatch. This is the obvious next milestone (clears 6 of the 8).
- **1 token-clear lifecycle** (Unstoppable Mark owner-destroyed) — needs the `TokenClearService` that walks containers on `OnUnitDestroyed` and removes `OwnerDestroyed`-triggered tokens. Not an operation-queue concern.

Also still deferred (unchanged from prior notes): behavior-level execution of the queued operations (**Phase 8**), the C#-catalog/JSON loader (rule definitions are still inline in tests), per-model aura expansion, and Hero/Transport (await the engine refactor's unit-joining/transport primitives).

## Phase 7c implementation notes (2026-06-06) — activated abilities as direct dispatch

The 7 activated-ability tests are green (suite **238 / 1**; the lone red is test 16,
the 7g owner-destroyed cleanup). Activated offer/accept moved off the `RuleHookBus`
stub onto `RuleEvaluator` — the same "it's a query, not a broadcast" reasoning that
killed the passive bus. The one new fact an activated ability carries over a passive
rule is **a target distinct from the bearer**; everything else is mechanical reuse.

### Offer/accept live on RuleEvaluator, reusing the one Effect.Apply

`RuleEvaluator` gained `GatherOffers(context)` and `ResolveAbility(offer, targets)`
beside `Evaluate`. `GatherOffers` reads the acting unit off the context via a new
`IHasActingUnit` capability (in `Definitions`, not `Foundation`, because it refs
`IUnit` — same layering rule as `IHasActionType`), walks its `Activated` abilities,
and keeps those whose `TriggerHook` matches, whose `AvailableWhen` passes, and whose
`Cost` is affordable. `ResolveAbility` emits cost ops then applies the effect once per
target. The harness `OfferAbilities`/`Accept` re-point to the evaluator; the bus is now
a `Dispatch`-only stub backing `harness.Fire` (smoke test + test 16) until 7g.

### Target threading — extend RuleInvocation, don't fork Apply

`RuleInvocation` is now the **resolution environment**: `(IHookContext? Hook, IUnit
Bearer, args, IUnit? Target = null, IDiceRoller? DiceRoller = null)` with computed
`EffectiveTarget => Target ?? Bearer` and `OwnerForEffectiveTarget`. Effects land on
`EffectiveTarget`, so the **same polymorphic `Effect.Apply`** serves passive (Target
null → bearer) and activated (explicit target) — no parallel switch, and `GrantToken`
isn't duplicated. `Hook` went nullable because an accepted ability resolves off the
live-event path; only capability-typed effects read `Hook`, and they only ever fire
under `Evaluate` (Hook always set), so `null is TCap` is a safe miss. The evaluator
holds an injected `IDiceRoller` (anticipated in the 06-03 notes) and threads it into
every invocation; `Heal` rolls `DiceExpression.Sides` and reduces the histogram to a
scalar pip-total (`Σ face·At(face)` — fractional under the probabilistic roller, hence
`InvokeHeal.Amount` is float). Six effects got target-aware `Apply` bodies: `GrantToken`
(edited), `AddRule`, `Heal`, `TriggeredMove`, `Reactivate`, `DealHits`.

### Token owner is a unit, not a player (design call, 2026-06-06)

`OwnerUnitID` stays `UnitID`. Decisive rule: **Unstoppable Mark**'s `OwnerDestroyed`
clear means "remove when the *placing unit* dies" — a unit dying and a player being
eliminated are different events (a player isn't out until their last model dies, and
can win on objectives with zero models), so a `PlayerID` owner would make `OwnerDestroyed`
near-permanent. `UnitID` is the strict superset: player is derivable from unit (lookup),
unit is never derivable from player; the DAO-targeting-laser "any of my units may spend
it" pattern is a *query-site* concern (group marks by `owner → PlayerID`), not a storage
one. Owner-stamping rule: bearer whenever a token lands on a unit ≠ bearer, null
self-targeted — uniform across `GrantToken`/`AddRule`, falls out of `Target ?? Bearer`.

### Cost ops emitted now; what stays untested-but-emitted

`ResolveAbility`'s queue is the full transaction. `SpellTokens`/`ConsumesToken` →
`ConsumeTokensFromUnit`; `OncePer{Activation,Round,Game}` → grant a per-ability
`"AbilityUsed:<rule>"` marker with the matching clear trigger; affordability is filtered
in `GatherOffers`. **Untested-but-emitted** (no current test asserts them): the OncePer*
cost ops + their affordability (only `SpellTokens` affordability is exercised, test 07),
`Heal`'s amount, and `Heal`'s model pick (first model). Their *execution* and the
used-marker / `FirstTrigger` clearing land in 7d / Phase 8.

### Remaining red + still deferred

Unchanged deferrals: behavior-level execution (Phase 8), C#/JSON catalog, per-model
aura expansion, Hero/Transport, activated-ability args (no corpus ability uses
`ValueSource.Arg`, so `AbilityOffer` carries none — thread `ResolvedRule.Arguments`
when one appears).

## Phase 7g implementation notes (2026-06-06) — cross-unit token cleanup → suite 241/0

The last red (owner-destroyed cleanup) is green; **the full Phase 7 RED baseline now
passes**. `TokenClearService` (in `Rules/Tokens`) exposes
`ClearForDestroyedOwner(UnitID destroyedOwner, IEnumerable<ITokenContainer>)`: it walks
every supplied container and removes the tokens the dead unit *owns* (via
`TokensWithOwner`) whose `ClearTrigger` is `OwnerDestroyed`. Clearing is **trigger-driven**
— a token the dead unit owns under a different trigger is left alone (this supersedes the
early arch-sketch line "all tokens with OwnerUnitId == A are removed"; the per-trigger
model is the one we built). The service is hook- and store-agnostic: the caller passes the
containers and the dead unit's ID, so the harness walks the data store today and a real
destroying stage will walk `ITableState` once rules are wired into the engine. It is **not**
a bus subscription — the bus is dead; `harness.Fire` routes a `UnitDestroyedContext` to the
service directly.

New container API: **`ITokenContainer.RemoveTokensWithOwner(type, owner, count)`** —
owner-*scoped* removal. The existing `RemoveTokens` is owner-*agnostic* by deliberate Phase 2
design ("owner-scoping lives in the layer above"), so without this a placer's mark couldn't
clear without risking another placer's same-type mark on the same target. `TokenContainer`
now routes both removers through a private `RemoveMatching(predicate, count)` core; +2
`TokenContainerTests` lock the owner-scoping (other owners' tokens survive; missing owner is
a no-op).

## Outcome

(pending — written when items 026–034 can proceed against this architecture)
