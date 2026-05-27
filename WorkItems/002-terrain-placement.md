# 002 — Terrain placement workflow

**Status**: done
**Related**: branch `TerrainPlacement` (parent + submodule); spiritual sequel to #001 (objective placement)

## Goal

Replace `PlaceTerrainStage`'s hardcoded `BuildTestLayout` with a real
terrain-setup phase. Three placement modes chosen in the lobby:

1. **AutoFromLayout** — server places the existing hardcoded layout
   end-to-end. Current behavior; preserves headless / piped runs.
2. **Alternating** — roll-off winner places first, then players take
   turns picking one piece from a shared template pool and placing it.
   A piece-count counter ticks down from N (lobby-set). Duplicates
   allowed (a player can place 8 Wall Segments out of their 20 if they
   want). Phase ends when the counter hits 0.
3. **LoadFromFile** — host points at a `.json` `TerrainLayoutFile` and
   the server places its contents verbatim. No interactive placement.

Lobby sub-options appear/disappear based on the selected mode:

```
Terrain Placement: [AutoFromLayout / Alternating / LoadFromFile]
  └─ Alternating selected →   Terrain Piece Count: [20]   (1..30)
  └─ LoadFromFile selected →  Layout File: [browse...]    *.json
```

Pressing LAUNCH with an invalid configuration (e.g. LoadFromFile mode
with no file specified, or path that doesn't exist / fails to
deserialize) shows an inline error under the LAUNCH button and
refuses to launch. Mechanism already exists at the data layer
(`ILobbyViewModel.TryLaunchGame(out string? failReason)`); the
LobbyScreen currently `Console.WriteLine`s the failure, which we'll
upgrade to a visible error.

Done = on `TerrainPlacement` branch, both repos, AI + human players
can place terrain interactively in `Alternating` mode end to end, the
two non-interactive modes keep working, all 101 engine tests still
pass, and a piped headless run (`printf … | dotnet run -- --headless`)
still completes without prompting.

## Rules summary (GF Beginner's Guide v3.5.1, p.12 — Advanced Terrain
verbatim where relevant)

> "Once you have chosen which terrain pieces you are going to use,
> you can either have one player set up all of the terrain, or have
> both players set up terrain together."

> "To make sure neither player has an advantage, you can roll-off,
> and then alternate in placing one terrain piece each, starting
> with the player that won the roll-off."

> "When setting up terrain, you should use at least 15-20 pieces of
> terrain, although using more can be more interesting."

> "Small pieces of scatter terrain… 1"x1" and 3"x3". Large terrain
> features… 4"x4" and 8"x8", but can be as large as 12"x12"."

Balance guidelines (NOT enforced by code in v1; surfaced as optional
soft warnings later if at all): ≥50% blocking LoS, ≥33% cover, ≥33%
difficult, each player picks 1 dangerous piece; no gaps >12" between
pieces; ideally ≥6" gaps for large units.

The basic rules (p.5) explicitly say "no specific rules on how you
should place terrain" — placement procedure is informal in the
rulebook. The three modes above cover the three procedures the
rulebook does mention (one-player, both-together, alternating); v1
ships Alternating + the two non-interactive shortcuts.

## Current gap analysis (2026-05-17)

Already in place:

