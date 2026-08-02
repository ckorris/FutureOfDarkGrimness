# 309 — Pass is pinned to the bottom of the action menu and asks for confirmation

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #248 (Back / letter hotkeys / Esc-belongs-to-the-menu), #298 (line-height-derived row sizes), #295 (Confirm = Enter/Space)

## Goal

Passing ends an activation outright and cannot be undone, but in the Choose Action menu it was just the
last row of an ordinary option list — one row under Shoot, wherever the list happened to end. In a
2026-08-02 multiplayer game both players ended activations that still had actions left by clicking it.

Done looks like: Pass is out of the option list and pinned to the bottom of the resolver panel, clear of
both the actions above it and the panel's bottom edge; and choosing it (mouse or its X hotkey) opens a
confirmation where Enter/Space passes and Escape cancels.

## Notes

- 2026-08-02 (second hand-verify pass, user-driven): two more defects fixed.
  - The confirmation opened nearly screen-tall for one frame before snapping down. Cause: wrapped text
    inside an `AlwaysAutoResize` window is circular — the wrap width comes from the window width, which
    comes from the content — so on the appearing frame the message wrapped to a column of single words.
    The popup now pins its width (`SetNextWindowSize(w, 0)`, height auto-fit) and wraps at an explicit
    position, which breaks the loop and makes the first measured height correct.
  - Back moved BELOW Pass at the user's request; agreed on UX grounds — the panel's bottom edge is the
    easiest target to slam into, so it should belong to the harmless action, not the irreversible one.
- 2026-08-02: Implemented client-side, in `GuiStringSelectionResolver` + a new `ActionMenuLayout`.
  - The engine still describes the menu as a flat list of strings; the client decides how it reads,
    matching `Pass` by name the same way `ResolverHotkeys` pins its letters to the built-in action names.
    No engine change, no wire-format change.
  - The panel is now instructions / scrolling option list (its own child) / pinned footer. Only menus
    that actually offer Pass grow a footer — the weapon, spell and ability menus share this resolver and
    keep their original single-scroll layout, Back included.
  - Back is pinned above Pass (user sign-off), so both "leave this menu" actions stay reachable when the
    option list scrolls.
  - A greyed-out Pass (#197 Instinctive compels an attack) keeps the pinned spot with its reason, so the
    rows above never shift under the cursor depending on whether passing is legal (user sign-off).
  - Escape is claimed from `EscapeRouter` while the confirmation is up, so the same press cannot also
    open the in-game menu behind it. Every letter/Backspace hotkey is frozen while it is up — the modal
    blocks the mouse, but those keys are read straight from ImGui (#248's muting rule).
  - Tests: `FdgRaylib.Tests/ActionMenuLayoutTests.cs` (14 asserts over the vertical budget at both UI
    scales, including the invariant that Pass lands exactly one bottom-gap above the panel edge).
    App 870/870, engine 2550/2550, headless smoke exit 0.
- 2026-08-02: Hand-verified in the running app (shootout scenario): footer layout, confirmation popup,
  Escape cancelling without opening the in-game menu. Two defects found and fixed from that pass:
  the popup was centred with `ImGuiCond.Appearing`, which lands an `AlwaysAutoResize` window against the
  top of the screen (no size yet on the appearing frame) — now `Always`; and Pass was drawn in the
  `Deemphasized` style, which read as disabled rather than merely receding — now full strength.

## Decisions

- **Client-side name match, not engine metadata.** The alternative was a flag on `StringSelectionRequest`
  ("this option is destructive / pin it"). Rejected as speculative generality for a single option in a
  single menu: the presentation of a menu is the client's business, `ResolverHotkeys` already pins
  behaviour to these exact names, and `ChooseActionStage` refuses to let a custom action take the name
  `Pass`, so the match can only ever hit the real thing. If a second option ever needs the treatment,
  that is the moment to promote it to a request-level flag.
- **Footer only when Pass exists.** Pinning Back for every string menu would have been more uniform, but
  it would move the Back button in the weapon / spell / ability pickers, which nobody asked for.
- **No CLI change.** The CLI resolver takes typed input, which is not a misclick; adding a confirmation
  there would only add a keystroke to every pass.
- **Scrollbar-aware re-measure.** The option list is its own scrolling child now, so when its content
  overflows the rows are re-wrapped into the width left by the scrollbar. Without it a long label
  (a melee weapon's stat tail, #298) would be drawn underneath the bar.

## Outcome

_Open._
