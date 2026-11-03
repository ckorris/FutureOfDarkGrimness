# 412 — Anonymous usage analytics

**Status**: done (2026-10-28)
**Related**: #271 (list-server Worker, the host), #226 (bug-report drop box, the pattern to mirror),
#310 (UserConfig, where the opt-out and install ID live), #257 (teams), #191 (AI profiles)

## Goal

Chris can answer "how many people open the game, how often, and how are they playing it" from a
terminal, without a new server and without anything a privacy-minded player would object to.
Concretely: launches per day; unique installs over 1/7/30 days and lifetime (persisting through app
upgrades); games started vs finished; the settings games are played with (points, game system,
AI vs multiplayer, AI profiles, factions, terrain/objective/turn/shooting modes, house-rule toggles);
which features get opened. Opt-out is one click in the first-launch popup and remains reachable
later from the main menu and the in-game Options panel. The README's current "no telemetry" promise
is replaced by an accurate paragraph, and the release notes say so.

Done means: Worker slice deployed and prod-smoked, the client sends the events below from a
Release build, `fetch-stats.sh` prints a summary and exports CSV, the popup text (Chris's) and the
README paragraph (drafted by the implementer, reviewed by Chris) are in, engine + app suites green,
headless smoke exit 0.

## Decisions already made (signed off by Chris 2026-10-28)

These are settled. Do not re-open them; record any surprise under Decisions below.

- **Opt-out by default, nothing sent until the player has clicked through the first-launch notice.**
  The notice shows the full list of what is sent, with the "Send anonymous usage statistics" checkbox
  right there (ticked). Opting out later: main-menu Privacy button, and the escape-menu Options panel.
- **No "opted out" event.** Turning the toggle off sends nothing, deletes the local install ID and
  the spool file. Turning it back on generates a fresh ID (the UI says so).
- **Install ID = random UUID (Option A)**, generated locally when the player first leaves analytics
  enabled, stored in `config.json` (per-user config dir, so it survives upgrades - #310 put it there
  for exactly this reason). Never derived from hardware, never stored server-side next to an IP.
  "Reset anonymous ID" button next to the toggle.
- **Never sent:** player name, server name, chat, army list name or contents, file paths, IP (the
  Worker must not persist it), hostname, OS username, hardware IDs, locale, screen size.
  **Sent and fine:** faction name per slot (the Army Forge book faction), game system slug (GDF/AoF),
  points.
- **Host sends game_start / game_end / game_abandon; clients send game_join** carrying the host's
  game ID, so guests show up as uniques and their faction is visible without double-counting games.
- **Stats are read with a script**: `fetch-stats.sh` prints the canned JSON summary and can export
  every event as CSV. No web UI on the Worker.
- **README privacy paragraph and release-note line: implementer drafts, Chris reviews before commit.**
  **Popup text: Chris writes it by hand.** Ship a clearly marked placeholder and STOP to ask him for
  the wording when slice 3 is reached (see "Stop points").
- **Excluded runs** (send nothing): `--headless`, `--scenario` launches, and any build whose
  `AppVersion.Value` contains `-dev`. `FDG_ANALYTICS=1` forces sending from an excluded run (for
  testing against wrangler dev / prod); `FDG_ANALYTICS=0` or a blank `FDG_ANALYTICS_URL` kills it.
- **Storage: a third Durable Object on the existing Worker**, SQLite-backed with a real `events`
  table (unlimited retention, exact counts, free tier, same admin token). Workers Analytics Engine
  was considered and rejected as the primary store (about 90-day retention, sampling); it can be
  added later for dashboards if wanted.

## Event schema

Every event is a JSON object. Common fields (all required unless noted):

| Field | Type | Notes |
|---|---|---|
| `name` | string | one of the allowlisted names below |
| `t` | int | client UTC unix seconds |
| `install` | string | UUID, 36 chars |
| `session` | string | UUID per process launch |
| `game` | string? | UUID per game, present on game_* events only |
| `ver` | string | `AppVersion.Value`, max 64 chars |
| `os` | string | `windows` / `linux` / `macos` / `other` |
| `arch` | string | `x64` / `arm64` / `other` |
| `props` | object | per-event fields, validated against the allowlist below |

Events and their `props` (the Worker rejects any event whose name, prop key, prop type or enum
value is not in this list; strings are ASCII-folded and length-capped at 60 chars unless stated):

- **`app_launch`** - `first_launch` bool (the install ID was created this run); `prev_session_seconds`
  int?; `prev_session_clean` bool? (see Session tracker). Sent once per process, after the notice is
  acknowledged (first run) or on startup (later runs).
- **`game_start`** (host only, from `LobbyScreen.HandleLaunch`) - `points` int (setting);
  `system` enum `grimdark-future|age-of-fantasy|mixed` (from the slots' `ArmyListSummary.GameSystem`);
  `players` int; `humans` int (`EPlayerType.Local` + `Network`); `ais` int; `remote` int
  (`EPlayerType.Network`, so `remote > 0` means multiplayer over the network); `teams` int (distinct
  `ETeamOption` values); `ai_profiles` string[] (enum names, one per AI slot, max 8); `factions`
  string[] (one per slot in slot order, `ArmyListSummary.FactionName`, empty -> `custom`, max 8);
  `slot_points` int[] (`ArmyListSummary.PointCost` per slot, max 8); `listed` bool (host ticked
  List publicly); `resume` bool (`IsResumeMode`); `terrain_mode`, `objective_mode`, `randomness`,
  `turn_style`, `shooting_mode` enum names; `terrain_count` int; `cover_exceptions`,
  `see_through_friendlies`, `unlimited_split_fire` bool.
- **`game_join`** (client only, from the client lobby's launch handler) - `faction` string,
  `system` string, `points` int for this player's own slot. `game` is the ID carried on
  `LaunchGameMessage` (null if an older host sent none; still send the event without `game`).
- **`game_end`** (host only, from `OnGameCompleted`) - `outcome` enum `Win|Tie|Fault` (the
  `EGameOutcome` name); `rounds` int (`RoundsPlayed`); `seconds` int since game_start; `winner`
  enum `human|ai|none` (look the winning PlayerID up in the launch roster).
- **`game_abandon`** (host only; escape menu Return to Main Menu / Quit to Desktop while a game is
  running, and the window being closed mid-game) - `seconds` int; `reason` enum `menu|quit|close`.
  Must not fire after game_end for the same game (gate on a per-game "ended" flag).
- **`feature_open`** - `feature` enum `army_forge|combat_calculator|load_game|bug_report_sent|
  server_browser|host|join`. One event per open; no payload beyond the name.

Batch upload: `POST /events` with a JSON array of 1..20 events, body <= 8 KB, `Content-Type:
application/json`, no auth. Responds 202 with `{accepted: n}`. The client never retries a 4xx.

## Server (tools/list-server)

Mirror the `Reports` class structure exactly (routing in the top-level `fetch`, `requireAdmin`,
`clientIp`, `json`, the caps-that-reject posture). New pieces:

- `wrangler.toml`: `[[durable_objects.bindings]] name = "ANALYTICS" class_name = "Analytics"` and
  `[[migrations]] tag = "v3" new_sqlite_classes = ["Analytics"]`. Single instance via
  `idFromName("global")`, same as the other two.
- `class Analytics` uses the SQLite API (`this.state.storage.sql.exec(...)`), not the KV API.
  Create on first use (idempotent `CREATE TABLE IF NOT EXISTS`):
  ```sql
  events(id INTEGER PRIMARY KEY, received INTEGER NOT NULL, t INTEGER NOT NULL,
         name TEXT NOT NULL, install TEXT NOT NULL, session TEXT NOT NULL, game TEXT,
         ver TEXT NOT NULL, os TEXT NOT NULL, arch TEXT NOT NULL, props TEXT NOT NULL)
  -- indexes: (name, received), (install), (game)
  ```
  `props` is the validated JSON object; summary queries use `json_extract`.
- Validation is an explicit allowlist table in code (name -> {key -> {type, enum?, maxLen?,
  maxItems?}}). Unknown name/key/type/enum -> whole batch rejected 400 with the first reason.
  `t` must be within +-7 days of `received` (else clamp to `received`, don't reject - clocks are
  wrong on real machines). UUID fields must match `^[0-9a-f-]{36}$`.
- Abuse posture: body cap 8 KB (check real byte count); per-IP minimum interval 2 s using the
  existing `RATE_PREFIX` storage pattern, **and prune rate records older than 5 minutes on every
  write** so the "no IPs retained" claim is true (the registry's records are short-lived too, but
  make the analytics path explicitly honest). Row cap `ANALYTICS_MAX_ROWS = 2_000_000`; at the cap
  reject 503, never evict. The IP goes nowhere near the `events` table.
- Admin endpoints (`X-Admin-Token`, fail closed 503 when unset, exactly as reports):
  - `GET /stats?days=30` -> JSON summary: `launches_per_day[]`, `new_installs_per_day[]`,
    `unique_installs {d1, d7, d30, lifetime}`, `sessions {count, avg_seconds, unclean_exit_rate}`,
    `games {started, joined, finished_by_outcome{}, abandoned_by_reason{}, avg_rounds, avg_seconds}`,
    histograms over the window: `points`, `system`, `multiplayer_vs_ai` (remote>0 / ais>0 / both /
    solo-local), `ai_profiles`, `factions`, `terrain_mode`, `objective_mode`, `randomness`,
    `turn_style`, `shooting_mode`, each toggle's true/false split, `features{}`, `versions{}`,
    `os{}`. Keep it one `exec` per section; the DO serializes requests, and this is admin-only.
  - `GET /stats/export?after=<id>&limit=5000&format=csv|json` -> rows in id order (paged by
    `after`), CSV with `props` as a JSON string column. `fetch-stats.sh` pages until empty.
- `fetch-stats.sh <base-url> [--export stats.csv] [--days N]` next to `fetch-reports.sh`, reading
  `ADMIN_TOKEN` from the environment like it does. Output dir `tools/list-server/stats/` gitignored.
- `smoke.sh`: new sections after 14 - accept a valid batch (202), reject unknown event name / bad
  prop type / bad enum / oversize batch / 21 events (400 or 413), rate limit (429), `GET /stats`
  without token (403) and with (200, `unique_installs.lifetime >= 1`), export returns the row.
- `README.md` (tools/list-server): add the three endpoints to the table and a "Fetching stats"
  section.

## Client (FdgRaylib)

New folder `FdgRaylib/Analytics/`:

- `AnalyticsConfig` - mirrors `ListServerConfig`: `FDG_ANALYTICS_URL` env, else `analytics.url`
  file beside the exe, else the list-server base URL (`ListServerConfig.BaseUrl`, same Worker);
  blank/invalid -> disabled. `FDG_ANALYTICS` = `0` disables, `1` forces on in excluded runs.
  `IsExcludedRun` = headless || scenario launch || `AppVersion.Value.Contains("-dev")`.
- `AnalyticsEvent` - record matching the schema; `props` as `Dictionary<string, object>`;
  System.Text.Json like `UserConfig`.
- `Analytics` (static) - `Track(name, props, gameId = null)`; `Enabled` =
  `UserConfig.Current.AnalyticsEnabled && AnalyticsNoticeAcknowledged && config URL != null &&
  (!IsExcludedRun || forced)`. When not enabled, `Track` is a no-op (no queueing). Queue flushed
  by a background task every 30 s and on `Flush()` at clean exit (bounded wait, 2 s), 20 events per
  POST, 5 s HTTP timeout, `HttpClient` shared, never throws, never blocks the caller. Failed sends
  (network, 5xx) append to a spool file `analytics-spool.json` in `UserConfig.Directory`, capped at
  200 events (drop oldest); the spool is sent first on the next launch. 4xx responses drop the batch
  and log once at Debug level. All public members thread-safe (engine thread calls Track; main thread
  calls Flush).
- `SessionTracker` - `last-session.json` in the config dir: `{startedUtc, lastAliveUtc, cleanExit,
  version}`. On launch, read it: if present, derive `prev_session_seconds` (clean: exitUtc - started;
  unclean: lastAlive - started) and `prev_session_clean`, pass them into `app_launch`, then overwrite
  for this session. Heartbeat updates `lastAliveUtc` every 60 s (piggyback on the flush timer). On
  clean exit write `cleanExit = true` with the exit time. "Unclean" means crash OR task-kill; name
  the field `prev_session_clean`, not "crashed".
- `UserConfig` gains `AnalyticsEnabled` (default true), `AnalyticsInstallId` (string?, null until
  generated), `AnalyticsNoticeAcknowledged` (default false). Existing installs therefore see the
  notice on their first launch of this version. Opting out nulls the ID and deletes the spool.
- Main-thread wiring in `Program.cs` GUI path: after `UserConfig.EnsureExists()`, start the session
  tracker; the main menu shows the notice when `!AnalyticsNoticeAcknowledged`; `app_launch` is sent
  from the notice's OK handler on first run and directly at startup afterwards. Hook
  `feature_open` into the existing `MainMenu.On*Clicked` callbacks and the bug reporter's send path.
  On window close / Quit, `Analytics.Flush()` after the session tracker's clean-exit write.
- Game hooks live in `LobbyScreen` (host: `HandleLaunch` -> game_start, `OnGameCompleted` ->
  game_end; client: the `OnLaunched` path -> game_join) and `EscapeMenuOverlay` (`OnReturnToMainMenu`
  / `OnQuitToDesktop` -> game_abandon via a small `GameAnalyticsSession` object that owns the game
  ID, start time and the ended flag). `HostGameSettings.CaptureFrom(viewModel)` already snapshots
  every setting game_start needs; reuse it rather than re-reading the view model field by field.
- AI profile per slot: `LobbyPlayerInfoSummary` carries `PlayerType` but not `AiProfile`;
  `LobbyPlayerInfoFull.AiProfile` has it host-side. Add a host-only read accessor on
  `LobbyViewModel_Host` (engine, submodule) rather than widening the synced summary - no wire change.

### Engine change (submodule, commit first)

`LaunchGameMessage` is an empty class. Add `public string? GameId { get; set; }`; host sets a
new `Guid.NewGuid().ToString()` at both send sites (`LobbyViewModel_Host` lines ~770 and ~921) and
exposes it to the front end (e.g. on `OnLaunched` or a `LastLaunchedGameId` property); client stores
it from `OnLaunchGameMessageReceived` and exposes it the same way. Check the message-bus serializer
tolerates a missing/unknown field (Newtonsoft with TypeNameHandling ignores unknown members by
default) - if it does, **no `NetworkProtocol.Version` bump**: an old host simply yields a null
GameId. If it does not, bump and say so in Decisions. Add an engine test that a round-tripped
message keeps the ID and that a message without one deserializes to null.

## UI (all text ASCII-only, see CLAUDE.md)

- **First-launch notice** (`AnalyticsNotice` ImGui modal, drawn over the main menu, blocks input
  until OK): title, Chris's text (placeholder constant `AnalyticsNotice.Text` = `"[PLACEHOLDER -
  ask Chris]"` until he supplies it), an itemized list generated from the schema (what is sent /
  what is never sent / where it goes: "a Cloudflare Worker run by the maintainer; Cloudflare handles
  your IP like any website, the app's own records do not keep it"), the checkbox "Send anonymous
  usage statistics" (ticked), and OK. OK sets `AnalyticsNoticeAcknowledged = true`,
  `AnalyticsEnabled = checkbox`, saves, generates the install ID if enabled, then sends app_launch.
- **Main menu**: a small "Privacy" button in the footer (below Quit, `back`-styled or muted)
  reopening the same modal in settings mode: checkbox, "Reset anonymous ID" button, "Close". The
  main menu has no Options screen today, so this is the only pre-game place for the toggle.
- **Escape-menu Options panel** (`EscapeMenuOverlay.AttachOptions` area): the same checkbox and
  Reset button, same wording. Changing it mid-game takes effect immediately (a game_end after an
  opt-out is not sent).
- `config.json` stays hand-editable; document the three keys in the README paragraph.

## Delivery slices (one commit each, submodule-first where the engine is touched)

1. **Worker.** `Analytics` DO, allowlist validator, `/events`, `/stats`, `/stats/export`,
   `fetch-stats.sh`, smoke sections, list-server README. Verify: `npx wrangler dev` + `./smoke.sh`
   all sections green. **Deploy only after asking Chris** - the Worker also hosts the live server
   browser and bug-report box, and a bad deploy takes both down. After deploy: prod `smoke.sh`.
2. **Client core.** Config fields, `AnalyticsConfig`, `Analytics`, `SessionTracker`, app_launch,
   exclusions, feature_open on the main-menu callbacks. Tests in `FdgRaylib.Tests/AnalyticsTests.cs`
   using the `FDG_CONFIG_DIR` pattern from `UserConfigTests`: event serialization matches the
   schema; `Track` is a no-op when disabled / unacknowledged / excluded; opt-out nulls the ID and
   deletes the spool; spool caps at 200 and is sent first; session tracker derives clean/unclean
   durations; a fake `HttpMessageHandler` sees 20-event batches and never more than one in flight.
   Verify: `dotnet build`, app suite green, headless smoke exit 0 **and** no analytics request
   attempted (assert via a Debug log line "analytics: disabled (headless)").
   Manual: `FDG_ANALYTICS=1 FDG_ANALYTICS_URL=http://localhost:8787` against wrangler dev, then
   `fetch-stats.sh` shows the launch.
3. **Notice + settings UI.** **Stop point: ask Chris for the popup text before building this
   slice's modal** (see below). Then the modal, the main-menu Privacy button, the Options-panel
   toggle. Verify: GUI hand-check that first launch blocks on the notice, OK with the box unticked
   sends nothing (watch wrangler dev), Reset generates a new ID, escape-menu toggle works.
4. **Game events.** Engine: `LaunchGameMessage.GameId` + host AI-profile accessor + tests (engine
   suite green) -> submodule commit -> app: game_start / game_end / game_abandon / game_join,
   `GameAnalyticsSession`, `bug_report_sent`. Tests: the game_start props built from a fake roster
   match the schema (humans/ais/remote/teams/system `mixed`/factions `custom`); game_abandon is
   suppressed after game_end; game_join carries the host's ID. Verify: two-instance local host +
   client against wrangler dev, `fetch-stats.sh` shows one game_start, one game_join, one game_end.
5. **Docs + close.** Rewrite the README "Privacy" section (draft, **show Chris before committing**),
   add a release-note line, `docs/EngineNotes.md` pointer, write Outcome, tick the index, archive.

## Stop points (do not guess past these)

- **Slice 3, before writing the modal:** tell Chris the slice is reached and ask for the popup
  wording. Show him the auto-generated itemized list so his text can sit above it. Do not invent the
  text; a placeholder may be committed only if he says to carry on without it.
- **Slice 1, before `npx wrangler deploy`:** ask. Local smoke green first.
- **Slice 5, before committing the README paragraph:** show the draft.

## Notes

- 2026-10-28: **First full live game verified on prod, then wiped.** Host vs Tactician, 2000 pts: one
  game_start, one game_end (Win, 4 rounds, 1344s, human), no game_abandon after closing with the window X
  (correctly suppressed), clean-exit marker written, no spool left. Added admin
  `DELETE /stats?confirm=wipe-all-analytics` (local test: refuses without confirm / wrong token), deployed
  (version 2d2525c7), prod smoke passed, prod wiped (4 rows) - lifetime 0. Verify items left: Options
  untick removes the ID; optional two-instance game_join.
- 2026-10-28: **Renumbered 406 -> 412** (collision with the transport item filed upstream; see
  Reconciliations). Rebased onto `origin/master`; commits before this read `406:`.
- 2026-10-28: **Options polish (Chris, from screenshots).** Main-menu Options is now screen-sized (half
  the width, menu font at ~2.6% of screen height, heading inside, scrolls past 90% height). Notes wrap
  instead of running off the edge; slider labels sit above full-width sliders. **"Reset anonymous ID"
  removed** at Chris's request (`AnalyticsClient.ResetInstallId` and its test with it) - opting out and
  back in still mints a fresh ID. Supersedes the reset button in the plan sections above.
- 2026-10-28: **Main-menu Options + saved settings.** Chris asked for an Options button on the main menu
  (the opt-out had become reachable only from inside a game). The panel body moved to `OptionsPanel`,
  drawn by both menus (controls cheat-sheet in game only). All its settings now persist in
  `UserConfig.View` (`SavedViewSettings`): labels, grid, tokens, reach overlay, fireworks, dice popup
  time, master volume - loaded at startup, saved when Options closes and at app exit (catches the
  N/T/V hotkeys). Notice line back to "You can change this any time under Options." 4 new
  UserConfigTests; app suite 3035 green, headless smoke exit 0.
- 2026-10-28: **Worker deployed** (wrangler re-login needed; the old OAuth token had expired in August).
  Prod `smoke.sh` all 19 sections green, including the registry and report sections; smoke cleanup
  left prod stats empty (lifetime 0). README Privacy section committed in Chris's wording. Remaining:
  engine push then superproject push, and the GUI verify list.
- 2026-10-28: **Slice 3 committed** with Chris's notice text (apostrophes checked straight, one trailing
  space stripped). Remaining: README sign-off, Worker deploy + prod smoke, pushes, GUI verify list.
- 2026-10-28: **Slice 3 built, uncommitted (stop point: popup text).** `FdgRaylib/Rendering/PrivacyDialog.cs`
  (notice mode opens by itself until acknowledged; settings mode from a small bottom-right Privacy
  button on the main menu; shared `DrawSettingsControls` + itemized list in the escape-menu Options
  panel under "Privacy"), ASCII-only test over all dialog text. `PrivacyDialog.IntroText` is a marked
  placeholder awaiting Chris's wording. **Slice 5 drafted, uncommitted (stop point: README review):**
  README Privacy section rewritten in the working tree; EngineNotes pointers committed. Release notes
  are hand-written on the GitHub release, so the release-note line is a draft to paste:
  > **Anonymous usage statistics.** The game now sends anonymous, opt-out usage statistics (games
  > played, settings, factions - never names, chat or army contents). A notice on first launch says
  > exactly what is sent and has the off switch; you can also turn it off any time from Privacy on the
  > main menu or Options in a game. Details in the README's Privacy section.

  **Still to do before close:** Chris's notice text; README sign-off; Worker deploy (stop point) +
  prod `smoke.sh`; engine push then superproject push (submodule-first); GUI verify list below.

  **GUI verify list:** (1) fresh `FDG_CONFIG_DIR`: the notice blocks the menu on first launch, OK with the
  box ticked creates an ID and sends app_launch (watch `wrangler dev` or `fetch-stats.sh`), unticked
  sends nothing; (2) Privacy button: untick deletes the ID from config.json, Reset changes it;
  (3) escape-menu Options > Privacy does the same mid-game; (4) host vs a bot to the end: one
  game_start + one game_end, no game_abandon; (5) host + client on one machine: client's game_join
  carries the host's game ID; (6) quit mid-game: one game_abandon with reason quit/close/menu.
  Use `FDG_ANALYTICS=1 FDG_ANALYTICS_URL=http://localhost:8787` - local builds are `-dev` and excluded.
- 2026-10-28: **Slice 4 (game events) done; done before slice 3** because slice 3 waits on Chris's popup
  text and nothing here depends on it. Engine `e3ebc67` (local, not pushed): `LaunchGameMessage.GameId`
  (optional on the wire both ways, no protocol bump - verified by hand-built frames with and without the
  field), `ILobbyViewModel.LaunchedGameId` + `GetAiProfile` (default-null interface members, so fakes
  compile unchanged), host mints the ID at both launch sites before `OnLaunched`; 5 engine tests.
  App: `GameAnalyticsSession` (pure prop builders + per-game ended flag), hooked from
  `LobbyScreen.HandleLaunch` (host game_start / client game_join via `IFDGGame.LocalPlayerIDs`),
  the lobby's `OnGameCompleted` (game_end), `OnGameEnded` (client: mark ended), Program's
  `OnGameExited` (abandon "menu") and the exit `finally` (abandon "quit"/"close" via the new
  `RaylibRenderer.CloseRequested`), escape-menu Send Report (`bug_report_sent`), and
  `LobbyScreen.ListedPublicly` set from the host dialog's listing. 9 tests incl. a contract test that
  every prop key is in the Worker SCHEMA block for its event; abandon-guard mutation caught.
  Engine 3366 green, app 3027 green, headless smoke exit 0. **Not exercised live**: these events only
  fire through the GUI lobby, and driving the GUI needs Chris's say-so - two-instance hand-check is in
  the verify list.
- 2026-10-28: **Slice 2 (client core) done.** `FdgRaylib/Analytics/`: `AnalyticsConfig`, `AnalyticsEvent`
  (+ event/feature name constants), `AnalyticsClient` (gates, queue, 20-event batches, single-flight,
  429/5xx/network -> spool capped at 200, other 4xx dropped, opt-out wipes queue/spool/ID),
  `SessionTracker`, static facade `UsageAnalytics` (renamed from `Analytics` - a class named like its
  namespace breaks call sites). `UserConfig` gained the three fields. Program.cs: initialize before
  `CliApp` (headless/scenario excluded), `feature_open` on Host/Client/Army Forge/Combat Calculator/
  Load Game, `Shutdown()` on both exit paths. `AnalyticsTests` 23 tests incl. a contract test that
  every client event/feature name appears in the Worker's `SCHEMA`; two mutations (notice gate, 429
  retry) each caught. Engine 3361 green, app 3018 green, headless smoke exit 0 with
  `analytics: disabled (headless)`. Forced e2e (`FDG_ANALYTICS=1`, isolated `FDG_CONFIG_DIR`) against
  wrangler dev: the built binary's `app_launch` arrived and `fetch-stats.sh` counted 1 install.