- `MapSetupStage` is a `ParentStage<IGameContext, IMapSetupContext>`
  (built during #001). `IMapSetupContext` carries placement state
  across sibling stages.
- `RollForFirstTerrainPlacementStage` exists and rolls off two teams,
  but **discards the winner** — no field on `IMapSetupContext` for it.
- `TeamPlayerAlternationCursor` (built during #001) is reusable for
  the alternation loop. Same pattern as `PlaceObjectivesStage`.
- `GameSettings.TerrainPieceCount` already exists (default 12), is
  lobby-editable, and is network-synced via `LobbyGameSettingsUpdate`.
  **Currently unused by the engine** — it's just dead settings UI. We
  repurpose it.
- `TerrainLayoutFile` + `TerrainPieceEntry` already exist (SaveLoad).
  Suitable for both the template pool (Alternating) and the
  fully-baked layout (LoadFromFile / AutoFromLayout). Same JSON format
  in both cases.
- `TerrainData` + `ETerrainType` flags + `RectangularZone` +
  `CircularZone` cover everything we need to render and validate. No
  rotation primitive — see Decisions.
- `ITableState.Terrain` is already observable; `RaylibRenderer` is
  ready to draw new terrain as it's created. Same code path as the
  current hardcoded layout.
- `TryLaunchGame(out string? failReason)` already exists on both host
  and client lobby viewmodels; LobbyScreen has the call site but
  currently only `Console.WriteLine`s on failure. We upgrade the UI.

Missing:

- `ETerrainPlacementMode` enum (AutoFromLayout / Alternating /
  LoadFromFile) on `GameSettings`. The two file paths
  (`TerrainPoolPath` for Alternating, `TerrainLayoutPath` for
  LoadFromFile) — see Decisions for whether these are one field or
  two.
- `RollForFirstTerrainPlacementStage` must write its winner to
  `IMapSetupContext.TerrainPlacementTeamOrder` (cycling order starting
  with the winner). New context field.
- `PlaceTerrainStage` must branch on `Settings.TerrainPlacementMode`:
  - `AutoFromLayout` → current `BuildTestLayout` behavior, unchanged
    code path.
  - `LoadFromFile` → deserialize `TerrainLayoutPath` and create pieces
    verbatim. Failure here is reported during launch, not stage entry
    (validated by `TryLaunchGame` before the game even starts).
  - `Alternating` → become a `ParentStage<IMapSetupContext,
    ITerrainPlacementTurnContext>` that loops:
    `DetermineNextTerrainPlacerStage` → `PlaceOneTerrainPieceStage`,
    until counter hits 0.
- `PlaceOneTerrainRequest` (request + result types):
  - Carries: placer ID, placements remaining, table bounds, existing
    terrain (for overlap check), the template pool (for the UI).
  - Result: a chosen template + a `Position`. (No rotation in v1 —
    see #044 follow-up.)
- `TerrainPlacementValidator` (pure function, v1):
  - Footprint fully inside the table.
  - Footprint does not overlap any existing terrain piece (strict
    no-overlap policy — see Decisions).
  - Returns `Valid / OutOfBounds / OverlapsExistingTerrain`.
- CLI resolver: list pool indices + dimensions, parse
  `<pool_index> <x>,<z>`. EOF → auto-place this piece via a fallback
  strategy (so piped headless smoke tests keep working even if a
  user ever selects Alternating mode with stdin EOF).
- GUI resolver (`GuiPlaceTerrainResolver : IGuiResolver,
  IGuiCanvasOverlay`):
  - Right-side panel lists templates from the pool, one button per
    distinct template, each showing type flags + dimensions + a tint
    color. **Buttons do NOT decrement** as the user places — the same
    Wall Segment button stays clickable forever. What decrements is
    the global "placements remaining" counter at the top of the panel.
  - After click, the chosen template renders as a translucent ghost
    that follows the cursor. Green outline = legal, red = illegal
    (overlap or off-table).
  - Click on the table commits-pending; Confirm/Cancel panel +
    Enter/Esc shortcuts (per #001 precedent).
  - Esc on the ghost returns to template selection.
- AI resolver (`AiPlaceTerrainResolver`):
  - v1 strategy: random template choice + random legal position
    (rejection-sample against the validator until valid, with a cap).
  - Deliberately dumb — same philosophy as #001's AI objective
    placer. Will not produce balanced or interesting layouts; that's
    not the goal at this stage.
- Lobby UI: combo for `TerrainPlacementMode` + conditional sub-fields
  (terrain count slider when Alternating; file path picker when
  LoadFromFile). The existing terrain-count field at LobbyScreen.cs:266
  becomes one of those conditional sub-fields.
- Lobby launch-error surface: replace the `Console.WriteLine` at
  LobbyScreen.cs:283 with an inline red label drawn under the LAUNCH
  button.
- `LobbyViewModel_Host.TryLaunchGame` extended to validate terrain
  settings (mode set, file exists + parses if LoadFromFile, count in
  1..30 if Alternating) and return a human-readable `failReason`.

The built-in test layout (`BuildTestLayout` currently in
`PlaceTerrainStage`) gets externalized as a checked-in
`Assets/Terrain/default-pool.json` (or wherever assets live — see
Decisions) and loaded by `AutoFromLayout` mode. Defining the pool as
data instead of code is also what `Alternating` mode needs (its pool
of templates) and what `LoadFromFile` consumes, so all three modes
read the same on-disk format.

## Subtasks

1. **Engine: settings + context plumbing**
   - Add `ETerrainPlacementMode { AutoFromLayout, Alternating,
     LoadFromFile }` and the path field(s) to `GameSettings`.
   - Extend `LobbyGameSettingsUpdate` round trip + host & client
     viewmodels to include the new fields.
   - Add `TerrainPlacementTeamOrder` field to `IMapSetupContext`;
     populate from `RollForFirstTerrainPlacementStage`.

2. **Engine: pure validator**
   - `TerrainPlacementValidator.Check(pool, candidate, position,
     existing, tableBounds) → TerrainPlacementValidity`.
   - Footprint vs. table bounds: AABB for rectangles, circle vs.
     rectangle for circles. Pure; testable in isolation.
   - Footprint vs. existing terrain overlap: shape-vs-shape — rect/
     rect, rect/circle, circle/circle. **Strict no-overlap** (incl.
     touching counts as overlap — keep ≥0.01" margin to be safe with
     float noise). See Decisions for rationale.

3. **Engine: request + result types**
   - `PlaceOneTerrainRequest` (placer ID, remaining count, table
     bounds, pool snapshot, existing terrain snapshot) +
     `PlaceOneTerrainResult` (template index, position).
   - Same shape as `PlaceObjectiveRequest` — copy the patterns
     ruthlessly.

4. **Engine: refactor `PlaceTerrainStage` into a parent stage when in
   `Alternating` mode**
   - Children: `DetermineNextTerrainPlacerStage` (advances cursor) +
     `PlaceOneTerrainPieceStage` (emits request, validates, creates
     `TerrainData`, decrements counter).
   - `AutoFromLayout` and `LoadFromFile` branches stay in the parent
     stage's `Enter` (no children, immediate completion). Need to
     check whether `ParentStage` allows zero-child fast-paths or if
     this needs to be three sibling top-level stages selected by a
     small dispatcher — see Decisions.

5. **Engine: externalize the built-in pool**
   - Move the contents of `BuildTestLayout` into a checked-in
     `default-terrain-pool.json` shipped with the build. `AutoFromLayout`
     loads from it.
   - The same JSON also serves as the template source for
     `Alternating` mode (each `TerrainPieceEntry` becomes a template
     in the pool).

6. **CLI resolver** (`PlaceOneTerrainResolver`)
   - List templates with index + type/dims; parse
     `<index> <x>,<z>`; EOF → auto-pick first legal template at
     a deterministic position so piped tests don't break.

7. **GUI resolver** (`GuiPlaceTerrainResolver`)
   - Right-side panel: template buttons + remaining-count header.
   - Ghost preview on the canvas; green/red outline; click → freeze
     pending; Confirm/Cancel; Enter/Esc shortcuts.
   - Per #001, draw via `ImGui.GetBackgroundDrawList()` so shapes
     land above the canvas but below ImGui windows.

8. **AI resolver** (`AiPlaceTerrainResolver`)
   - Random template + rejection sample for position. Cap at, say,
     200 attempts; if none legal, place at table center (should be
     impossible in practice for any sane pool size).
   - Same dispatcher pattern as `PlaceObjectiveRequest` —
     `PlaceOneTerrainRequest.PlacerPlayerID` field + a registry-level
     dispatcher that picks human vs AI based on `EPlayerType`.

9. **Lobby UI**
   - `ETerrainPlacementMode` combo in the settings panel.
   - Conditional sub-fields below it (existing Terrain Count field
     moves under Alternating; file picker appears under LoadFromFile).
   - Use `TinyDialogs.OpenFileDialog` per the existing army-load
     pattern at `LobbyScreen.cs:299`.

10. **Launch validation surface**
    - In `LobbyViewModel_Host.TryLaunchGame`: validate (Alternating
      → count in 1..30; LoadFromFile → file exists + parses).
    - In `LobbyScreen.DrawLaunch`: replace the
      `Console.WriteLine("Launch failed: …")` at line 283 with a
      stored `_lastLaunchError` string drawn inline under the LAUNCH
      button. Clear on next valid launch attempt.

11. **Tests**
    - `TerrainPlacementValidatorTests` covering each constraint
      (out-of-bounds in each direction, rect/rect overlap, rect/
      circle overlap, circle/circle overlap, valid placements).
    - Integration test for `PlaceTerrainStage` in `Alternating` mode
      with a scripted resolver: alternation order correct, counter
      decrements to 0, terrain pieces show up on the table.

## Decisions

- **Three modes, not two**: AutoFromLayout is preserved instead of
  being collapsed into LoadFromFile with a default path. Rationale:
  AutoFromLayout is what `--headless` and CI piped runs use today,
  and conflating "we're running a smoke test" with "the user picked
  a file" muddies the headless default. Keeping it explicit means
  the headless default in `CliApp` stays `AutoFromLayout` without
  reaching into the filesystem.

- **Both `Alternating` and `AutoFromLayout` read the same file
  format**: for now, hardcoded path to `default-terrain-pool.json`
  shipped with the build, *not* user-configurable. Per user, the
  selectable-pool feature is a separate work item (#044). This
  means `GameSettings` adds just one *user-set* string field
  (`TerrainLayoutPath`, used only by LoadFromFile mode) — the
  built-in pool path is a constant in the engine.

- **No rotation in v1**: rectangles are axis-aligned, circles are
  rotation-invariant. `RectangularZone` has no angle field; adding
  one cascades through movement / LoS / overlap / save-load code.
  Per user, deferred to #045. Acceptable consequence: a few of the
  default-pool pieces look samey when placed; users can work around
  by placing several at different positions.

- **Strict no-overlap policy** (per user): any footprint
  intersection rejects the placement. Touching pieces also count as
  overlapping (≥0.01" gap required) so float drift can't sneak two
  pieces into a "merged" state. Rationale: simpler validator,
  simpler validator tests, and downstream movement / LoS code
  already handles disjoint terrain without surprises. We can relax
  later if play feel demands it.

- **Hard cap on Alternating piece count: 30** (per user). Lobby
  slider clamps to 1..30; lower bound enforced server-side so a
  hand-rolled `LobbyGameSettingsUpdate` can't slip a 0 through.
  Rationale: rulebook recommends 15-20; ≥30 makes alternating
  placement tedious and the table cramped.

- **Pool template buttons do NOT decrement** as the user places
  (per user). The pool defines *types of pieces available*, like a
  hobby-store terrain bin. The only counter visible to the player
  is "placements remaining" at the top of the panel. A user can
  place all 20 pieces as Wall Segments if they want.

- **Active-player-only selection** (per user): when it's player
  N's turn, only player N sees the template panel populated and can
  click a piece; the other player(s) see a "Waiting for Player N to
  place terrain" indicator. Same pattern as #001's objective
  placement — implemented for free by the resolver registry routing.

- **Built-in pool location**: ship the JSON in the parent repo
  under `FdgRaylib/Assets/Terrain/default-pool.json` (parent, not
  submodule — assets are application-layer, not engine-layer).
  Engine reads via a path that's set by the application before
  game start (host viewmodel resolves the path and stuffs it into
  `GameSettings.TerrainBuiltinPoolPath` or similar). This keeps the
  engine submodule from depending on application file layout. See
  follow-up #044 for first-class pool selection.

- **Validation timing for LoadFromFile**: validate at `TryLaunchGame`
  time, not stage-entry time. By the time the engine boots, the
  path is trusted. Rationale: a launch failure in the lobby is
  recoverable; a failure once the engine is running 6 layers deep
  in a state machine is not, and the user would see no useful
  message in the GUI.

- **`ParentStage` zero-child fast-path**: needs verification — if
  `ParentStage<TParent, TChild>` requires ≥1 child, the
  AutoFromLayout / LoadFromFile branches won't fit cleanly inside
  `PlaceTerrainStage` itself. Fallback: introduce a small
  `SelectTerrainModeStage` dispatcher above `PlaceTerrainStage` that
  routes to one of three sibling stages
  (`PlaceTerrainAutoStage` / `PlaceTerrainFromFileStage` /
  `PlaceTerrainAlternatingStage`, the last being the only parent
  stage). Decide in Subtask 4.

- **Existing `GameSettings.TerrainPieceCount`** (currently unused):
  repurpose for `Alternating` mode's piece count. No new field
  needed; just gate its use behind `TerrainPlacementMode ==
  Alternating` in `PlaceTerrainStage`, and conditionally show it in
  the lobby UI.

- **`Console.WriteLine` → inline error**: store the last failure
  reason in a `LobbyScreen` field (or in the viewmodel — TBD); draw
  in red under the LAUNCH button. Clear when settings change. Same
  treatment for client-side launch failures (rare; mostly a host
  concern).

## Notes

- 2026-05-24: Merged to master on both repos. End-to-end working in
  GUI (verified visually: lobby mode selector + conditional slider/file
  picker, R-key rotation rotates the ghost 45° with live-validated
  outline color, thumbnails show to-scale shape previews including
  composites). Pre-existing movement validation gap (ignores model
  base radius) surfaced and spun off as #046.

- 2026-05-24: **45° rotation** shipped (closes #045 inline; that work
  item is being marked done rather than left as a follow-up).
  - New `RotatedZoneWrapper(IZone inner, float angleDegrees, Float2 pivot)`
    delegates `IsPointWithinZone` / `DoesPathIntersectZone` to the inner
    after inverse-rotating the query — so movement + LoS get rotation
    transparently with no changes there (both go through interface
    methods, not type switches).
  - `ZoneExtensions.Primitives` walker flattens rotated composites into
    a uniform leaf set: `RectangularZone`, `CircularZone` (rotation of
    a circle off-pivot collapses to a translated circle since circles
    are rotation-invariant), and `RotatedZoneWrapper<RectangularZone>`
    (the OBB primitive). Nested wrappers compose by adding angles and
    rotating pivots.
  - Validator's `PrimitiveOverlaps` extended with SAT for OBB×OBB and
    OBB×AABB (AABB treated as 0°-rotated OBB) and OBB×Circle (transform
    circle center into OBB local frame, then rect-circle distance).
  - GUI: `_rotationDegrees` state in resolver, R increments by 45° in
    both AwaitingClick and AwaitingConfirm; resets to 0° on template
    change; info panel shows rotation + hint. Renderer adds rotated-rect
    cases in both Raylib (`DrawTriangle ×2`) and ImGui (`AddQuadFilled`
    + `AddQuad`) flavors.
  - AI picks random angle ∈ {0, 45, 90, …, 315}. CLI input syntax now
    `<idx> <x>,<z> [rotation_deg]`.
  - `TerrainPlacementResult` gained `RotationDegrees`; engine applies
    `Rotate(template, angle)` → `TranslateToCenter` before placing.
  - 7 new OBB validator tests including "AABB overlaps but OBB doesn't"
    (catches lazy-AABB shortcuts). 139 engine tests total, all green.

- 2026-05-24: **Composite zones + L-shape buildings** shipped (the user
  proposed the composite-zone design; see Decisions below for the
  semantics agreed: union for membership, decompose-to-primitives for
  overlap, sub-zones invisible at the table-state level).
  - New `CompositeZone(IReadOnlyList<IZone> parts)` with union
    semantics for `IsPointWithinZone` / `DoesPathIntersectZone`.
    Sub-zones don't exist as independent `ITerrain` entries; the
    composite is the only thing the game world sees.
  - `ZoneExtensions.Primitives` + `GetAABB` + `GetAABBCenter` helpers
    replace ad-hoc type-switching across the codebase (validator,
    AI resolver, GUI thumbnail, CLI describe).
  - Validator's overlap math now decomposes to primitives and runs the
    Cartesian product — composite overlap "just works" with the
    existing rect/rect, rect/circle, circle/circle cases. 4 new tests
    (rect-in-L-notch valid, rect-overlapping-L-bar rejected, etc.).
  - Renderer (`ZoneRenderer`) gained composite cases for both Raylib
    and ImGui draw paths.
  - GUI thumbnail picker now iterates `Primitives()` and draws each
    in its proper relative position with a single shared `ppi` so
    L-shapes look like L-shapes.
  - Pool changes: dedup'd forest/sandbag duplicates; forest changed
    from circle to 6×6 rect; cover tint changed green → brown per
    user; added small/tall plain buildings + small/large L-shaped
    buildings (all Blocking | Impassible).

- 2026-05-24: **Zero-piece skip**. If
  `Settings.TerrainPlacementMode == Alternating && TerrainPieceCount == 0`,
  both `RollForFirstTerrainPlacementStage` and `PlaceTerrainStage`
  early-return via the shared `PlaceTerrainStage.ShouldSkipTerrainPhase`
  predicate — they log a "skipping" line and activate their
  done-bindings without rolling or placing. Lobby slider min lowered
  to 0; validator accepts 0.

- 2026-05-24: **Template-picker thumbnails**. Replaced text-only
  buttons with `InvisibleButton` rows + manual draw (hover-aware
  background fill). All thumbnails share one `ppi` derived from the
  pool's largest piece, so relative sizes read correctly.

- 2026-05-17: Subtasks 2–10 shipped on branch `TerrainPlacement`. End
  to end working in headless (`printf "2\n2\n" | dotnet run -- --headless`
  completes; verified terrain placement runs through all three modes
  without crashes; AI vs AI alternating placed 8 pieces successfully).
  Visual UI (GUI resolver + lobby conditional fields + inline launch
  error) is **untested** — no display available in this session.

  Shipped:
  - **#2 Validator** (`TerrainPlacementValidator`): pure function
    checking bounds + overlap with 0.01" margin. 14 unit tests in
    `TerrainPlacementValidatorTests.cs`. Supports rect/rect,
    circle/circle, rect/circle, circle/rect.
  - **#3 Request types** (`PlaceOneTerrainRequest`,
    `TerrainPlacementResult`). Pool sent in the request; existing
    terrain read live from `TableState`.
  - **#4 Stage refactor**: `PlaceTerrainStage` is now mode-aware. Auto
    + LoadFromFile dump pieces verbatim; Alternating loops with
    `TeamPlayerAlternationCursor` + re-prompt on invalid placement.
    Templates translated via `TerrainTemplateUtilities.TranslateToCenter`.
  - **#5 Pool externalization**: built-in pool moved into
    `DefaultTerrainPool` (still code, not JSON — JSON path deferred
    to #044). Same shape data as the old `BuildTestLayout`.
  - **#6 CLI resolver** (`PlaceOneTerrainResolver`): stdin parse +
    EOF-defaulted grid search so piped headless runs progress.
  - **#7 GUI resolver** (`GuiPlaceOneTerrainResolver`): three-state UI
    (template picker → cursor ghost → frozen confirm). Uses
    `ZoneRenderer.DrawFilled` for shape rendering. **Visual review
    pending** — no display in this session.
  - **#8 AI resolver** (`AiPlaceOneTerrainResolver`): random template
    + rejection-sample position with grid fallback. Deliberately
    dumb. Registered in `AiResolverRegistryFactory.BuildSoloRules`.
  - **#9 Lobby UI**: `Terrain Mode` combo + conditional sub-controls
    (slider 1–30 for Alternating, file picker for LoadFromFile).
    `Terrain Count` row replaced by the conditional slider.
  - **#10 Launch validation**: `LobbyViewModel_Host.TryLaunchGame`
    rejects Alternating count outside 1–30 and LoadFromFile with a
    missing / unparseable path; LobbyScreen surfaces the failure
    inline (red text) below the LAUNCH button.
  - Incidental: flipped `AutoPlaceObjectivesDebug = true` for headless
    in `CliApp` so the piped smoke test #001 promised actually works.

  Deferred / out of scope:
  - **#11 Integration test** for `PlaceTerrainStage` in Alternating
    mode (mirrors #001's deferred subtask 8). Validator + the headless
    end-to-end smoke test cover the basics.
  - **Visual review** of the GUI resolver and lobby conditional layout
    — needs a display to verify.
  - JSON-asset externalization of the built-in pool — tracked under
    #044.
  - Terrain rotation — tracked under #045.

- 2026-05-17: Subtask 1 shipped on submodule branch
  `TerrainPlacement`. Engine settings + context plumbing in place:
  - `ETerrainPlacementMode { AutoFromLayout, Alternating, LoadFromFile }`
    + `TerrainPlacementMode` + `TerrainLayoutPath` fields on
    `GameSettings`. Default count bumped to 20 (rulebook recommended
    midpoint); was 12.
  - `IMapSetupContext.TerrainPlacementTeamOrder` +
    `SetTerrainPlacementTeamOrder`, mirroring the objective-placement
    pair. `RollForFirstTerrainPlacementStage` now writes the
    alternation order (winner first) instead of discarding the
    roll-off result.
  - `ILobbyViewModel` + host + client viewmodels exposed
    `TerrainPlacementMode` / `TerrainLayoutPath` observables + getters
    + host setters; client setters throw the same "not the host"
    exception as siblings. Client side updates state on
    `LobbyGameSettingsUpdate` receipt.
  - No engine consumer wired yet — `PlaceTerrainStage` still uses
    `BuildTestLayout` regardless of mode. That's Subtask 4. All 114
    engine tests still pass.

- 2026-05-17: Work item created, design agreed with user, branch
  `TerrainPlacement` created on both repos off `origin/master`.
  Found pre-existing assets to reuse:
  - `GameSettings.TerrainPieceCount` already wired through lobby
    UI + network sync but unused by engine. Will repurpose.
  - `TryLaunchGame(out string? failReason)` already returns a
    failure string; LobbyScreen just `Console.WriteLine`s it
    today. Need a UI surface only.
  - Submodule's `origin/TerrainPlacement` branch is fully merged
    into master (no unique commits) — safe to ignore, local branch
    starts fresh from origin/master.
  Next session: start Subtask 1 (engine settings + context
  plumbing).

## Outcome

Shipped on `TerrainPlacement` branch and merged to master on both
repos (parent: `e713c6c`; submodule: `2ba1c5b`).

End-to-end working in GUI and headless. Three placement modes selected
in the lobby:
- `AutoFromLayout` — server places the built-in `DefaultTerrainPool` verbatim
- `LoadFromFile` — server places contents of a user-chosen `.fdgterrain` JSON verbatim
- `Alternating` — roll-off winner places first, players alternate one
  piece each, picking from the pool template panel with thumbnail
  previews; R-key rotates 45°; ghost shows live-validated outline;
  inline launch error if Alternating count or LoadFromFile path is invalid.

Two new `IZone` primitives shipped to support shape variety the
rulebook implies (Blocking + Impassible buildings of various forms):
- `CompositeZone` — union of N sub-zones; sub-zones not exposed as
  independent terrain pieces. Used for L-shaped buildings.
- `RotatedZoneWrapper` — wraps any `IZone` with rotation around a
  pivot. Movement + LoS get rotation transparently via interface
  dispatch. Validator overlap math extended with SAT (OBB×OBB,
  OBB×AABB) and OBB×Circle local-frame transform.

AI resolver places random template at random center with random 45°
rotation (rejection-sample against validator; grid fallback if the
table is crammed). Deliberately dumb — smarter AI is a separate
concern (see "On the AI resolver" discussion in session log if
revisited).

Pre-existing behaviors confirmed working through new shapes:
- Headless AI vs AI in Alternating mode placed N pieces (8 / 9) end
  to end across composites and rotated rects.
- 152/152 engine tests pass after the merge with upstream #018/#019.

Out of scope / spun off:
- **#044** — Multi-pool terrain selection (lobby picker for which
  `TerrainLayoutFile` feeds the built-in pool slot). v1 ships with
  one hardcoded `DefaultTerrainPool`.
- **#045** — Originally scoped as the rotation follow-up; pulled into
  v1 mid-stream and shipped (45° increments via SAT). Marking #045
  done alongside #002.
- **#046** — Movement validation ignores model base radius for terrain
  footprints (zero-width segment vs. swept disc). Pre-existing
  limitation surfaced more often by #002's richer terrain.
- **#11 (subtask)** — Engine-side integration test for the Alternating
  loop. Deferred per #001 precedent.

Files added (engine submodule):
- `TableState/Zones/CompositeZone.cs`
- `TableState/Zones/RotatedZoneWrapper.cs`
- `TableState/Zones/ZoneExtensions.cs`
- `SaveLoad/TerrainLayoutLoader.cs`
- `StageResolution/Requests/PlaceOneTerrainRequest.cs`
- `StateMachine/MapSetupStage/PlaceTerrainStage/TerrainPlacementValidator.cs`
- `StateMachine/MapSetupStage/PlaceTerrainStage/TerrainTemplateUtilities.cs`
- `StateMachine/MapSetupStage/PlaceTerrainStage/DefaultTerrainPool.cs`
- `Ai/Resolvers/AiPlaceOneTerrainResolver.cs`
- `Tests/TerrainPlacementValidatorTests.cs`

Files added (parent):
- `FdgRaylib/Cli/Resolvers/PlaceOneTerrainResolver.cs`
- `FdgRaylib/Rendering/Resolvers/GuiPlaceOneTerrainResolver.cs`

Incidental fix: `CliApp` now sets `AutoPlaceObjectivesDebug = true` in
headless mode, finally honoring the smoke-test promise from #001.
