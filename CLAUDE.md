# FDG Raylib

A Raylib-based client for **Future of Dark Grimness** — a tabletop wargame rules engine. The repository contains two C# .NET 8 projects.

## Git Conventions

- Do not include Claude, AI, or co-author attributions in commit messages. Keep messages brief.
- **Submodule-first commit cadence.** When engine changes are authorized (the `FutureOfDarkGrimness` submodule), commit the submodule first, then bump the superproject submodule pointer together with any app-side changes in a second commit.
- **Verify before committing — never commit red.** Run `dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj` green, and for app-side changes a full `dotnet build`. When a change touches a playable path, also run a headless smoke (`printf "2\n2\n" | dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless`) and confirm it exits 0 with the expected log line.
- **Re-verify assumptions before shared/irreversible operations.** Inspect git state before merging to or pushing a shared branch; if a stated premise turns out false (e.g. "master is synced"), surface it before proceeding rather than pressing on.

## Versioning & Releases

Three independent version axes; keep them straight and never merge them into one number:

- **App version** — plain SemVer (`MAJOR.MINOR.PATCH`), the single source of truth in `Directory.Build.props` `<Version>`. Drives `AssemblyVersion`/`FileVersion`/`InformationalVersion` for **both** projects (the props file covers the submodule too). Pre-1.0 while breaking changes are still expected. Bump this one line to rev the app.
- **OPR rules version** — the OnePageRules ruleset the build implements, `RULES_VERSION` (e.g. `OPR_3_5_1`), a top-of-file env in both `scripts/build-dist.sh` and `.github/workflows/release.yml`. Bump when rebased onto a new OPR ruleset, independently of the app version.
- **Git tag** — `v<app-version>` (e.g. `v0.2.0`). Kept clean SemVer (sortable, tooling-friendly); the OPR version deliberately stays **out** of the tag. Kept in sync with `<Version>` by hand — CI fails the release if `vX.Y.Z` != props `<Version>`.

Both numbers surface together everywhere a human sees a release, never in the tag: release **title** `Future of Dark Grimness v0.2.0 (OPR 3.5.1)`, **archive names** `FdgRaylib-<rid>-v0.2.0-OPR_3_5_1.{zip,tar.gz}`, and binary **ProductVersion** `0.2.0+opr.3.5.1.git-<sha>-<date>` (OPR + commit as SemVer build metadata after the `+`; local/IDE builds report `<Version>-dev`).

**Cutting a release:** set `<Version>` (+ `RULES_VERSION` if the ruleset changed), then `git tag v<Version> && git push --tags`. The `v*` tag triggers `release.yml`, which runs `build-dist.sh` on CI and publishes the GitHub Release with all four platform archives + `SHA256SUMS.txt`. Releases come from **CI, not a local build** (SignPath requires verifiable automated builds); the local script is for dev/testing. Signing is wired into the same workflow but disabled until SignPath accepts the project — the first release is unsigned by design and is itself the application evidence.

## Working Conventions

