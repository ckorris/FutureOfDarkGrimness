# 329 — In-game Army List overlay (toggleable, all players)

**Status**: in-progress (slices 1-3 implemented + tested; awaiting GUI hand-verify)
**Related**: #259 (RuleGlossary/RuleTextFlow), #292 (RuleHoverText), #246 (EscapeRouter/ViewSettings), #227 (joined-hero stats), #328 (render-thread token snapshots), #149 (precedent for adding a serialized field to unit data)

## Goal

A toggle key brings up each player's army list in-game, visually recognizable as the printed Army
Forge card layout (name [count] - pts header, blue Quality/Defense/Tough stat pills, underlined
hoverable special rules, RNG/ATK/AP/SPE weapon table) but live: wounds, casualties, destroyed units,
activation state, and status tokens all reflect current game state. Tabs switch between players'
lists. Done = all three slices below shipped, or remaining slices explicitly deferred here.

## Design (signed off 2026-08-02)

User sign-off on all forks:

- **Presentation**: fullscreen modal overlay over a dimmed board (Esc-menu treatment), toggled by
  hotkey. Read-only; never touches the engine; reads live state each frame like TableTooltipOverlay
  (use #328 snapshot conventions for token reads).
- **Hotkey**: **L = army List**. The current L binding (unit-name labels, ViewSettings.ShowLabels)
  **rebinds to N** (Names); update the TableTooltipOverlay hotkey check and the Esc-menu Options
  label ("Unit labels (N)"). Toggle gated on !WantCaptureKeyboard / !EscapeRouter.MenuOpen like the
  other view toggles. While open, the overlay claims EscapeRouter.TryConsumeEscape() (Esc closes the
  list; second Esc opens the menu). Discoverability: small "Army Lists" button next to the
  bottom-left Menu button + an Options-panel row.
- **View modes**: card view first (slice 1); condensed table view is a later polish facet.
- **Points**: per-unit points require the engine plumb (slice 2) — approved.
- **Tabs**: one per filled player slot, local player first, tab colored via colorForPlayer, labeled
  "Name - Army Name". Tab header line: army name, faction, total points vs game points limit (from
  the lobby's synced ArmyListSummary; needs a small plumb through GameGuiWiring.Launch; scenario
  direct-launch falls back to player name only).
- **Live-state layer**: live/starting model count "[12/20]"; Tough pill shows "Tough 7/12" with warm
  tint when wounded (fraction text always — never color alone); weapon counts recompute free via
  AllWeapons() (filters dead models); destroyed units stay listed, dimmed + "DESTROYED" stamp;
  "Activated" tag (tooltip red) on spent units; TokenChipRenderer chips row; joined hero as a
  sub-block in the host card with its own Qua/Def (HeroAttachment, #227).
- **While open**: engine keeps running; if a local stage request arrives, a pulsing "action needed"
  chip appears in the overlay header (_resolverOverlay.HasAnyPending), click closes the list.
- **Layout**: responsive N columns (floor(width/cardWidth)), vertical scroll, scroll position kept
  per tab. ASCII-only text (CLAUDE.md).

## Slices

1. **Overlay shell + card view** — new `ArmyListOverlay` in FdgRaylib/Rendering (sibling of
   EscapeMenuOverlay), attached in the AttachGameSession path (ITableState + colorForPlayer); drawn
   in RaylibRenderer's in-game block before DrawMenuButton/_escapeMenu.Draw so the menu dims above
   it. L-key rebind (labels -> N). Cards: pills, hoverable rules via RuleHoverText, weapon table via
   WeaponStatFormatter, wounds/destroyed/activated states. Pure helpers (weapon grouping, column
   packing, tab ordering) unit-tested in the BannerBandLayoutTests style. No points yet.
2. **Points + headers** — engine (submodule-first): carry PointCost from UnitFileEntry through
   ListCompiler -> UnitTemplate/UnitData so it syncs and survives saves (#149 pattern; old saves
   default 0 -> points hidden). Template round-trip test. App: plumb ArmyListSummary
   (name/faction/total) into the game session for tab headers.
3. **Polish** — condensed table view as a header toggle, collapse chevrons, per-model tough pips,
   possibly a spells section for casters (feasibility TBD).

## Notes

- 2026-08-02: v6 feedback: card alpha corrected for compositing - the card draws OVER the 85%
  window, so total = 0.85 + A*0.15; A = 2/3 lands the card region at the intended 95% total (the
  naive 0.95 stacked to ~99%, reading fully opaque). Header-line widgets now pin to one ABSOLUTE
  centered Y each (a relative nudge before the first radio didn't survive the next SameLine - the
  Table radio drifted high).
- 2026-08-02: v5 feedback: Cards/Table radios (and the close button, same line) vertically centered
  against the LargeFont title; player tabs enlarged (1.3x label scale + fatter FramePadding, scale
  reset for tab content); card bodies now DARKER (ImGuiTheme.InkWell) at 95% alpha over the window's
  85% (the 0.90 bump from the previous round is superseded) - cards read as crisp dark sheets on the
  ghosted board, like the printout.
- 2026-08-02: v4 feedback: table header rows are no longer interactive (TableHeadersRow's hover
  highlight advertised a click that did nothing; now a Headers-flag row with plain text). The
  Action-needed chip and Close merged into one button: "Return to Game (L)" normally, orange-flashing
  "Action Needed - Return to Game (L)" while a local prompt waits.
- 2026-08-02: v3 feedback: +5% of the viewport's width/height as extra breathing room on all four
  sides, on top of the v2 chrome clearance (still per-frame, so still resize-proof).
- 2026-08-02: v2 feedback (screenshot): margins now leave the game's chrome visible on all four
  sides - top clamp(screenH*0.095, 96, 140) clears the status strip + toast band, bottom clears the
  pinned Menu/Army Lists buttons via GetFrameHeight (UI-scale-aware), 16px sides - recomputed per
  frame so they hold through resizes. Also fixed from the screenshot: the card weapon table's SPE
  cell ran multi-rule weapons together ("Deadly(3)Limited") - RuleHoverText.RuleSegments carries no
  separators (its shoot-panel caller adds them); the SPE cell now interleaves ", ".
- 2026-08-02: v1 feedback tweak: no longer a true modal popup. Now a plain top window sized to the
  TABLE AREA (layout.AreaW) with 85% background alpha, so the right column (resolver panel, log,
  chat) stays visible and clickable beside it and the board ghosts through. Board input muting is
  unchanged (open state still ORs into EscapeRouter.BeginFrame); resolver keyboard hotkeys stay
  muted while open, but right-column clicks now work - the Action needed chip remains for
  canvas-based prompts.
- 2026-08-02: Slice 3 (table mode) implemented, app-only: Cards|Table radio pair in the overlay
  header (session-persistent static), condensed one-row-per-unit table (Unit/Stats/Loadout/Special
  Rules) with the same live states (points, wounds fraction, DESTROYED/Activated, token chips,
  joined-hero tag) and hoverable rules everywhere - loadout lines use the #292 weapon stat
  vocabulary, not the printout's. Card/table hero + rule-segment rendering deduped into
  DrawHeroSummary/RuleSegmentsFor. DEFERRED from the polish list (explicitly, until after GUI
  hand-verify shapes them): per-card collapse chevrons, per-model tough pips on multi-model tough
  units, caster spells section.
- 2026-08-02: Slice 2 implemented. Engine (submodule ae684f2): `UnitData.PointCost` from the file
  entry, hero cost folds into host at merge (HeroJoinResolver), Reinforcement copy keeps its twin's
  cost, `ArmyData.ArmyName/Faction/PointsLimit` set in CreateArmy - all serialized, so they sync and
  survive saves (3 tests in `ArmyPointsCarryTests`, incl. GameSaveSerializer round-trip). App: card
  header gains "- NNNpts", tab header line "Name - Faction" + "total / limit pts" (sums UnitData
  costs; pre-slice-2 saves just thin the line). DESIGN CHANGE from plan: army identity rides
  ArmyData engine-side instead of plumbing lobby ArmyListSummary through GameGuiWiring - works for
  scenario direct-launch and resumed saves too, no app plumbing. Engine suite 2653/2653, app
  1013/1013, headless smoke exit 0.
- 2026-08-02: Slice 1 implemented, all app-side (no engine changes). New: `ArmyListOverlay`
  (modal/tabs/cards), `ArmyListLayout` (pure packing/wrapping/formatting core, 12 tests in
  `ArmyListLayoutTests`), `UnitActivation` (HasActivated extracted from TableTooltipOverlay, now
  shared). Modified: TableTooltipOverlay + EscapeMenuOverlay + ViewSettings (labels L->N),
  RaylibRenderer (attach/draw/EscapeRouter OR/Army Lists button), GuiOutstandingTaskDisplay
  (LocalPlayerIDs exposed), ImGuiTheme (AccentBlue/InkWell for pills). Verified: full build clean,
  app suite 1013/1013, engine suite 2650/2650, headless smoke exit 0. GUI hand-verify checklist:
  L opens/closes (and Esc closes, second Esc opens menu); tabs colored, local player first; cards
  show pills / hoverable rules / weapon table; wound a unit and watch [live/start] + weapon counts
  shrink; kill a unit -> dimmed card + DESTROYED stamp; Activated tag during a round; joined-hero
  sub-block; token chips; "Action needed" chip while a prompt is pending; labels toggle now on N.
- 2026-08-02: Design proposed and signed off (fullscreen modal / L key with labels->N / cards first /
  points plumb approved).

## Decisions

- L was taken by the unit-label toggle; user chose to give L to the list and move labels to N rather
  than use a collision-free letter (I) — strongest mnemonic wins.
- Hold-to-peek (FPS scoreboard style) rejected: holding a key precludes hovering rules for tooltips.
- Docked panel rejected: cramps the multi-column card layout and collides with the right
  resolver/log column.
- Destroyed units stay visible (dimmed + stamped): on the enemy tab, "what's left" is half the value.
- Joined hero renders inside its host unit's card (in-game truth) rather than as the separate
  pre-merge entry the printed list shows.
- Per-unit points come from the engine plumb, NOT from matching compiled units back to army-file
  entries (unreliable after hero merges / combined units), and NOT from broadcasting full army files
  (redundant with compiled state, stale vs live state).
- Masonry packing uses the PREVIOUS frame's measured card heights (cached per UnitID), estimate on
  first sight — packing must happen before drawing, and a one-frame settle is invisible. The packer
  itself (shortest-column-first) is pure and tested.
- The overlay ORs into `EscapeRouter.BeginFrame`'s menu-open flag: while it covers the board, a
  hidden resolver dialog must NOT claim Esc (it would answer No invisibly) and canvas/resolver
  hotkeys must not fire. The overlay handles its own Esc/L directly, like the Esc menu.
- Opening L keeps the standard `WantCaptureKeyboard` toggle gate; closing only gates on
  `WantTextInput`, because the modal itself holds keyboard capture while open.
- Status tokens render as colored TEXT chips ("[Shaken]", hover for description) rather than the
  canvas's abstract shape chips — a card has room to say the name.

## Outcome

(open)
