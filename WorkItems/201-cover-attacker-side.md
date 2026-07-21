# 201 — Cover granted by terrain on the attacker's side of the line

**Status**: ruling received 2026-07-21 — plan written (below), awaiting owner sign-off on one flagged
amendment, then implement. Branches: `201-cover-proximity` (superproject + submodule).
**Related**: #044-#046 (line-of-sight cluster), #150 (base-shape geometry), #055 (rule attribution in resolvers), #162 (tactical overlay truthfulness)

## Goal

Shooting *out of* cover must not grant the defender a cover bonus. Today, a unit standing right up
against a wall gets its own wall counted as the defender's cover. "Done" means the cover check only
considers terrain that meaningfully screens the *defender*, per the 2026-07-21 ruling below, with an
integration test per accepted case.

## Reported symptom

2026-07-08 playtest: Battle Brothers positioned flush against a cover wall, firing outward. The
opposing unit — in the open — received the +1 defense cover bonus.

## Root cause

`ShootStage/FireStage/CoverCheckStage/CoverCheckStage.cs:29-36` asks, for each defending model, whether
*any* attacking model's sight line is `ESightLineEffect.Cover`:

```csharp
ESightLineEffect effect = LineOfSightUtilities.EvaluateSightLine(
    attacker.GetValue().PositionBinding.GetValue(), defPos, terrain);
if (effect == ESightLineEffect.Cover) { modelsInCover++; break; }
```

`LineOfSightUtilities.EvaluateSightLine` (`ShootStage/LineOfSightUtilities.cs`) folds every
terrain piece intersecting the attacker->defender segment into a worst-effect, with **no notion of
where along the segment the intersection happened**. A cover piece touching the segment one inch from
the *attacker* is indistinguishable from one touching it at the *defender*. The majority rule at
`CoverCheckStage.cs:40` (`modelsInCover * 2 > defenders.Count`) then turns that into +1 defense.

## Decisions (owner ruling, 2026-07-21)

The official GF rules have no proximity allowance at all — any cover piece crossing the line grants
the bonus. The fix is therefore a pair of **house-rule exceptions**, toggleable as a game setting in
the lobby, **default ON**:

1. **Attacker-exit rule.** If the point where the sight line *exits* a cover piece is less than
   **2 inches** (a named const) from the shooting model's **base**, that piece is ignored (treated as
   Clear for that sight line). Covers a unit lined up along sandbags/a wall.
2. **Shared-cover rule.** If the shooter and the target are both inside the **same** cover piece,
   that piece is ignored unless the base-to-base distance between them is at least **6 inches**
   (named const). Covers two units brawling inside the same forest.

Answers to the original design-fork questions:

- *Proximity / attacker-side exclusion*: rule 1 above — measured at the piece's exit point, per piece,
  per sight line. Not "piece contains the attacker": a shooter hugging the near face of a thin wall
  is outside the wall's zone but is exactly the reported case.
