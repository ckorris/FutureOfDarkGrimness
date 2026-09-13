# 402 — Host setting: which game systems' armies a lobby accepts

**Status**: done
**Related**: #378 (GameSystem field + Forge system filter + the mixed-system warning this replaces),
#153 (launch gate), #372/#388 (Random Army + BotArmyPicker), #395 (armies folder discovery)

## Goal

A synced, host-owned lobby setting — **Army Source: All / Grimdark Future / Age of Fantasy** — that
decides which game systems' armies the lobby accepts, changeable in the lobby like Army Points. Armies
that break it are highlighted the way over-points armies are, and a launch is BLOCKED (not merely
warned) while any slot is illegal. Random Army only ever rolls a legal army, and the armies folder is
scanned recursively so it can be organised into `GDF/` and `AoF/` subfolders.

Done when: the combo syncs host -> client, the Faction cell reads red with an explaining tooltip on a
wrong-system army, the Pts cell explains itself on both its colours, LAUNCH greys out with a tooltip
listing every blocker, Random Army never hands out an illegal list, and `#378`'s advisory mixed-system
line is gone.

## Notes

- 2026-09-12: **Built, all four slices, suites green** (engine 3326, app 2927, headless smoke exits 0
  with a completed game). Commits: engine `572fdcc` (settings + gate split) -> `180ce4d` (lobby UI +
  submodule bump) -> `06f281a` (recursive scan, Random Army filter, folder split) -> engine bot-stub
  fix -> `d9f758f`. Still needs a GUI hand-verify (checklist below).
- 2026-09-12: **Test Army stub removed outright** (owner request, while hand-verifying). See the
  decision below. Suites green again: engine 3326, app 2929, smoke exits 0.
- 2026-09-12: **Renumbered 400 -> 402.** Filed as 400 against `origin/master` at `cbab372` (index
  high-water 398, archive max 399, no `400-*` anywhere) - but another session filed 400 the same day
  from the same state and merged first, taking 400 (Deadly clump order) and 401 (wound packets). This
  item was unmerged, so it yielded, per the rule. See `Reconciliations.md`. Commits before the merge
  still read `400:` in their subjects; the history was not rewritten.
- 2026-09-12: Merged `origin/master` (#398 Combat Calculator UX, #400/#401 wound packets). Adopted
  two helpers it introduced: `UiText.Tooltip` (printf-safe - these tooltips carry player and army
  names, which is exactly the '%' hazard it exists for) and `UiChrome.ButtonSize`.

## Decisions

- **2026-09-12 — `All`, not `Both` (owner).** The enum's permissive value is `EAllowedGameSystems.All`
  rather than `Both`, because OPR's custom-book tool can produce armies belonging to neither
  collection, and a two-valued name would have to be renamed the day one of those shows up. `All = 0`
  so a pre-#402 save (field absent from the JSON) resolves to today's behaviour.

- **2026-09-12 — the gate blocks, it does not warn (owner).** Three conditions hard-block a launch:
  wrong game system, over the points limit, and **no army assigned at all** (previously the host
  silently substituted a 100-pt stub). Deliberately NOT blocking: an *underbuilt* army (50+ pts under
  the limit, the yellow Pts state) stays a legal advisory — blocking it would reject perfectly legal
  lists and kill quick small-army test games in a big-points lobby.

- **2026-09-12 — Forge catalog errors keep their #153 override (owner).** Force-org problems (too many
  Heroes for the points, etc.) still raise the "Launch anyway?" confirm rather than blocking, so the
  house-rules escape hatch #153 was built for survives. This is what splits `LaunchGate` into two
  results: `BlockingProblems` (cheap, summary-level, computed every frame to drive the button) and
  `OverridableProblems` (runs the full `ListValidator` over a Forge army's embedded book, so it is
  computed only on click).

- **2026-09-12 — blocked launch reads as a disabled LAUNCH + tooltip (owner).** Not a modal: the
  button greys the moment anything is wrong and hovering it lists every blocker. The per-row red cells
  are the at-a-glance half of the same information.

- **2026-09-12 — the mixed-system warning is deleted, not kept (owner).** #378's advisory line
  ("! Mixed game systems ... launch if that is the plan") is superseded by the setting. Chris hadn't
  known it existed; two overlapping system notices in one panel is worse than one rule.

## Decisions (continued)

