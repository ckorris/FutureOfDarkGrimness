# 042 — Special rules architecture

**Status**: in-progress (design phase)
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
- `SpecialRuleDefinition(string Name, string? DisplayName, IReadOnlyList<HookEntry> Passive, IReadOnlyList<ActivatedAbility> Activated, IReadOnlyList<string>? Aliases)` — top-level rule record. `Aliases` lets army-specific renames resolve to the same definition without duplication.

### Test coverage

Phase 3 is data shapes only — no new tests. Test suite remains at 175 green. Phase 4 (hook bus) and Phase 5 (test harness) follow next; Phase 6 begins the 20-test red baseline.

## Outcome

(pending — written when items 026–034 can proceed against this architecture)