- *Depth / shoot-through*: deliberately none. A deep piece (a 4" forest strip) hugged by the shooter
  has its exit >2" away, so it still grants cover — intended, the shot really traverses it. Only
  thin, muzzle-adjacent pieces get voided. Cover never degrades to Blocking.
- *Symmetry*: shooting only. Melee/spell/impact/strafing paths all seed `CoverCheckResults(0)` and
  never compute cover, so there is nothing to change there. The rules apply identically whichever
  unit shoots (the "shooter" role is per sight line).
- *Interaction with `SightRuleQueries.IgnoresCover` (Blast)*: composes naturally — the proximity
  exceptions run per-piece inside sight-line evaluation and can only *remove* cover; the Blast
  short-circuit at `CoverCheckStage.cs:44-49` stays as the final whole-bonus gate, untouched.
- Both rules downgrade **Cover -> Clear only**. `Blocking` pieces (including model-base blockers from
  `BuildModelBlockers`) are never affected — a wall that blocks sight still blocks it at any range.

### Assessment of the ruling (2026-07-21, Fable)

Both constraints are reasonable and cheap to evaluate; per-piece, per-sight-line semantics compose
cleanly with the existing majority rule and Blast. Consequences worth having on record:

- **Accepted residual**: a shooter 2.5"+ behind their own thin wall still grants the open-field
  defender cover (exit >2" away). That's the deliberate meaning of the 2" const, not a bug.
- **Flagged amendment (needs owner call before/at implementation)**: with a thin wall hugged on
  *both* sides (shooter on one face, defender on the other, ~1-3" apart), rule 1 voids the cover even
  though the defender is genuinely behind that wall — under RAW they'd keep +1. Recommended tweak:
  **skip rule 1 when the exit point is also within 2" of the defender's base** (one extra
  `SurfaceDistanceToPoint2D` call). The plan below implements the ruling *as stated* (no amendment)
  and marks the one line where the amendment would slot in; say the word and it's a one-liner + one test.
- Rule 2's "inside" test uses **model centers** (`IZone.IsPointWithinZone` on the piece), matching how
  sight lines are center-to-center; distance uses the standard base-to-base measure
  (`DistanceUtilities.GetBaseToBaseDistanceInches_3D`), same as weapon range checks. Rule 2 voids only
  the shared piece — a distinct wall between two models inside the same forest still grants cover.
- Old saves/scenarios predate the setting: the field is stored **nullable** and normalized null->ON,
  so "default on" holds for resumed pre-#201 saves too (a plain `bool` would silently deserialize OFF).

## Implementation plan

Engine changes are authorized for this item (submodule-first commit cadence). One vertical slice at a
time; each slice ends green (`dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj`, plus full
`dotnet build` + headless smoke where app code changes) and is committed before the next starts.

### S1 — engine geometry: segment-exit query on `IZone`

`IZone` (`TableState/Zones/Zone.cs`) has `GetFirstSegmentEntry` but no exit. Add:

```csharp
/// Largest-t point in [start,end] where the segment crosses this zone's boundary from inside to
/// outside; null if the segment never intersects the zone OR if end is inside it (no final exit).
Float2? GetLastSegmentExit(Float2 startPosition, Float2 endPosition);
```

The "end inside -> null" contract is load-bearing: when the target stands inside the piece, rule 1
must not apply (rule 2 owns the both-inside case; target-inside-only keeps cover).

Implement on every `IZone` implementer (grep `IZone` for the full set before starting):

- `CircularZone.cs` — line-circle intersection, take the larger root clamped to [0,1]; short-circuit
  null if `IsPointWithinZone(end)`.
- `RectangularZone.cs` — slab method's tExit; same end-inside short-circuit.
- `RotatedZoneWrapper.cs` — rotate endpoints into local space, delegate to `Inner`, rotate the result
  back (mirror its `GetFirstSegmentEntry`).
- `CompositeZone.cs` — null if end is inside any part; else max-t over parts' exits. (Max over parts
  equals the union's last exit whenever end is outside all parts — if the segment were inside some
  other part just after the max exit, that part's own exit would be later, contradicting maximality.)
- `TerrainData.cs` — delegate to `Shape`, like `GetFirstSegmentEntry`; surface on `ITerrain`.
- The test double in `Tests/LineOfSightTests.cs` (~line 202) delegates too.

Tests (new fixture, e.g. `Tests/SegmentExitTests.cs`): per zone type — clean crossing, start inside
(exit still found), end inside (null), no intersection (null), tangent/degenerate, rotated rect, and a
two-part composite where the far part owns the last exit.

Commit (submodule): `#201: IZone.GetLastSegmentExit + per-zone implementations`.

### S2 — engine rules: setting + proximity filter + cover stages (the meat)

**`GameModel/GameSettings.cs`**:

```csharp
/// House-rule cover proximity exceptions (#201): void a cover piece the shooter's muzzle is
/// hugging, and shared cover at knife range. Nullable so pre-#201 saves (field absent in JSON)
/// resolve to the default ON — read via CoverProximityExceptionsEnabled, never directly.
public bool? CoverProximityExceptions;
public bool CoverProximityExceptionsEnabled => CoverProximityExceptions ?? true;
```

`GetDefault()` sets `CoverProximityExceptions = true`.

**New `ShootStage/CoverProximityRules.cs`** (sits next to `LineOfSightUtilities`):

```csharp
public static class CoverProximityRules
{
    public const float AttackerExitIgnoreInches = 2f;   // rule 1
    public const float SharedCoverMinDistanceInches = 6f; // rule 2

    // True if this Cover piece is voided for the shooter->target sight line under the #201
    // house rules. Never called for Blocking pieces.
    public static bool VoidsCover(ITerrain piece, in CoverContext ctx);
}

// Everything the filter needs about the two endpoint models. Built once per model pair.
public readonly struct CoverContext
{
    public Position ShooterPos; public IBaseShape ShooterBase; public Float2 ShooterFacing;
    public Position TargetPos;  public IBaseShape TargetBase;  public Float2 TargetFacing;
}
```

`VoidsCover` logic, in order (cheap check first):

1. Rule 2: `piece.IsPointWithinZone(shooterCenter) && piece.IsPointWithinZone(targetCenter)` and
   `DistanceUtilities.GetBaseToBaseDistanceInches_3D(...) < SharedCoverMinDistanceInches` -> true.
2. Rule 1: `exit = piece.GetLastSegmentExit(shooterCenter, targetCenter)`; if `exit` non-null and
   `BaseShapeGeometry.SurfaceDistanceToPoint2D(ShooterBase, ShooterPos, ShooterFacing, exit) <
   AttackerExitIgnoreInches` -> true. *(The flagged amendment, if adopted, adds here: `&&
   SurfaceDistanceToPoint2D(TargetBase, TargetPos, TargetFacing, exit) >= AttackerExitIgnoreInches`.)*
3. Else false.

**`LineOfSightUtilities.cs`** — add an overload; the existing 3-arg `EvaluateSightLine` keeps its
exact semantics (PolarSightMap and its tests pin a mirror of it — do not touch it):

```csharp
public static ESightLineEffect EvaluateSightLine(Position attacker, Position target,
    IEnumerable<ITerrain>? terrain, in CoverContext coverContext, bool applyProximityExceptions)
```

Same worst-effect fold, except a piece evaluating to `Cover` is demoted to `Clear` when
`applyProximityExceptions && CoverProximityRules.VoidsCover(piece, coverContext)`. `Blocking`
early-out unchanged.

**Consumers** (both loops already iterate model pairs, so the context is at hand):

- `CoverCheckStage.RunStage` — build `CoverContext` from each attacker/defender `ModelData`
  (position, `BaseShape`, `Facing`), pass `GameContext.Settings.CoverProximityExceptionsEnabled`.
- `ChooseRangedAttackStage.ComputeHasCover` (~line 417) — the targeting UI's cover flag (#045/#055
  truthfulness: what the option card shows must match what the stage will roll). Add an
  `applyProximityExceptions` parameter threaded from the stage's `GameContext.Settings`; update its
  existing tests' call sites.

**Tests** — new `Tests/CoverProximityRuleTests.cs` mirroring `CoverMajorityTests` style
(`TestGameContext` + `RunStage`), with a `TestGameContext` settings override for the toggle (check
`Tests/Doubles/TestGameContext.cs`; add a settings hook if it lacks one). Geometry note: rule 1 cases
need a **thin** wall (e.g. `RectangularZone(5.0, 5.5, 0, 10)`) — a deep piece's exit is far away by
design. Cases:

1. Shooter hugging thin wall (~1" from near face), defender in the open -> **0 bonus** (the #201
   symptom, now fixed).
2. Shooter ~3" behind the same wall -> **+1** (accepted residual pinned as intended).
3. Toggle OFF, shooter hugging wall -> **+1** (old behavior preserved).
4. Both inside one 10x10 forest, <6" apart -> **0 bonus**; same but >=6" apart -> **+1**.
5. Both inside the forest <6" apart but a *separate* wall crosses the line -> **+1** (rule 2 voids
   only the shared piece).
