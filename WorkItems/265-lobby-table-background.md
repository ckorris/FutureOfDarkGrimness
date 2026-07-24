# 265 — Lobby battlefield (table background) picker

**Status**: done
**Related**: #201 (the setting-plumbing pattern this copies), #167 (scenario tools), #052 (save/resume)

## Goal
A lobby dropdown that picks the table's look — Forest (today's green board), Desert, Ice, Mars-Like,
Urban, Barren — that every player at the table sees the same way and that a saved game resumes with.
Cosmetic only: no rule, terrain placement, or AI evaluation may read it.

## Notes

- 2026-07-23: Shipped. Six surfaces hand-verified in the running app (one `--scenario` launch each,
  screenshotted); the lobby dropdown verified open with all six labels and a pick sticking.

## Decisions

- **The setting lives in `GameSettings`, not in a local preference file.** The ask was explicitly
  "works over the network and saves/loads", and `GameSettings` already gets both for free: the host
  broadcasts the whole struct in `LobbyGameSettingsUpdate` on every change, and `GameProgressData`
  carries it into the save. A per-client preference would have needed neither, but then two players
  would be looking at different boards, and a resumed game would not look like the one put down.

- **`ETableBackground.Forest` is the zero value.** A pre-#265 save has no such field in its JSON, so
  it deserializes to `default` — which must be the green board those games were actually played on.
  Pinned by a test rather than left to enum-ordering luck.

- **The value reaches the renderer on `GuiResolverOverlay`**, stamped in `ResolverRegistryFactory
  .BuildGui`, exactly as #201's cover-proximity setting does. Nothing in the resolvers reads it; the
  alternative was a ninth positional parameter on the `GameLaunchedHandler` delegate and its two call
  sites, which buys nothing. Both launch paths (lobby and the `--scenario` direct launch) already had
  the #201 argument in hand, so each grew one line.

- **Mottle stays additive with a dark tint.** The original grass flecks are a Perlin patch blended
  additively; keeping that for all six means the noise only ever lifts the surface. An additive bright
  tint blows the pale surfaces (Ice, Desert) out to white — `TableBackgroundTests` pins the tint
  ceiling so a later palette tweak cannot reintroduce that. Each style also carries its own noise
  scale and sample offset (fine and organic for grass, coarse and blotchy for concrete), so the
  texture is regenerated when the background changes rather than baked once.

- **Urban's edge trim is steel, not the shared warm brown.** The brown frame reads as a mistake
  against grey; the trim is per-style for that reason.

- **Scenario JSON gained an optional `settings.background`.** Not asked for, but the scenario tools
  (#167) are how a surface gets eyeballed without clicking through a lobby, and the field is how the
  six screenshots above were taken. Unknown names throw at compile time with the valid list.

## Outcome

Shipped end to end. Engine: `ETableBackground` + `GameSettings.TableBackground` (Forest = default and
the value old saves resolve to), `ILobbyViewModel.TableBackground` / `SetTableBackground` with the
host broadcasting and a client refusing to set it, and `settings.background` on the scenario file.
App: `TableBackgrounds` maps each value to surface / grid / trim / mottle; `RaylibRenderer` paints
from that style and regenerates its noise texture when the surface changes; the lobby's Battlefield
dropdown sits under Turn Style, host-only like every other setting.

Tests: `LobbyTableBackgroundSyncTests` (6, real host + client over the loopback: default, sync, every
value, undefined rejected, client-set throws), `GameProgressTests` (save round-trip + the old-save
default), `ScenarioCompilerTests` (case-insensitive parse, unknown throws), `TableBackgroundTests` (7,
palette invariants + ASCII labels). Engine 2030 green, app 555 green, headless smoke exit 0.

Verified by hand in the app: all six surfaces screenshotted via `--scenario`, terrain legibility
checked on the palest one (Ice), and the dropdown opened in a live lobby with a pick applied. **Not
verified on two machines** — the host->client sync is covered by the loopback test only.
