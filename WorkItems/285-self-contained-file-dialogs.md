# 285 — Self-contained file dialogs (drop the zenity dependency)

**Status**: todo
**Related**: TinyDialogsNet call sites: `ArmyBuilderScreen`, `ArmyForgeScreen`, `LobbyScreen`, `EscapeMenuOverlay`, `Program.cs`. Short-term mitigation in `scripts/build-dist.sh` (Linux README).

## Goal

File open/save dialogs work out of the box on Linux with no package install. Today
TinyDialogsNet (tinyfiledialogs) shells out to an external helper — typically zenity — and
when none is installed (common on minimal Arch/i3 setups) the dialogs silently do nothing.
Done means: a fresh minimal Linux install can save/load armies and saves from the GUI with
zero extra packages, and a missing-helper situation is never a silent no-op.

## Notes

- 2026-07-26: Filed from Arch user feedback on the July dist build: they had to install
  zenity manually and suggested "including GTK in the app" long-term. Short-term half
  shipped alongside this filing: the Linux dist README now lists `sudo pacman -S zenity`
  next to the apt/dnf lines (`scripts/build-dist.sh`). They also noted zenity dialogs
  "play nicely with i3" — floating dialogs behave well under tiling WMs.

## Decisions

- **Bundling GTK or the zenity binary is rejected.** GTK's dependency tree (glib, pango,
  cairo, gdk-pixbuf, schemas, themes) is tens of MB and fragile to ship self-contained;
  a bundled zenity binary still needs system GTK libraries, so it moves the dependency
  rather than removing it. xdg-desktop-portal (DBus FileChooser) also rejected: minimal
  i3/Arch setups often lack a portal backend, so it fails in exactly the reported case.
- **Design fork (open, needs sign-off before building):**
  - (a) In-app ImGui file picker as a *fallback* — try TinyDialogs, fall back when it
    fails or no helper binary is found on PATH. Keeps native dialogs where they exist;
    needs reliable detection (tinyfiledialogs reports failure like a cancel).
  - (b) In-app ImGui picker as the *primary* on all platforms — one code path, consistent
    look, trivially testable; loses native look and OS bookmarks/favorites.
  - Either way the five call sites should go through one shared dialog seam rather than
    calling `TinyDialogs.*` directly, so the choice is made in one place.

## Outcome

(open)
