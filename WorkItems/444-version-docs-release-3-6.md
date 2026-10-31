# 444 — Version, docs, release for OPR 3.6

**Status**: in progress (merge pending) - steps 1-7 done on `feature/opr-3.6` 2026-10-31; step 8 (merge + tag) waits for
Chris's GUI hand-verifies, his sign-off on the open forks (#441 F1-F9) and D decisions, and the #445 run.
**Related**: #415 (umbrella), #445 (verification, runs after this item's doc/version changes and before the merge)
**Branch**: `feature/opr-3.6` in both repos until the very last step of this item. Model/effort: Sonnet 5.5 / medium.

## Goal

The release surfaces say 3.6 everywhere, the branch is merged, and `v0.4.0` (plan Q7 default) is tagged so
CI publishes "Future of Dark Grimness v0.4.0 (OPR 3.6.0)". Concretely:

1. `RULES_VERSION` -> `OPR_3_6_0` in `scripts/build-dist.sh` and `.github/workflows/release.yml`.
2. `Directory.Build.props` `<Version>` -> `0.4.0` (or whatever Chris answers on Q7).
3. `CLAUDE.md`: the Versioning section's example strings (`OPR_3_5_1`, `v0.2.0 (OPR 3.5.1)`, `0.2.0+opr.3.5.1...`)
   become 3.6.0 examples; the Work Items paragraph can mention the upgrade plan is archived.
4. `README.md`: ruleset version, book counts (48 GDF + 40 AoF), the renamed/new books, a one-paragraph
   "what 3.6 changed for players" note, and the note that 3.5 saves are not loadable while 3.5 army files are.
5. `docs/EngineNotes.md` and `docs/ResolverGuide.md`: every 3.5 number or rule facet the upgrade changed
   (melee 2" -> 3", Counter's impact facet -> Brace, Indirect move penalty, Scout zone, Transport 2" -> contact,
   Hero morale, Fatigue 6s, "all models" -> "most models") - grep both files for `3.5`, `2"`, `Counter`,
   `Indirect`, `Scout`, `Transport`, `all models` and fix each line against the shipped code.
6. `SpecialRulesAudit.md`: a dated header note that the corpus is 3.6 now and the 3.5 audit text is historical.
7. `WorkItems/`: #415-#445 closed and archived per `WorkItems/README.md`; `docs/opr-3.6-upgrade-plan.md`
   gets a final "shipped" status line (keep the file; it is the record).
8. Merge: `git -C FutureOfDarkGrimness checkout master && git merge --no-ff feature/opr-3.6 && git push`,
   then in the superproject `git checkout master && git merge --no-ff feature/opr-3.6`, confirm the
   submodule pointer is the engine master merge commit, push, `git tag v0.4.0 && git push --tags`.
   Before merging, `git fetch origin` and re-verify that `master` has not moved past what the branch
   already merged (CLAUDE.md: re-verify assumptions before shared operations). Stop and report if it has.

## Rules for the agent

Everything in plan section 1. This is the ONLY item allowed to touch `master`, and only in step 8, and only
after #445 has been run on the final branch state and Chris has said "merge". Steps 1-7 are ordinary
branch commits.

## Acceptance

`scripts/build-dist.sh` (local, dev) produces archives named `FdgRaylib-<rid>-v0.4.0-OPR_3_6_0.*`; `git tag`
shows `v0.4.0`; CI release job green with the title containing "(OPR 3.6.0)".

### Survey rows (code survey 2026-10-29)

- Version: `scripts/build-dist.sh:40` (+ comments :24-28, :39, :51-56, :64-65; `RULES_TOKEN` :56; stamp :58;
  suffix :66); `.github/workflows/release.yml:23` (+ :20-22, :66-67, :71); `CLAUDE.md:29-34`;
  `Directory.Build.props:11`; `FdgRaylib.Tests/AnalyticsTests.cs:144` ("+opr.3.5.1").