- **One vertical slice at a time.** Implement -> add an integration test mirroring the nearest existing `*RuleIntegrationTests` -> verify (above) -> commit -> update the canonical running record (the work item's dated notes / partial-facet ledger). Don't batch unrelated facets into a single change.
- **Never silently cut scope.** When deferring a facet or edge case, say so explicitly and record it in the canonical ledger at the same time — don't drop it quietly.
- **Surface design forks before building anything non-trivial.** Present the options with tradeoffs and a recommendation, and get sign-off before committing to UI or architecture decisions.
- **Game text is ASCII-only.** The ImGui font atlas bakes only Basic Latin + Latin-1 glyphs, so anything beyond U+00FF (em/en dashes, arrows, ellipsis, `<=`-style symbols as single glyphs, accented letters) renders as `?` in-game. No such characters in any user-facing string: log lines, banners, request instructions/labels, UI text, rule/spell descriptions, or book/army data. Use `-`, `->`, `...`, `<=`, `>=`, `x` instead. `OprBookImporter.AsciiFold` scrubs imported OPR text; hand-authored strings must be born ASCII. (Comments and docs are exempt.)

## Work Items

Long-running engineering tasks are tracked outside this file to keep the context budget tight:

- `WorkItemsList.md` (repo root) — index of **open** work only; read it when starting work-item tasks. **Keep entries to <=3 lines** (number, title, one-sentence scope/status, link) — running notes, commit hashes, and test tallies belong in the detail file, never the index.
- `WorkItems/NNN-slug.md` — per-item working memory: goal, dated notes (newest on top), decisions, outcome. Created when work starts. Template: `WorkItems/README.md`.
- `WorkItems/Archive.md` — completed/closed items, moved out of the index. When finishing an item: write its Outcome in the detail file, tick the index line, move it to the archive.
- `WorkItems/Reconciliations.md` — number-collision log. Numbers are permanent and never reused. **`git fetch origin` BEFORE filing a number and take it from `origin/master`'s index + archive, not your local copy** — the number gets copied into source comments, tests, filenames and docs immediately, so a collision costs a renumber across dozens of references (see reconciliations 39 and 40, two sessions that made the same mistake days apart). Then read the log; on a collision the unmerged local item yields. A per-clone pre-push hook (`.git/hooks/pre-push`, not version-controlled — install snippet in `WorkItems/README.md`) blocks duplicate numbers across index + archive, but only at push time, which is far too late.

This file-based system is for durable, cross-session tracking. The built-in Task tool is still the right place for in-session ad-hoc todos.

## Projects

| Project | Type | Purpose |
|---------|------|---------|
| `FutureOfDarkGrimness` | Class library | Game engine: rules, state machine, unit/model data, stage resolution, networking |
| `FdgRaylib` | Console exe | Application layer: Raylib + ImGui front end, screens (menu/lobby/army builder), CLI + GUI input resolvers |

`FutureOfDarkGrimness` is a **git submodule**, and it is usually where the proper fix belongs — prefer the engine-side change over a client-side workaround when the engine is the real home for the behavior. No need to ask first; follow the submodule-first commit cadence above and verify with the engine suite.

## Build & Run

```bash
# Build everything
dotnet build

# Run with Raylib window (requires a display)
dotnet run --project FdgRaylib/FdgRaylib.csproj

# For normal play prefer the built binary: `dotnet run` re-runs MSBuild's up-to-date check every
# launch (~2.7s of overhead before the app starts)
./FdgRaylib/bin/Debug/net8.0/FdgRaylib

# Run headless (CLI only, no window — useful for piped/automated play)
dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless

# Pipe empty stdin to auto-resolve everything via EOF defaults
printf "2\n2\n" | dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless

# Slow mode: pause N ms before each resolver call (default 1500ms if no value given)
dotnet run --project FdgRaylib/FdgRaylib.csproj -- --slow 2000

# Rule tracing: narrate every rule hook evaluation (fired / condition failed / suppressed) via
# the Debug log channel. In the GUI the console's Debug toggle flips the same switch at runtime.
dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless --trace-rules

# Scenario tools (#167, see Scenarios/README.md): compile a scenario JSON to a resumable save,
# or launch one directly - no main menu, no lobby (slot 0 = you, other slots AI). Works headless too.
dotnet run --project FdgRaylib/FdgRaylib.csproj -- --make-scenario Scenarios/example-shootout.json out.fdgsave
dotnet run --project FdgRaylib/FdgRaylib.csproj -- --scenario Scenarios/example-shootout.json

# Run engine tests
dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj
```

## Application Flow

Two top-level modes determined in `Program.cs`:

**Headless (`--headless`)** — `CliApp.Prepare()` then `CliApp.RunAsync()`. No screens, no Raylib window. Stage requests resolved via stdin/stdout (CLI resolvers).

**GUI (default)** — `RaylibRenderer.Run()` blocks the main thread. Screen stack starts at `MainMenuScreen` and navigates via `renderer.NavigateTo(IAppScreen)`:

```
MainMenu -+-> HostModal ----> LobbyScreen --> (in-game)
          +-> ClientModal --> LobbyScreen --> (in-game)
          +-> ArmyBuilder / ArmyForge
          +-> Quit
```

Each screen is an `IAppScreen`; `Program.cs` wires the navigation callbacks — that's where the screen graph lives. The game itself only starts when `LobbyScreen.HandleLaunch` fires (after the host clicks LAUNCH, on both host and client). Until then no `IFDGGame` exists.

## Threading

- **GUI mode**: Raylib + ImGui own the main thread. The game engine runs on whatever thread the network/lobby kicks it off on (usually a background `Task`). Resolvers' `Resolve()` methods are called from the engine thread; their `Draw()` methods are called from the main thread. **`_request` and `_tcs` fields must be guarded by a lock.**
- **Headless mode**: `CliApp.RunAsync()` runs on the main thread (no Raylib). Resolvers read stdin synchronously.

## Stage Resolver Pattern

The engine sends `IStageTaskRequest<TResult>` objects through the message bus whenever it needs a player decision; resolvers implement `IStageResolver<TRequest, TResult>`, registered in a `StageResolverRegistry`. There are **two parallel resolver sets** — CLI (`FdgRaylib/Cli/Resolvers/`, stdin/stdout, EOF-safe defaults) and GUI (`FdgRaylib/Rendering/Resolvers/`, ImGui dialogs + canvas overlays); every request type has both. `ResolverRegistryFactory.Build(tableState)` builds the headless registry; `BuildGui(tableState)` returns `(registry, GuiResolverOverlay)`.

**Before touching resolvers, movement, or deployment code, read `docs/ResolverGuide.md`** — GUI overlay architecture, per-request resolver inventory, and the validation gotchas (float-precision margins, the `null` Back-sentinel, deployment spacing) that repeatedly cause bugs when missed.

## Engine reference

`docs/EngineNotes.md` holds the deeper map: networking/lobby wiring, engine concepts (`ITableState`/`IModel`/`DataBinding`), renderer internals, game termination, known engine stubs, and the key-files tree. Two invariants worth repeating here:

- **The engine has real gaps** — never assume a rule is enforced because a stage exists. Check "Known stubs" in `docs/EngineNotes.md` before relying on engine behavior.
- **Objectives decide the winner** — a player can win with all their models eliminated. Never use unit counts as a win condition.

## Army Files

Army lists use the `.fdgarmy` extension (JSON, with `TypeNameHandling.Auto`). The CLI prompts for a file path; EOF falls back to a built-in two-unit test army (5x Warriors with rifles + 3x Heavy Gunners with heavy rifles). The Army Builder screen edits these files via `TinyDialogs` save/load dialogs; the Army Forge screen builds them from bundled faction books (`FdgRaylib/Assets/Books/`).