6. Shooter inside forest, defender outside, shooter deep (exit >2") -> **+1**; shooter at the far
   edge (exit <2") -> **0**.
7. Majority composition: 3 defenders, 2 of whose sight lines are voided by rule 1 -> **0 bonus**.
8. Blast composition: cover survives proximity but attacker has IgnoresCover -> **0**, log line intact.
9. Settings round-trip: deserialize a `GameSettings` JSON *without* the field ->
   `CoverProximityExceptionsEnabled == true`; explicit `false` survives save/load (mirror wherever
   `GameProgressTests`/`GameSaveLoadTests` pin settings).
10. If the amendment is adopted: both hugging the same thin wall -> **+1**.

Commit (submodule): `#201: cover proximity house rules (2in exit / 6in shared cover) behind
GameSettings.CoverProximityExceptions`; then superproject pointer bump.

### S3 — lobby setting (host set + client sync + UI)

- `Network/Connection/Lobby/LobbyViewModel_Host.cs` — add `_settings_CoverProximityExceptions`
  (`BehaviorSubject<bool>`) + `SetCoverProximityExceptions(bool)` mirroring
  `SetObjectivePlacementMode` (update `_gameSettings`, broadcast `LobbyGameSettingsUpdate` — the
  update message carries the whole `GameSettings`, so the wire format extends for free; old-version
  clients just ignore the unknown field, consistent with the existing #075 posture). Resume path
  already adopts saved settings via `progress?.Settings` — nullable normalization covers old saves.
- `LobbyViewModel_Client.cs` — mirror however the existing settings observables are exposed for
  display (read-only on clients, as today).
- `FdgRaylib/Rendering/LobbyScreen.cs` — host-editable checkbox in the settings panel next to
  Terrain Mode (follow `DrawEnumCombo`/`DrawIntField` host-gating). Label (ASCII only):
  `Cover Proximity Rules` with tooltip
  `House rule: ignore cover hugged by the shooter (<2in) and shared cover at close range (<6in).`
- CLI/headless (`CliApp`) uses `GameSettings.GetDefault()` -> ON automatically; scenario JSONs can set
  the field explicitly through `ScenarioCompiler` (nullable -> absent means ON). Verify, don't assume.

Verify: engine tests green, full `dotnet build`, headless smoke
(`printf "2\n2\n" | dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless` exits 0).
Commits: submodule (viewmodel) first, then superproject (LobbyScreen + pointer).

### S4 — app-side preview truthfulness

Client-side previews evaluate sight lines locally and must agree with the stage or the indicators lie:

- `FdgRaylib/Rendering/Resolvers/GuiDefineMovementResolver.cs:1697` — hypothetical-position aim-line
  preview calls the 3-arg `EvaluateSightLine`. Switch to the context overload (shooter = selected
  model at the hypothetical position, its real base/facing). Needs the effective flag app-side: trace
  how the GUI resolvers/overlay get constructed (`ResolverRegistryFactory.BuildGui`, wired in
  `Program.cs` / `LobbyScreen.HandleLaunch`) and thread the launched game's `GameSettings` (or just
  the one bool) through — **on both host and client paths** (clients hold the synced settings from
  the lobby viewmodel). Read `docs/ResolverGuide.md` first per repo convention.
- `FdgRaylib/Rendering/TacticalOverlay/RulesProbe.cs` — `BestSight`/`EvaluatePip` feed the #162 pips
  ("a pip may never be wrong"): apply the context overload there too (`EvaluatePip` already has the
  shooter model + facing; `BestSight` callers need the shooter's base info added).
- `PolarSightMap` (the field texture) deliberately stays on the raw 3-arg semantics — its whole
  design is a per-source radial map and rule 1/2 depend on both endpoints, which a polar map cannot
  encode. Its doc comment pins "mirrors EvaluateSightLine exactly", which stays true of the base
  overload. **Deferred facet, recorded here and in #162's ledger**: with the house rules ON the field
  may paint cover in spots where a proximity exception would void it; pips/stage remain authoritative.
  Add one sentence to the `PolarSightMap` header comment noting the #201 divergence.

Verify: engine tests + full build + headless smoke; GUI hand-check goes on the awaiting-verification
list (hug a wall and shoot out -> no cover badge on the option card, no +1 in the roll; toggle OFF in
lobby -> old behavior; forest brawl <6" -> no cover).

### Explicitly out of scope (recorded, not silently cut)

- Blocking/sight-blocker proximity (shooter's line clipping a blocking corner at their own muzzle) —
  different mechanic, file separately if it bites in play.
- Field-texture (`PolarSightMap`) proximity awareness — see S4 note, lives with #162.
- Any depth/shoot-through degradation (ruled out above).

## Notes

- 2026-07-21: Ruling received from owner; assessment + S1-S4 plan written (Fable). Branches
  `201-cover-proximity` cut in superproject and submodule. One amendment flagged for sign-off
  (defender also hugging the voided wall keeps cover) — plan implements the ruling as stated unless
  told otherwise.
- 2026-07-09: Filed from playtest feedback. Root cause confirmed by reading `CoverCheckStage` +
  `EvaluateSightLine`; the segment is evaluated as an unordered set of intersections. Deliberately
  **not** fixed in the same pass as the other playtest bugs — the fix is a rules decision, not an
  engineering one, and a naive "exclude terrain containing the attacker" patch would silently pick an
  answer to questions 1-3.
- Existing tests that pin current behavior: `Tests/CoverMajorityTests.cs`, `Tests/BlastCoverRuleIntegrationTests.cs`.
  Both place defenders in cover with attackers in the open, so neither would catch this; new cases must
  put the *attacker* in cover.

## Outcome

(open)