- Docs numbers: `docs/EngineNotes.md:33` (3" objective), :43 (D3+2), :48 (1" standoff), :52 (2"/4" melee), :56
  (Fatigue 6s), :57 (Rout at half), :62 (Ambush round 2 / 9"), :63 (Dangerous d6 per model);
  `docs/ResolverGuide.md:58` (9" coherency), :244 (Difficult 6"), :251 (1" spacing), :259 (charge gated on the
  2" constant), :260 (1" standoff band); :92 is a UI tolerance, not a rule.
- Engine comments citing v3.5.1: `ObjectivePlacementValidator.cs:14`, `PlaceObjectivesStage.cs:4`,
  `MoraleUtilities.cs:65, :239, :278`, `MeleeRangeUtilities.cs:7`, `PileInUtilities.cs:6`, `CastSpellStage.cs:89`,
  `ResolveRangedMoraleStage.cs:38`, `ChooseActionStage.cs:109, :734`, `CoreRuleCatalog.cs:481`;
  `WorkItemsList.md:89, :95, :108, :259`.
- README: `README.md:8` (GDF-only framing), `:128` (stale "AoF not yet"); no version string or counts today.
- Constants table `FutureOfDarkGrimness/Utilities/GameWideConstants.cs` (:6, :8, :10, :16, :22, :27, :32, :34,
  :41, :43, :53, :58, :65, :72), `TeleportStage.cs:29` - the numbers the docs must match.

## Notes

- 2026-10-31 (later): **D2 ruled B by Chris ("the absence of a version is an indicator that it's too old"); built.**
  Engine: `OprRulesVersion.CurrentStamp` ("OPR v3.6.0", `Helpers/OprRulesVersion.cs`) is the one ruleset literal;
  `GameSaveFile.RulesVersion` (null on every pre-3.6 save) is written by `GameSaveSerializer.Save` and read back by the
  new `Load(json, out rulesVersion)` overload (`Load(json)` unchanged, schema `Version` stays 2); `LegacySaveNotice`
  (`SaveLoad/`) yields the amber line for an absent / unparseable / other-ruleset stamp; `LegacyArmyNotice.CurrentRules`
  now derives from the stamp. `ILobbyViewModel.SaveRulesVersion` (default null) + a trailing optional parameter on the
  host resume constructor carry the stamp into the lobby. App: `LobbyScreen.LegacyNotices(players, isResume, stamp)`
  draws the save line first, then the #440 army lines, same amber; `Program.LoadGameFlow` passes the stamp (and echoes
  the line to the console); `ScenarioLauncher.LoadStore(path, out notice)` prints it like `ArmyLoader` prints the army
  line, and the GUI `--scenario` path posts it into the game log via `RuleLoadWarnings.PostNotice`. Tests: engine
  `LegacySaveNoticeTests` (round trip, unstamped loads + is legacy, 3.5 legacy, current not; +16, suite 4471 / 1
  skipped), app `LegacySaveNoticeLobbyTests` (save + army lines coexist, resume gating, host VM carries the stamp,
  `LoadStore` console output; +6, suite 4042). Gates: build 6 warnings (unchanged), headless smoke exit 0, `--scenario`
  on a `--make-scenario` save shows no line and the same save with `RulesVersion` deleted shows it. Docs: README,
  release notes, plan rule 6 + #444 row, #445 section 9 line, CLAUDE.md one clause. Decisions D13-D20. Awaiting GUI
  hand-verify (the #445 section 9 line).
- 2026-10-31: **Steps 1-7 done on the branch (step 7 partial by brief); step 8 not started and not mine to start.**
  Engine `cbd4cf79` (444: rule citations that still read v3.5.1 now cite the unchanged v3.6.0 text); superproject
  `e864f1c` (0.4.0, OPR_3_6_0, CLAUDE.md examples, pointer bump), `ea56978` (README / EngineNotes / ResolverGuide /
  SpecialRulesAudit / index lines / `docs/release-notes-v0.4.0.md` draft), plus the records commit (archive, plan, this
  file). `master` untouched in both repos (super 8e598a9, engine 457bb41d); no tag.
  - **Step 1.** `RULES_VERSION` default `OPR_3_6_0` in `scripts/build-dist.sh` (+ every comment example) and
    `.github/workflows/release.yml` `env:` (+ the title comment). `FdgRaylib.Tests/AnalyticsTests.cs` fixtures left (D5).
  - **Step 2.** `Directory.Build.props` `<Version>0.4.0</Version>` (Q7 default). Nothing else pinned 0.3.0 except the
    plan's Q7 history line and the AnalyticsTests literals (D5). The app prints no version anywhere (no `--version`, no
    banner); the built `FdgRaylib.dll` carries `AssemblyInformationalVersion` `0.4.0-dev+<sha>` (the SDK appends the
    commit, as `AppVersion` documents), which is what bug reports and analytics send.
  - **Step 3.** CLAUDE.md Versioning examples -> `OPR_3_6_0` / `v0.4.0` / `v0.4.0 (OPR 3.6.0)` /
    `FdgRaylib-<rid>-v0.4.0-OPR_3_6_0` / `0.4.0+opr.3.6.0.git-<sha>-<date>`; one Work Items bullet naming the plan
    as the record of the upgrade (D9). The attribution rule and everything else unchanged.
  - **Step 4.** README: line 8 now "GrimDark Future (and Age of Fantasy)"; new "Rules Version: OPR 3.6.0" section
    (48 GDF + 40 AoF, Voidspace Stalkers new, Daemons of X / Saurian Starhosts renames, a what-changed paragraph, the
    older-files paragraph with the amber notice and the save caveat - D1, D2); the stale AoF "Unsupported" bullet
    replaced with a "3.6 terrain types" bullet (D3). SmartScreen / Gatekeeper notes kept.
  - **Step 5.** Every number in the survey rows re-checked against the shipped code (`GameWideConstants.cs`,
    `TeleportStage.cs`, `IUnit.GetIsAtHalfStrength`, the stages) and the 3.6.0 text: melee 3"/4", standoff 1",
    coherency 1"/9", Difficult 6", objectives 3" / D3+2 / 9" apart, Ambush round 2+ over 9", Rout at half, Dangerous
    dice = wounds to destroy, Fatigue 6s, Counter/Brace, Indirect, Scout, Hero aura, most-models - all already 3.6
    in both docs (the phase-2 items updated them as they went). Edits: "(3.6, unchanged)" anchors on the objective,
    D3+2, Rout, Ambush, coherency, Difficult and spacing lines, the Known-stubs survey line dated, and two stale
    statements fixed (EngineNotes "3.5 Unique, which stays until #436" - Unique left with the 3.5 books at #438;
    "`attackedFromOverInches` ... not-yet-rebaked books" - no bundled book embeds it). Engine comments: the survey's
    v3.5.1 citations (`ObjectivePlacementValidator`, `PlaceObjectivesStage`, `MoraleUtilities` x3,
    `ResolveRangedMoraleStage`, `ChooseActionStage` x2, `CoreRuleCatalog` Takedown) plus four test comments citing the
    same text now cite v3.6.0 (D7); `MeleeRangeUtilities`, `PileInUtilities`, `ConsolidateStage`, `GameWideConstants`
    and `CastSpellStage` already read 3.6 (their "v3.5.1" is a deliberate before/after comparison). Index lines fixed:
    the "All 33 GF v3.5.1 core special rules" / "0 of 13,870" paragraph, #234's Caster quote, #241's version gate,
    #175's Fearless quote.
  - **Step 6.** `SpecialRulesAudit.md` dated historical header (corpus is 3.6; 3.5-era wording and anchors).
  - **Step 7 (partial, per brief).** Archived #416, #419, #427, #428, #431, #432, #435, #436, #437, #438, #439 (index
    lines moved verbatim to `WorkItems/Archive.md`; each file already had Status done and an Outcome). Kept in the
    index (D6): #415 (umbrella), #417, #418, #420, #421, #422, #423, #424, #425, #426, #429, #430, #433, #434, #440,
    #442, #446, #447 (a GUI hand-verify by Chris still owed in each file), #441 (hand-verify + forks), #443 (deferred,
    open), #444 (this item), #445 (not run). Plan: status line "feature-complete as of 2026-10-31 pending ...", #444
    row, ", #444" in the section 6 D-entry list.
  - **Release notes.** `docs/release-notes-v0.4.0.md` drafted from the #441 / #440 / #442 hand-offs, plan sections
    2-3 and the #434 / #436 / #439 follow-up lists (D4). A placeholder line about F1-F9 must be deleted or updated.
  - **Gates.** Engine 4455 passed / 1 skipped (unchanged); full `dotnet build` 0 errors (208 warnings, all pre-existing:
    the props bump forces a full recompile of both projects, which an incremental baseline build skips); app 4036
    passed (unchanged); `--validate-rules` both supplements "OK: 401 definitions, no problems."; shipped
    `--rule-coverage` Dead 0; headless smoke exit 0 with `Game result:`. `scripts/build-dist.sh linux` -> stamp
    `0.4.0+opr.3.6.0.git-e864f1c-20261031-dirty` (dirty = an unrelated uncommitted local file), archive
    `FdgRaylib-linux-x64-v0.4.0-OPR_3_6_0.tar.gz`; the run wiped `dist/` (gitignored build output; it held only the
    #440 linux archive).
  - **Deferred.** Step 8 (merge + tag) -> this item, after hand-verify / fork sign-off / #445. The stale
    "not-yet-rebaked books" / "until #438" / "until #436" code comments (engine `IHasShootChargeDistance.cs:18`,
    `Condition.cs:248, :405`, `Effect.cs:59`, `MovementRuleQueries.cs:97`, `RuleValidator.cs:193`,
    `OprBookImporter.cs:370, :378`; tests `CounterLegacyDataTests.cs:22`, `RerollThresholdRuleIntegrationTests.cs:132`,
    `ConditionSerializationTests.cs:68`, `OprBookImporterTests.cs:249`, `WeaponEffectAssignerTests.cs:40`; app
    `TerrainDebuffShippedDataTests.cs:56`, `BundledBookRulebookTests.cs:94`) -> #415 (post-merge comment sweep; noted
    there): outside step 5's v3.5.1-citation scope, comment-only, and wide enough to deserve its own slice. The
    Elevated -> Climbable enum reservation #443 allowed "if cheap" -> #443 (D12).

