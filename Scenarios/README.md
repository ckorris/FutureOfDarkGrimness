# Scenarios (#167)

A scenario is a compact JSON file describing a game state — armies, model placements, pre-applied
wounds/tokens, and whose activation comes next. The compiler turns it into a normal `.fdgsave`
positioned at the **start of the active player's activation**, so loading it means the very next
decision is the one your test targets. Setting up a rule test drops from ~10 minutes of playing to
editing ~20 lines of JSON, and every repetition is identical.

## Commands

```bash
# Compile a scenario to a save
dotnet run --project FdgRaylib/FdgRaylib.csproj -- --make-scenario Scenarios/example-shootout.json out.fdgsave

# Launch a scenario directly - no main menu, no lobby. Slot 0 is you, every other slot is AI.
# Takes either the .json (compiles in-memory) or a compiled/in-game .fdgsave.
dotnet run --project FdgRaylib/FdgRaylib.csproj -- --scenario Scenarios/example-shootout.json

# Headless (CLI resolvers for slot 0; pipe stdin or let EOF defaults drive)
dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless --scenario Scenarios/example-shootout.json
```

## Scenario JSON

```jsonc
{
  "name": "What this scenario tests",
  "round": 1,                          // 1..4, the round the scenario starts in
  "activePlayer": 0,                   // index into players: whose activation comes next
  "settings": {
    "randomness": "Probabilistic",     // Probabilistic (default; histogram dice - see below) or Realistic
    "diceSeed": 42                     // optional: seeded Realistic dice for repeatable runs
  },
  "objectives": [[18, 24], [36, 24]],  // optional [x,z] markers; default = 3 across the midline
  "terrain": [                         // optional; absent = open table
    { "type": "Blocking|Impassible",   // ETerrainType flags, '|' or ',' separated:
                                       //   Cover, Impassible, Difficult, Dangerous, Blocking, Elevated
      "shape": "Rectangle",            // Rectangle (or Rect) / Circle
      "center": [24, 11],              // [x,z] of the piece's center
      "size": [20, 2],                 // rectangle: [width (x), depth (z)] inches
      "rotationDegrees": 30,           // rectangle only: around the center (in-game dial convention)
      "heightInches": 4 },             // optional, default 0
    { "type": "Cover, Difficult", "shape": "Circle", "center": [30, 30], "diameter": 8 }
  ],
  "players": [
    {
      "army": "armies/Marksmen.fdgarmy",   // path relative to this file
      "team": 0,                           // optional; default = player index
      "units": [
        {
          "unit": "Rending Squad",         // name in the army file (case-insensitive)...
          "unitIndex": 0,                  // ...or index, when names repeat
          "models": [[30, 16], [32, 16]],  // one [x,z] per model; count must match the unit
          "facing": [0, 1],                // optional shared facing normal
          "woundsDealt": [0, 2],           // optional per-model wounds (applied after Tough)
          "activated": true,               // optional: already activated this round
          "tokens": [                      // optional unit tokens
            { "type": "Shaken", "count": 1, "clearTrigger": "ManualOnly" }
          ]
        }
      ]
    },
    { "army": "armies/Dummies.fdgarmy" }
  ]
}
```

Notes:

- **Units you don't list still deploy** — auto-rowed inside their team's deployment band. Only
  position the units the test cares about.
- **Hero joins resolve before matching**: a joined hero's models belong to its HOST unit's entry
  (host models first, hero's last).
- `(0, 0)` is the engine's "not on the table" sentinel and is rejected as a placement.
- Token types are the engine IDs (`Shaken`, `Fatigued`, `SpellTokens`, ...); clear triggers:
  `ManualOnly` (default), `RoundEnd`, `ActivationEnd`, `AttackEnd`, `FirstTrigger`,
  `UnitDestroyed`, `OwnerDestroyed`.
- The four granted roll-modifier tokens (`HitRollModifier`, `SaveRollModifier`, `MoraleRollModifier`,
  `CastRollModifier`) take a signed `"delta"` — the roll stages read that payload, so without it the
  token nets zero and reads as the modifier silently not working. `delta` on any other token type is
  a compile error rather than a silent drop.
  `{ "type": "CastRollModifier", "count": 1, "clearTrigger": "FirstTrigger", "delta": -1 }`
- Terrain pieces compile through the same construction the in-game placement uses, so movement
  sweeps, cover, and LoS treat them exactly like player-placed terrain. **Auto-placed units are
  rowed terrain-blind** — explicitly place any unit whose relation to terrain the scenario tests.
  A circle takes `diameter` (no rotation); a rectangle takes `size` (+ optional `rotationDegrees`).
  See `example-walled-advance.json`.

## Testing workflow (from SpecialRulesAudit.md section 3)

1. **One scenario per rule-mechanism, not per rule.** Rules sharing a primitive reuse one scenario
   with the army file swapped — army files are JSON, cloning for a sibling rule is a one-word edit.
2. **Test in Probabilistic mode first**: histogram dice make modifier arithmetic visible and
   deterministic — an AP change shifts the save fraction every single time, no rerolling through
   variance. Spot-check in Realistic mode (with a `diceSeed`) only for things that genuinely branch
   on discrete outcomes.
3. **Design armies for signal, not realism** (see `armies/`): extreme stats so any modifier flips
   an outcome unmistakably — Quality 2 attackers, Defense 5+ targets, 1-wound models.
4. **When a bug is found, save its scenario here before fixing it** — the save is both the repro
   and, after the fix, the regression check.
5. Run with `--trace-rules` to see every rule hook evaluation (fired / condition failed /
   suppressed) instead of guessing from the numbers.