- 2026-10-28: **Slice 1 (Worker) done, not deployed.** `src/analytics.ts` (new `Analytics` DO, SQLite
  tables `events`/`installs`/`rate`), shared helpers moved to `src/util.ts`, migration `v3`,
  `fetch-stats.sh`, smoke sections 15-19, list-server README. Typecheck clean; `smoke.sh` against
  `wrangler dev` passes all 19 sections. Deploy waits for Chris (stop point).
- 2026-10-28: Plan written and signed off (Chris: "I like all things"; clarifications: popup text
  hand-written later, lifetime uniques must survive upgrades, faction + GDF/AoF per army, opt-out
  both in the popup and in settings later; question round: clients also send game_join, stats via
  script + CSV, README drafted by implementer for review, exclude dev/headless/scenario runs).
  Number 412 taken after `git fetch origin` with origin index + archive topping out at 405.

## Decisions

- 2026-10-28 (Chris, after seeing the popup): **notice trimmed to his message + checkbox + OK**; the
  itemized what-is/isn't-sent lists and the "where it goes" paragraph removed from the notice and from
  the Options panel, and the **main-menu Privacy button removed**. Rationale: don't inundate players;
  the notice says it's anonymous general gameplay usage, the README Privacy section has the detail, and
  opting out is one click. Opt-out after the notice now lives only in the escape menu's Options panel,
  which is reachable only in a game (or by `"AnalyticsEnabled": false` in config.json).