- **2026-09-12 — a fresh bot arrives with NO army (found mid-build).** `AddAiPlayer` stamped every new
  bot with a hard-coded 100-pt "Test Army" stub, which meant the "no army assigned" blocker could never
  fire on a bot row: a lobby that could not roll a real army (no armies folder - see #395 - or nothing
  of an allowed system) would have launched a real game with the fake list, silently, which is the exact
  failure the blocker was asked for. Bots now arrive unassigned like human slots. The launch-time
  fallback in `Launch()` stays for the paths that never reach the lobby's gate (scenarios, CLI).

- **2026-09-12 — the maintenance CLI tools recurse too.** Splitting `armies/` would otherwise have made
  `--retrofit-effects armies` and its siblings silently index nothing. Three `Directory.GetFiles` sites
  in `Program.cs`. The pre-existing `IndexedPointsMatchAFullDeserializeOfEveryShippedArmy` caught the
  same class of bug in the test suite itself.

- **2026-09-12 — the Test Army stub is gone entirely (owner).** `GetTempTestArmyFile` /
  `GetTempTestUnit` and both substitution sites deleted. An army-less slot is now REFUSED at two
  depths instead of papered over: `ValidateLaunchSettings` names the player and returns a fail reason
  (so a caller that skips the lobby's gate still cannot launch), and `GameBootstrap.CreateArmy` throws
  if one gets past that. `PlayerSlot.ArmyListFile` became nullable, which is what a resume always
  meant in practice - saved slots carry null, `BuildRuleResolver` skips them, and
  `RestoreArmyRuleData` supplies the definitions from the save.

  Consequence caught while doing it: a resume lobby's rows ALL report no army, so the new red Army
  cell would have fired on every one of them and told the player to load a list already in the save.
  `LobbyArmySource.IsMissingArmy` takes an `isResumeLobby` flag and exempts them.

  Untouched on purpose: `FdgRaylib/Cli/ArmyLoader.cs`'s "<Player>'s Test Army" - a different
  mechanism (the headless EOF fallback documented in CLAUDE.md, which the smoke command relies on).

## GUI hand-verify checklist

1. Army Source combo appears under Army Points, host-editable, greyed for a client and on a resume.
2. Set Grimdark Future, load an AoF army: the Faction cell turns red and its tooltip names both sides.
3. LAUNCH greys out; hovering it lists every blocker. Fix the row -> it goes live again.
4. Pts cell: an over-limit army is red and its tooltip says the launch is blocked; an underbuilt one is
   yellow and says it is legal.
5. A slot with no army reads red "N/A" on the Army cell and blocks.
6. Random Army in an AoF-only lobby with only GDF armies in the folder: no pick, slot stays empty/red.
7. Add a bot with `armies/` present: it gets a real list. "Test Army" no longer exists at all.
   With `armies/` absent, the bot row stays red "N/A" and LAUNCH stays greyed.
10. Resume a save: rows read "N/A" in PLAIN text (not red), RESUME is live, and the game resumes with
    its own armies.
8. Host + client: the host changing Army Source is reflected on the client's roster colours immediately.
9. A Forge army with a force-org error still raises "Launch anyway?" (not blocked).

## Outcome

**Closed 2026-09-12, hand-verified by the owner.** The lobby gained an **Army Source** setting (All /
Grimdark Future / Age of Fantasy), host-owned, synced, remembered between sessions, and live-editable
like Army Points. Three conditions now BLOCK a launch rather than warning: an army from a disallowed
system, an army over the points limit, and a slot with no army at all - each with a red roster cell that
explains itself, and the problem list on the greyed LAUNCH button's tooltip. Being underbuilt stays
legal and says so in yellow. Forge catalog errors keep their #153 "launch anyway?" override, which is
why `LaunchGate` split into a cheap `BlockingProblems` (polled per frame) and `OverridableProblems`.

Random Army only rolls armies of an allowed system with no last-resort fallback, the armies folder scan
recurses, and the 28 bundled lists moved to `armies/GDF/` with an empty `armies/AoF/` beside them -
folder names are decoration, the slug in the file decides. #378's mixed-system advisory line is gone.

Found and fixed en route: a fresh bot was stamped with a hard-coded 100-pt "Test Army", so the
no-army blocker could never fire on a bot row; the whole stub mechanism was then removed at the owner's
request, and an army-less slot is refused by `ValidateLaunchSettings` and throws in
`GameBootstrap.CreateArmy` rather than being papered over. Resume lobbies are exempt from the empty-army
flag (their slots legitimately carry none).

Renumbered from 400 mid-flight - see `Reconciliations.md`; commits before the merge read `400:`.
