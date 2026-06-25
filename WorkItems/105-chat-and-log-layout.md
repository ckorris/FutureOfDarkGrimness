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

- (none yet — opened 2026-06-25 at the user's request.)

## Notes

- **2026-06-25** — Opened. Current state to build on: `FdgRaylib/Rendering/GuiPlayerMessageUI.cs` (chat sink → `GameLog`), `RaylibRenderer.DrawChatInput` (bottom input bar) + `DrawLogPanel`/`ComputeLayout` (right-side log), and the engine relay (`LogAndChatMessageRelayer` / `LogChatMessageEndpoint` / `NetworkPlayerController`) already supports Global+Team. App-side work, except possibly small relay tweaks for the self-echo de-dup.
