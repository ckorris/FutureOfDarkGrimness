# 266 — Console word-wrap + more room for resolver panels

**Status:** implemented 2026-07-23, awaiting GUI hand-verify (both facets are pixels, so neither can be verified headlessly)
**Related:** #161 (resolver UI consistency), #229 (bottom menu panel)

## Report

Two complaints about the in-game right column, from the same 2026-07-23 session:

1. The chat/log console has a **horizontal scroll bar**. Long lines should word-wrap onto the next line
   instead of running off to the right.
2. The console starts too high. Drop its top by roughly **10% of the screen height** and let the request
   resolvers use the space that frees up - **especially shooting**, whose weapon/target/detail sections
   were cramped.

## Root cause (facet 1)

Not a missing wrap - `RaylibRenderer.RenderConsoleLine` has always drawn each entry with `ImGui.TextWrapped`.
The scrollback child was opened with `ImGuiWindowFlags.HorizontalScrollbar`, and ImGui widens a
horizontally-scrollable window's work rect to its *content* size. `TextWrapped` wraps against that work
rect, so the wrap width grew to match whatever the longest line already was - the wrap could never bite,
and every long line just extended the scroll range instead. Self-perpetuating: the flag both caused the
overflow and hid the fix.

## Fix

- **Facet 1** — drop `HorizontalScrollbar` from the `##consolescroll` child. The wrap width becomes the
  visible column and long lines spill to the next row. One-flag change; no change to `RenderConsoleLine`.
- **Facet 2** — the split was the literal `screenH / 2` in `RaylibRenderer`. It is now
  `ResolverPanelLayout.ScreenHeightFraction` (0.60), a named constant next to the rest of the panel
  geometry, so the console takes the bottom 40%.

**Facet 2 needed no per-resolver work.** Every docked resolver already sizes itself from
`ResolverPanelLayout.H` - shooting (`GuiChooseRangedAttackResolver`) divides that height into three
stacked scrolling sections, and the selection / wounds / spell / cast-assist / melee-defender dialogs all
read `dh = ResolverPanelLayout.H` - so raising the fraction widened every one of them at once. Verified by
grep across `FdgRaylib/Rendering/Resolvers/`: the only hard-coded height left is the 118px `##DeployStats`
block in `GuiPlaceObjectsResolver`, which is a fixed-size stat readout rather than a scroll region and was
deliberately left alone.

## Notes

- 2026-07-23 — implemented. App-side only, no engine change. App 548/548, engine 2023/2023, build clean
  (0 warnings), headless smoke exit 0. None of that touches the pixels: **needs a GUI hand-verify** -
  (a) post a long chat line and a long log line, confirm they wrap and no horizontal scrollbar appears;
  (b) open a shooting prompt and confirm the weapon/target/detail sections are visibly taller and the
  console top sits lower.