- 2026-10-31 (from #441): release-notes material - the Army Forge lists the 48 + 40 books under their 3.6 names
  (Voidspace Stalkers new); Spawn / Split rules hover with their description instead of "not enforced"; the Forge no
  longer grays out a combo upgrade (e.g. Assault Brothers' "Heavy Pistol, Energy Hammer") after the pistols were all
  swapped; long 3.6 rule texts wrap in every hover; the importer explains a unit priced below Army Forge (a stale
  "Replace X" pick) and AoF Shadow Stalkers says why it has five spells. The README's "Age of
  Fantasy armies: ... I will add them eventually" bullet (now line 127) is still yours per #441's survey row.
- 2026-10-31 (from #440): release-notes material - the Random Army lists were rebuilt for 3.6 (some units substituted
  or cut to keep each inside its 1k/2k/3k/4k bracket; #440 D1-D4 for Chris's review), 3.5 army files keep loading and
  playing with an amber "built for OPR v3.5.x; it plays with 3.6 rules" lobby/CLI notice, and Very Fast is gone from
  the built-in test army and the catalog. `scripts/build-dist.sh linux` was run and ships the regenerated `armies/`
  folder unchanged; the archive still says `OPR_3_5_1` until RULES_VERSION is bumped here (and the run wiped the old
  multi-platform `dist/` artifacts, as the script always does).
- 2026-10-30 (from #442): release-notes material - the Tactician now uses Spell Drain, prices its Versatile picks and
  the 3.6 activation-start moves, and reads the 3.6 range / charge-distance rules, Ravage, Hazardous, Scourge and Tag
  tokens; the Combat Calculator takes a charge distance and shows Ravage. Two owner-review items there (D7 break-bonus
  discount, D9 Artillery's moved -1) and the Strategist leaf decision (D11) are in #442's Decisions.
- 2026-10-29: filed with the plan.

## Decisions

- **D1 (OWNER REVIEW) - README shape and wording.** A new "Rules Version: OPR 3.6.0" section right after the intro
  (book counts, renames, one what-changed paragraph, one older-files paragraph), rather than a CHANGELOG file or a note
  under Installation: the README is the release's landing page and the version is the first thing a returning player
  needs. The title "A New Way to Play GrimDark Future" is left as is (branding; line 8 now says "(and Age of
  Fantasy)"). Wording is in a neutral voice beside Chris's first-person text; his to restyle.
- **D2 (OWNER REVIEW) - 3.5 saves: "not supported", not "not loadable" (brief vs code).** The brief and plan rule 6
  say 3.5 saves are not loadable / not supported. There is no save version gate: a 3.5-era save
  (`377-SpellVerify.fdgsave`, copied to the scratchpad, run `--headless --scenario <copy> --all-ai`) loaded and played
  to `Game result:` with exit 0. README and release notes therefore say "not supported: some may still open, but they
  keep the 3.5 armies they were saved with; start a new game". Alternative: a save version gate refusing pre-0.4.0
  saves (a code change, out of this item's doc scope; Chris to say if wanted). **Chris ruled B on 2026-10-31** ("let's
  do B. We can treat the absence of a version as an indicator that it's too old"); built the same day: saves carry an
  `OPR v3.6.0` rules stamp, a save with no stamp (every pre-3.6 save) or another ruleset's stamp loads anyway and the
  resume lobby / `--scenario` console show the amber "This save predates the OPR 3.6 upgrade; it plays with 3.6 rules"
  (or "This save was written for OPR v3.5.3; it plays with 3.6 rules") line, never a refusal. README, release notes and
  plan rule 6 reworded to match. Details in the Notes and D13-D20.
- **D3 (OWNER REVIEW) - the stale AoF "Unsupported" bullet became a "3.6 terrain types" bullet.** AoF has shipped
  since 3.5, so the bullet was simply false; the slot now names the one 3.6 area not in this release (#443 deferred)
  instead of being deleted.
- **D4 (OWNER REVIEW) - release-notes draft.** Structure: rules version, books, what changed, older files, interface,
  AI, known gaps. Known gaps list the #436 D6 / #434 behaviour follow-ups in player terms (Teleport 3"/6", Swift,
  Predator, ... ) and the #439 spell edge cases, plus Combat Possession and #443; the #441 F1-F9 line is a marked
  placeholder. Kept out: owner-review D entries of individual items (internal), bench numbers.
- **D5 - `AnalyticsTests.cs` fixtures left at "0.3.0..." / "+opr.3.5.1".** They are literal inputs to
  `AnalyticsConfig.BuildExclusion` and the event's `ver` field, testing the stamp FORMAT (dev vs release), not derived
  from `Directory.Build.props` or `build-dist.sh`; changing them proves nothing. Per the brief, left and noted.
- **D6 - archive criteria.** Archived = Status done, Outcome written, and no GUI hand-verify by Chris owed in the file.
  Open owner-review D entries do not block archiving: plan section 6 tracks them as a separate open set, and the
  verbatim-moved index lines keep their "owner review asked on ..." text. Borderline calls: #431 ("nothing awaits GUI
  hand-verify beyond a glance"), #432 ("GUI glance (Chris, optional)") and #436 ("worth a glance in the next GUI
  session") archived as optional glances; #434's "GUI glance (Chris)" on the Wolfborn / Bounding move tool reads as
  owed, kept; #438's "Awaiting #424's GUI hand-verify" is #424's (kept open there) and its AI bench was re-run by the
  orchestrator (#442, 2026-10-30), archived; #420's Status line asks for a GUI hand-verify of the movement-panel
  hint, kept.
- **D7 - which engine "v3.5.1" comments changed.** Only citations whose quoted rule is the SOURCE of the behaviour and
  whose 3.6.0 text says the same (checked against GF Core Rules / Beginner's Guide v3.6.0: Shaken always fails, morale
  at half or less, Rout melee-only, objectives p.6, Takedown grants no LoS/cover, Caster before attacking). The Caster
  quote moved to the 3.6 wording ("Once per activation, before attacking"). "2" in v3.5.1"-style before/after notes
  stay. Four test comments with the same citation changed too (beyond the survey rows, same text, comment-only).
- **D8 - docs: annotate, don't rewrite.** Every survey-row number already matched the shipped code and 3.6; short
  "(3.6, unchanged)" anchors show they were checked without churning the paragraphs.
- **D9 - CLAUDE.md Work Items bullet.** One bullet naming `docs/opr-3.6-upgrade-plan.md` as the upgrade's record, as
  the brief allows; nothing else outside the Versioning examples touched.
- **D10 - version check without a version display.** The app prints no version (no flag, no banner, no main-menu
  label), so the check is the built assembly's InformationalVersion (`0.4.0-dev+<sha>`) and the dist stamp.
- **D11 - the index's "All 33 GF v3.5.1 core special rules" paragraph** rewritten to "the OPR 3.6.0 core special rules
  are implemented ... shipped coverage Dead 0 on the 88 books", keeping the #196 / #197 history; no 3.6 core-rule count
  stated (the API's 39 plus the PDF's extras is the plan's figure, not one this item re-verified rule by rule).
- **D12 - the Elevated -> Climbable reservation (#443 "may be reserved by #444 if cheap") not done.** Saves and
  scenarios store terrain flags by NAME (#443 survey), so a rename needs a JSON alias plus UI and test changes - not
  cheap, and it is engine work outside a docs item. Left to #443.
- **D13 - one ruleset literal, in the engine.** `FDG.OprRulesVersion.CurrentStamp` = "OPR v3.6.0" (`Helpers/`, namespace
  `FDG`, same shape as a book's `Version`) is the only place the ruleset is spelled; `CurrentMajorMinor` ("3.6") is
  derived from it and `LegacyArmyNotice.CurrentRules` became a property returning that (was its own `const "3.6"`).
  Alternatives: a const concatenation `"OPR v" + CurrentRules + ".0"` (hard-codes the patch digit) or two literals
  pinned together by a test (drift is caught late). Bumped alongside `RULES_VERSION` (comment there, CLAUDE.md clause).
- **D14 - `LegacySaveNotice` is its own class (`FDG.SaveLoad`), not an extension of `LegacyArmyNotice`.** The two differ
  on purpose about an absent stamp: an army with none is hand-authored (nothing says it is old, #440), a save with none
  predates 3.6 (every save this build writes is stamped). One `IsLegacy` cannot serve both without a flag parameter;
  a test (`TheArmyNotice_StillIgnoresAnAbsentStamp_WhileTheSaveNoticeDoesNot`) pins the difference.
- **D15 - the stamp rides the view model.** `ILobbyViewModel.SaveRulesVersion` (default interface member, null, like
  `LaunchedGameId`) and an optional trailing `saveRulesVersion` parameter on the host resume constructor; the screen
  helper `LobbyScreen.LegacyNotices(players, isResume, saveRulesVersion)` is ImGui-free and puts the save line before
  the army lines. The null default means a resume lobby built from an in-memory store (tests, `ResumeRejoinNetworkTests`)
  would read as legacy if drawn - acceptable, the app's only resume-lobby constructor call (`Program.LoadGameFlow`)
  passes the real stamp. Alternative: a `LobbyScreen` property set after `SetViewModel` like `ListedPublicly` (#412);
  rejected because the host owns the save and the view-model route is what a later client sync would reuse.
- **D16 - not synced to joining clients in 0.4.0.** A client's `SaveRulesVersion` is null and its `IsResumeMode` is
  false, so no line. No existing synced lobby field carries it (the roster's `RulesVersion` is per army), so syncing
  means a new lobby message field; a resume lobby cannot re-crew networked clients yet anyway ("Networked re-crew ...
  is a follow-up" on the constructor). Host-side display is enough for the release.
- **D17 - GUI `--scenario` shows the line in the game log.** `ScenarioLauncher.LoadStore(path, out notice)` prints the
  line to the console in both modes (exactly as `ArmyLoader` prints the army line) and `Program` posts it into the game
  log through the new `RuleLoadWarnings.PostNotice` (amber WarnColor, beside the #168 drop summary, buffered until the
  log exists). Not hand-verified in the GUI (no display driving) - folded into the #445 section 9 line.
- **D18 - no checked-in unstamped fixture.** Both test suites derive the pre-3.6 shape by stripping `RulesVersion` from a
  fresh `Save()` of a default store (engine: Newtonsoft `JObject`; app: `System.Text.Json` `JsonNode`) - a real round
  trip of this build's own format with nothing to rot. A hand-made `.fdgsave` would need a valid type map and would
  break on the next schema bump; the user's untracked root saves stay out of the repo.
- **D19 - no save schema bump.** `GameSaveSerializer.CurrentVersion` stays 2: the stamp is additive and advisory, `Load`
  never gates on it, and the one-argument `Load(string)` is kept as a delegating overload (`StoreSnapshot`, tests and
  the app compile unchanged).
- **D20 - `Program.LoadGameFlow` also echoes the line to the console.** One extra `Console.WriteLine`, so a GUI Load
  Game leaves the same trace in the terminal as the CLI paths; the lobby line is the one the player sees.

## Disagreements between shipped code and the 3.6 text (recorded, code not changed)

- Teleport: `TeleportStage.TELEPORT_RANGE_INCHES` is a flat 6"; the 3.6 description gives 3" / 6" by action. Already
  in #436 D6 item (1) / the #434 follow-up list; no doc states the Teleport range. Release notes list it.
- 3.5 saves (D2): the plan says unsupported, the code has no gate (they can load). Resolved 2026-10-31 (D2 option B):
  they load with an amber notice; plan rule 6, README and release notes now say so.
- Otherwise none: every number the two docs state matches both the code and the 3.6.0 text.

## Outcome

(open) Steps 1-7 done on the branch 2026-10-31; step 8 (merge + tag) waits for hand-verify, fork sign-off and #445.
