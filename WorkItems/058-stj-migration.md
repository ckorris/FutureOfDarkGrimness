# 058 — Migrate message/save serializer off Newtonsoft TypeNameHandling.Auto to System.Text.Json

**Status**: todo
**Related**: rule-definition JSON loader (#042 sub-stream, the thing that introduced STJ to the engine)

## Goal
The engine ends up on a single JSON library. Today there are two: the new rule-definition format is System.Text.Json (closed, non-generic `Condition`/`Effect`/`ValueSource` sum types — STJ's `[JsonDerivedType]` sweet spot), while the message bus + save/load run on Newtonsoft with `TypeNameHandling.Auto` (`GameDataStore.GetJsonSettings()`). "Done" = the Newtonsoft paths are ported to STJ and the `Newtonsoft.Json` package reference can be removed, with the networked round-trip + save/load tests still green.

**This is pure consolidation — zero functional gain.** It does not gate the rule loader or anything else. Only do it if the single-library/modern-stack aesthetic is judged worth the risk on networked + save-critical submodule code.

## Notes
- 2026-06-13: Filed while building the STJ rule loader. Investigated feasibility — it's more tractable than the "STJ is weak at derived types" reputation suggests, because the only *generic*-polymorphic family is trivial (see Decisions).

## Decisions
What `TypeNameHandling.Auto` is actually carrying through `GetJsonSettings()`, and the STJ replacement per seam:

| Seam | Shape | STJ replacement |
|---|---|---|
| `IZone` → Circular / Rectangular / Rotated / Composite | closed, non-generic (recursive wrappers) | `[JsonDerivedType]` per impl — trivial; STJ recurses through wrappers |
| `PresentationBeat` → 8 beat types | closed, non-generic | `[JsonDerivedType]` per beat — mechanical |
| `CancellableResult<T>` → `Selected<T>(T)` \| `Cancelled<T>` | **generic**, but only 2 cases + 1 payload | **one `JsonConverterFactory`, written once, covers every `T`** |

The generic case is STJ's general weak spot but is trivial here because `CancellableResult<T>` is the smallest possible union. A factory whose inner `JsonConverter<CancellableResult<T>>` writes `{"kind":"selected","value":…}` / `{"kind":"cancelled"}` and recurses via `JsonSerializer.Serialize(w, value, options)` (so the nested `DataBinding<T>` converter still applies) handles all `T`.

The real cost is breadth + risk, not cleverness:
1. **Discovery** — `TypeNameHandling.Auto` is global + implicit; STJ fails *closed* (a forgotten polymorphic seam silently drops subtype data rather than erroring). Must enumerate every seam. Existing round-trip tests (`PresentationBeatSerializationTests`, `CancellableResultTests`, `ConcreteRequestTests`, `TerrainTests`, `VisualsTests`) are the safety net.
2. **STJ constructor strictness** — records bind fine, but the zones are *classes* (`CircularZone`/`RectangularZone`); class types with ctor logic / private setters may need `[JsonConstructor]` or property tweaks where Newtonsoft was forgiving. Most likely friction.
3. **Port `DataBindingJsonConverter<T>`** to STJ's converter API (store builds these per-type reflectively in `GameDataStore` ctor — wiring stays, body changes).
4. **`JsonSerializerSettings`→`JsonSerializerOptions`, `JsonConvert.*`→`JsonSerializer.*`** across ~10 non-test files + tests; rename `GetJsonSettings()`.
5. **Format + version event** — wire and save JSON change shape (`$type`→`kind`); bump save `Version`; host/client must both be on the new build (no mixed-version play). Presumably no shipped saves to preserve — confirm.

Sequencing: build the rule loader on STJ first (done independently); tackle this afterward if at all. Estimated a bounded multi-day refactor, not a rewrite.

## Outcome
_(open)_
