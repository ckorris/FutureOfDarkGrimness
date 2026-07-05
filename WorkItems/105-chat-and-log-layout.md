# 105 — Improve in-game chat + relocate the log panel

**Status**: todo
**Related**: follows #077 (minimal in-game chat shipped on `082-default-answer`); touches the renderer layout (#056 presentation work is adjacent)

## Goal

#077 shipped a deliberately minimal in-game chat: sent from a thin bar at the bottom of the game area, received lines interleaved into the same right-side `GameLog` as every engine log message. It works but is rough. This item makes chat a first-class, legible feature and rethinks where the log lives so neither competes for the same cramped space. "Done" = chat is readable and usable as its own thing (not lost in log spam), the player can pick who they're talking to, and the on-screen layout for log + chat feels intentional rather than "a fixed strip on the right plus a bar at the bottom."

## Scope (fragment when picked up)

**Chat improvements**
- Separate chat from the engine log — a dedicated chat view/panel (or at least a filtered tab), so messages aren't buried between stage-change log lines. Keep scrollback + autoscroll-when-at-bottom (mirror `DrawLogPanel`).
- Channel selection: **Global vs Team**. The relay already supports `EChatMessageType.Team` (`LogAndChatMessageRelayer.SendTeamPlayerMessage`, `LogChatMessageEndpoint` team filtering) but the GUI always sends `Global` — add a channel toggle so Team chat is reachable.
- Sender coloring: tag each line with the sender's player-palette colour (the renderer already has `colorForPlayer`), instead of one flat chat colour.
- De-dup the **networked-host self-echo**: a host currently displays its own sent line once via `LocalPlayerController.SendPlayerMessage` AND again via the relay's local dispatch of the broadcast `PlayerChatNetworkMessage` (a pre-existing relay quirk surfaced by #077's GUI; only bites with ≥1 network client). Decide the canonical display path and suppress the duplicate.
- Nice-to-haves: timestamps, an unread indicator when the panel is hidden, a show/hide toggle, a brief fade-in for new lines.

**Log relocation / layout**
- The log is a fixed full-height `LogPanelWidth` strip on the right (`RaylibRenderer.DrawLogPanel` + `ComputeLayout` reserving `logW`), and chat is a separate bottom bar. Reconsider holistically: e.g. a collapsible/resizable dock, a tabbed bottom panel combining Log + Chat, or moving the log so it stops permanently eating horizontal table space. Surface 2–3 layout options with mockups and get sign-off before building (per the "surface design forks" convention).

## Decisions

- **2026-07-05 — Layout: bottom console.** Chose a collapsible, tabbed (Log | Chat) dock across the
  bottom over the two right-side options (tabbed / split right dock). Rationale: the user wants a
  full-width table; accepted the tradeoff that on a height-bound wide window the bottom dock shrinks the
  board somewhat (the right strip was otherwise "free" horizontal margin), in exchange for a roomy,
  dedicated console. Chat quality upgrades (separate chat store, Global/Team toggle, sender-palette
  colours, host self-echo de-dup) are layout-independent and apply here too.

## Notes

- **2026-07-05 (later)** — Iterated on tester feedback. Console model changed from exclusive Log/Chat
  **tabs** to independent **toggles** (Chat button on the left): with both on, the two sources merge into
  one column in arrival order. Needed a shared monotonic `LogEntry.Sequence` (a static counter across all
  `GameLog` instances) to merge-sort correctly; the console does a two-pointer merge of the two
  already-ordered snapshots. Also (general GUI polish landed on this branch while iterating): scoreboard
  HUD lost its background panel (drop-shadow text instead; it never captured the mouse anyway), resolver
  dialogs right-aligned into the now-open right space, table auto-fit zoomed ~15%, measurement ruler now
  clears the instant both Ctrl and LMB are released.
- **2026-07-05** — Slice 1 implemented (builds clean; awaiting GUI hand-verification). Bottom console:
  full-width table + a collapsible bottom dock with Log/Chat tabs (`RaylibRenderer.DrawBottomConsole`);
  `ComputeLayout` now reserves bottom height instead of a right strip, `Layout.LogX` -> `AreaW`. Chat split
  into its own store: `GuiPlayerMessageUI` owns a `ChatLog` (was writing into the engine `GameLog`), takes
  a name->colour func built in `LobbyScreen.HandleLaunch` from the roster (sender-palette colours). Global/
  Team channel toggle in the input row; unread `*` on the Chat tab. Old `DrawLogPanel`/`DrawChatInput`
  removed. **Deferred to next slices:** host self-echo de-dup (needs relay look), timestamps, new-line
  fade-in. Sender colour is name-keyed (could collide on duplicate names); a PlayerID pass-through would be
  the robust upgrade if that ever bites.
- **2026-07-05** — Picked up on branch `105-chat-and-log-layout`. Layout chosen (bottom console; see
  Decisions).
- **2026-06-25** — Opened. Current state to build on: `FdgRaylib/Rendering/GuiPlayerMessageUI.cs` (chat sink → `GameLog`), `RaylibRenderer.DrawChatInput` (bottom input bar) + `DrawLogPanel`/`ComputeLayout` (right-side log), and the engine relay (`LogAndChatMessageRelayer` / `LogChatMessageEndpoint` / `NetworkPlayerController`) already supports Global+Team. App-side work, except possibly small relay tweaks for the self-echo de-dup.