- 2026-10-28 (Chris): **all menu-click events dropped.** The only `feature_open` left is
  `combat_calculator`, sent once per visit when the calculator has stayed open a minute (`DwellTimer`,
  fires at the threshold so it doesn't depend on how the visit ends).
- Exit flush waits for an in-flight timer send instead of skipping, so a quit's events go out before
  the process ends (the first version of its test awaited the background send and so passed against
  the old behavior; it now asserts at the instant Flush returns, and fails without the fix).
- Worker `DAILY_EVENT_CAP = 10_000` events per UTC day (503 past it, which clients spool): the
  free plan's ~100k rows-written/day is per account and shared with the server browser and bug
  reports, and an event costs ~2-3 row writes. Verified by dropping the cap to 3 under wrangler dev.
- 2026-10-28 (Chris): no `feature_open` for clicking Host - a host who launches is already counted by
  game_start, and one who backs out isn't worth a request. Removed from client, Worker enum and disclosure.
- `server_browser` stays in the Worker enum but the client never sends it: the Client dialog is
  browser-first whenever a list server is configured, so it would always equal `join`.
- A winning team with any human on it reports `winner: human` (the question is "do people beat the
  bots"). Unknown game-system slugs report as `other` per slot and make the game `mixed`.
- The session marker (`last-session.json`) is written only on non-excluded runs and holds timestamps
  and the version, no identifier. It is written even when the player opted out, since it never leaves
  the machine on its own; the README paragraph should mention it.
- `FDG_ANALYTICS=1` forces past run exclusions only; it never overrides the player's opt-out or the
  notice gate (tested).
- Slice 1 deviations from the plan above (the schema in `src/analytics.ts` is now the contract):
  - `game_start` also carries `systems` (game-system slug per slot) alongside `factions`, so a
    `mixed` game says which slot played what.
  - `EGameOutcome` has a fourth value, `Disconnect`; `game_end.outcome` allows it.
  - Engine enum names (terrain/objective/randomness/turn/shooting modes, AI profiles) are
    shape-checked as identifiers (`[A-Za-z0-9_-]{1,40}`), not value-listed, so a new engine enum
    member never needs a Worker redeploy. Values the Worker owns (system, outcome, winner, reason,
    feature, os, arch) stay strict enums.
  - An `installs(install, first_seen, last_seen)` table, upserted only by `app_launch`, answers
    every uniques question; the summary never scans all events for uniques. Day buckets use the
    client `t` (clamped to +-7 days of receipt) so spooled events land on the day they happened.
  - The rate record is written before validation, so malformed floods are throttled too. The
    client must therefore treat **429 as retryable** (spool it), unlike other 4xx.
  - `DELETE /stats/smoke` (admin) removes rows under smoke.sh's fixed install id, so smoking the
    deployed Worker leaves real numbers clean. It is the only delete path. `newest=1` on the export
    lets smoke find its rows on a big table.
  - Only one index, `(name, t)`: on the free plan each index entry is a billed row write.
- Durable Object SQLite over Workers Analytics Engine: lifetime uniques was the first requirement
  and AE retains ~90 days. Cost on the free tier is identical for this traffic.
- Random install ID rather than a rotating hash: a rotating hash (Option B) can never answer
  "how many people have ever played"; the random ID identifies nobody as long as the server never
  stores it beside an IP, which the design enforces by keeping IPs out of the events table and
  pruning rate records within minutes.
- Previous-session duration reported on the next launch instead of at quit: a crash or kill never
  gets to send a quit event; the marker file catches both and labels them "unclean", not "crash".
- Host-only game_start/end/abandon with client game_join: counts each game once while still
  showing guests as uniques with their faction.

## Outcome

Closed 2026-10-28, verified live by Chris on prod. Anonymous, opt-out usage statistics ship in the app
and are collected by a third SQLite Durable Object on the #271 Worker (deployed; no new server).

- **Client:** a first-launch notice in Chris's words with one checkbox (nothing is sent before OK); the
  same checkbox in Options, now on the main menu as well as in game. Opting out deletes the install ID and
  anything queued; opting back in mints a fresh ID. Events: app_launch, host game_start /
  game_end / game_abandon, client game_join, and feature_open for the Combat Calculator after a minute
  open. Batched every 30s, sent immediately on quit, spooled on failure. Never sent from headless,
  scenario or `-dev` runs unless `FDG_ANALYTICS=1`.
- **Engine:** the launch message carries a host-minted game ID, and the host exposes each bot's AI
  profile.
- **Worker:** allowlist schema, per-IP rate limit (IP never stored with events), 10k events/day cap,
  admin `/stats`, `/stats/export` (CSV/JSON), `DELETE /stats?confirm=wipe-all-analytics`; read with
  `tools/list-server/fetch-stats.sh`.
- **Side effect:** every Options setting (labels, grid, tokens, reach, fireworks, dice popup time,
  volume) is now saved between runs.
- **Verified live:** notice; full game vs AI (one game_start, one game_end, no abandon after closing
  with the window X); opt-out deletes the ID and drops the unsent launch. Prod wiped afterwards
  (lifetime 0). Not run live: the two-instance game_join (unit-tested).
- **Still to do outside this item:** players only send once a release ships; re-wipe prod just before
  it if testing adds rows. Release-note line: "Anonymous, opt-out usage statistics: a notice on first
  launch, and you can turn it off any time in Options."
- Renumbered from 406 after a collision (see Reconciliations).
