# 168 — Surface rule-load diagnostics in the UI

**Status**: implemented + tested; awaiting GUI hand-verify
**Related**: SpecialRulesAudit.md §5 Phase 1.1 (SYS-1, engine half DONE earlier), #196/#197 (the 199
unimplemented corpus references this makes visible), #059 (embedded definitions), #003 (the builder
warning pane this extends)

## Goal
The engine's `RuleDiagnostics` channel (unimplemented / mis-scoped / missing-argument rule drops at
army load) only reached stdout — invisible in GUI sessions, so a player fielding an army whose rules
silently did nothing got no signal. Surface it app-side: once per launch in the game log
("N special rules ... are not implemented: ..."), and live in the army builder's validation pane.

## Notes
- 2026-07-26: Implemented end-to-end.
  - **Engine**: `RuleDiagnostics.OnRuleDropped` structured event (`RuleDrop`: name/owner/reason/message,
    `ERuleDropReason`: Unimplemented/WrongScope/MissingArgument/NoWeaponsToAttach) raised alongside the
    string channel by all four load-time drop sites. The classification ladder was extracted to
    `ArmyListRuleResolution.ResolveOrDescribeDrop` (no side effects); `ResolveForScope`/`ResolveAnyScope`
    are now thin warn-wrappers over it.
  - **Engine**: `ArmyRuleAudit.Audit(ArmyListFile)` — store-free walk of a list's rule references
    (weapon entries as the UnitData ctor walks them, unit-level names as AttachRulesFromArmyList, spell
    WithRules names as ArmyListSpellResolution; same order), on the shared ladder so it cannot drift.
    `ArmyRuleAuditParityTests` runs the audit AND a real `CreateArmy` on the same messy army and asserts
    identical (name, owner, reason) drop sequences; a second test forces the fixture to keep exercising
    every `ERuleDropReason`. Also covers: clean army (no drops), invalid embedded definitions
    (`EmbeddedDefinitionError` reported, audit still completes core-only), both-channels event test.
  - **App**: `RuleLoadWarnings` (GUI modes only; headless keeps the bare-stdout fallback that automated
    runs grep) subscribes at startup, buffers (armies load host-side before the GameLog exists), then
    `AttachLog` at `GameGuiWiring.Launch` posts the aggregated summary visibly + every raw line on the
    Debug channel, and streams later warnings (dispatch-time `WarnOnce`s) as Debug lines. When
    installed, warnings are still echoed to stdout in the fallback's exact `[rules]` format.
    Flush points per launch path: lobby GUI = AttachLog (loads precede launch); CLI+window = AttachLog
    after `new FDGServer` in `CliApp.RunAsync`; GUI `--scenario` = `FlushPending()` after its server
    build (that path launches the GUI first). 8 formatter tests app-side (`RuleLoadWarningsTests`),
    incl. an ASCII-only guard.
  - **App**: Army builder header pane (under the #003 force-org warnings): per-frame
    `ArmyRuleAudit.Audit` (house style — `RefreshRuleNames` already recomputes per frame); one
    aggregated line for unimplemented names, one line per misauthored reference (each individually
    fixable while authoring), one line if embedded definitions would be rejected at launch.
- Verified: engine 2170/2170, app 615/615, full build clean, headless smoke exit 0 (stdout unchanged —
  `RuleLoadWarnings` never installs headless).

## Decisions
- Summary is game-wide, not per-player: the channel's owner strings name the unit/weapon/spell, which
  is what a tester acts on; per-army attribution would need PlayerID plumbing through every emit site.
- Builder audit is name-level classification on the shared ladder, not a throwaway `CreateArmy` — cheap
  enough per frame; drift risk carried by the parity test instead of runtime cost.
- Audit scope is one list standalone (core catalog + its own embedded defs). At a real launch another
  player's embedded definition could implement a name this list flags — acceptable for an advisory pane.
- Dispatch-time diagnostics (`WarnOnce` granted-rule failures) stay string-only; they stream into the
  Debug log but don't join the launch summary.

## Deferred (recorded, not silently cut)
- **Army Forge screen surfacing**: the Forge (where most real lists get built) shows no equivalent
  pane; its book-sourced units hit the same 199-name tail. Natural sibling — needs its own UI decision
  on a dense screen.
- **Networked clients**: armies build host-side only, so the summary appears in the host's log, not a
  remote client's (same visibility as every other load-time diagnostic; client sync would ride #190-
  style state plumbing).
- **Per-player attribution** in the launch summary (see Decisions).

## Hand-verify checklist (GUI)
1. Army builder: load/build a list referencing an unimplemented rule (any imported book army with an
   exotic rule, or type one in freeform) — amber "N special rule(s) on this list are not implemented:
   ..." under the points header; adding/removing the rule updates it live.
2. Builder misauthored lines: give a weaponless unit a weapon-scoped rule (e.g. Bane in melee) — a
   per-reference "will be dropped at launch: no weapon to carry it" line appears.
3. Host a lobby game with that army: after LAUNCH the game log shows the same aggregated line in
   amber, once; console Debug toggle reveals the per-reference `[rules]` detail lines.
4. Headless run of the same army: stdout still carries the plain `[rules] Skipping ...` lines (no
   summary, no behavior change).
5. `--scenario` GUI launch of a scenario whose save/army carries an unimplemented spell rule: summary
   still appears (FlushPending path).

## Outcome
(pending hand-verify)
