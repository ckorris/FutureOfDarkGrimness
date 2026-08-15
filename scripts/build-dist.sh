#!/usr/bin/env bash
#
# build-dist.sh - Produce self-contained, sendable builds of FdgRaylib for
# Windows, Linux, and macOS.
#
# Self-contained means the recipient needs NO .NET install. Cross-compiles EVERY
# target from whatever OS you run this on (Linux or Windows) - including the macOS
# builds, which need no Mac to produce. They are unsigned (no Apple Developer
# account), so Mac users clear one Gatekeeper flag on first run - see the macOS
# README the script writes.
#
# Usage:
#   scripts/build-dist.sh              # build all targets (win-x64, linux-x64, osx-arm64, osx-x64)
#   scripts/build-dist.sh win          # Windows only
#   scripts/build-dist.sh linux        # Linux only
#   scripts/build-dist.sh mac          # both macOS arches (Apple Silicon + Intel)
#   scripts/build-dist.sh mac-arm      # macOS Apple Silicon only
#   scripts/build-dist.sh mac-x64      # macOS Intel only
#
# Each output folder gets the published app plus a platform README.txt, the
# third-party license notices, and a copy of the repo's armies/ sample lists.
#
# Output lands in dist/:
#   dist/FdgRaylib-win-x64/     + FdgRaylib-win-x64-<RULES_VERSION>.zip
#   dist/FdgRaylib-linux-x64/   + FdgRaylib-linux-x64-<RULES_VERSION>.tar.gz
#   dist/FdgRaylib-osx-arm64/   + FdgRaylib-osx-arm64-<RULES_VERSION>.tar.gz
#   dist/FdgRaylib-osx-x64/     + FdgRaylib-osx-x64-<RULES_VERSION>.tar.gz
#
set -euo pipefail

# --- resolve repo root (script lives in <root>/scripts) -----------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$ROOT_DIR/FdgRaylib/FdgRaylib.csproj"
DIST_DIR="$ROOT_DIR/dist"

# Release archives are stamped with the OnePageRules rules-set version the build
# implements, e.g. FdgRaylib-win-x64-OPR_3_5_1.zip. Override: RULES_VERSION=... scripts/build-dist.sh
RULES_VERSION="${RULES_VERSION:-OPR_3_5_1}"

# Trimming is intentionally OFF: the app uses Newtonsoft with TypeNameHandling.Auto
# (reflection-based) for .fdgarmy files, which a trimmer would break.
COMMON_ARGS=(-c Release --self-contained true -p:PublishTrimmed=false)

# #226: stamp the build so bug reports can be tied to the binary that produced them
# (surfaced via AppVersion.cs; non-dist builds report the csproj default "dev").
GIT_SHA="$(git -C "$ROOT_DIR" rev-parse --short HEAD 2>/dev/null || echo unknown)"
GIT_STAMP="git-$GIT_SHA-$(date -u +%Y%m%d)"
if ! git -C "$ROOT_DIR" diff --quiet 2>/dev/null; then GIT_STAMP="$GIT_STAMP-dirty"; fi
COMMON_ARGS+=("-p:InformationalVersion=$GIT_STAMP")
echo ">> Build stamp: $GIT_STAMP"

# --- which targets? -----------------------------------------------------------
# No arg = build everything. An explicit arg turns all off, then enables the pick.
BUILD_WIN=1
BUILD_LINUX=1
BUILD_MAC_ARM=1
BUILD_MAC_X64=1
if [[ $# -gt 0 ]]; then
  BUILD_WIN=0; BUILD_LINUX=0; BUILD_MAC_ARM=0; BUILD_MAC_X64=0
  case "$1" in
    win|windows)                 BUILD_WIN=1 ;;
    linux)                       BUILD_LINUX=1 ;;
    mac|macos)                   BUILD_MAC_ARM=1; BUILD_MAC_X64=1 ;;
    mac-arm|mac-arm64|osx-arm64) BUILD_MAC_ARM=1 ;;
    mac-x64|mac-intel|osx-x64)   BUILD_MAC_X64=1 ;;
    *) echo "Unknown target '$1' (expected: win | linux | mac | mac-arm | mac-x64)" >&2; exit 2 ;;
  esac
fi

echo ">> Cleaning $DIST_DIR"
rm -rf "$DIST_DIR"
mkdir -p "$DIST_DIR"

# --- helper: publish one RID and package it -----------------------------------
publish_one() {
  local rid="$1" name="$2"
  local out="$DIST_DIR/$name"

  echo
  echo ">> Publishing $rid -> $out"
  dotnet publish "$PROJECT" "${COMMON_ARGS[@]}" -r "$rid" -o "$out"

  # Drop a recipient-facing README with the platform gotchas.
  write_readme "$rid" "$out"

  # Ship the third-party license notices next to the binary. The bundled MIT/BSD
  # components (Mono.Nat, Newtonsoft.Json, ImGui.NET, TinyDialogsNet) require their
  # notices to travel with any distribution; keep this in the archive.
  if [[ -f "$ROOT_DIR/THIRD-PARTY-NOTICES.txt" ]]; then
    cp "$ROOT_DIR/THIRD-PARTY-NOTICES.txt" "$out/THIRD-PARTY-NOTICES.txt"
  else
    echo "!! THIRD-PARTY-NOTICES.txt missing at repo root - license notices will NOT ship" >&2
  fi

  # Ship the sample army lists. The name and location are load-bearing: ArmyPaths
  # looks for an "armies" folder beside the executable and opens every .fdgarmy
  # Save/Load dialog there, so a recipient's Load Army lands straight on these.
  if [[ -d "$ROOT_DIR/armies" ]]; then
    cp -R "$ROOT_DIR/armies" "$out/armies"
  else
    echo "!! armies/ missing at repo root - no sample army lists will ship" >&2
  fi
}

write_readme() {
  local rid="$1" out="$2"
  if [[ "$rid" == win-* ]]; then
    cat > "$out/README.txt" <<'EOF'
Future of Dark Grimness - Windows build
=======================================

To run: double-click FdgRaylib.exe (or run it from a terminal).

No .NET install is required - the runtime is bundled.

First launch: Windows SmartScreen may show "Windows protected your PC"
because the app is not code-signed. Click "More info" -> "Run anyway".
This is expected for an unsigned app and is safe.

Sample army lists are in the "armies" folder - pick one from the lobby's
"Load Army" button.

Keep the whole folder together - FdgRaylib.exe needs the DLLs and the
Assets folder next to it.
EOF
  elif [[ "$rid" == osx-* ]]; then
    cat > "$out/README.txt" <<'EOF'
Future of Dark Grimness - macOS build
=====================================

This build is NOT code-signed (no paid Apple Developer account), so macOS
Gatekeeper blocks it on first run. This is expected for any unsigned app and
is safe. Allowing it takes one command.

To run: open Terminal, drag this folder onto the Terminal window to cd into
it (or "cd" to it yourself), then run:

    xattr -dr com.apple.quarantine .
    ./FdgRaylib

The first line clears the "downloaded from the internet" quarantine flag from
the app AND all its bundled .dylib libraries at once (each one is checked by
Gatekeeper, so the recursive -r matters). Without it macOS refuses to open
unsigned code. Right-click -> Open in Finder can work too, but on recent macOS
the xattr command is the reliable way.

If the launcher is not executable (e.g. it was unzipped oddly):
    chmod +x FdgRaylib

No .NET install is required - the runtime is bundled. File open/save dialogs
(Army Builder) work out of the box - they use macOS's built-in osascript, so
there is nothing extra to install.

Which build is this?
  osx-arm64 = Apple Silicon Macs (M1/M2/M3/M4 - any Mac from late 2020 on).
  osx-x64   = older Intel Macs.
Use the one matching the Mac. An Intel build also runs on Apple Silicon under
Rosetta 2, but the arm64 build is native and faster.

Sample army lists are in the "armies" folder - pick one from the lobby's
"Load Army" button.

Keep the whole folder together - the binary needs its native .dylib libraries
and the Assets folder next to it.
EOF
  else
    cat > "$out/README.txt" <<'EOF'
Future of Dark Grimness - Linux build (x64)
===========================================

To run:
    ./FdgRaylib

No .NET install is required - the runtime is bundled.

If the file is not executable (e.g. it was unzipped rather than untarred):
    chmod +x FdgRaylib

File open/save dialogs (Army Builder) use zenity. If those dialogs do
nothing, install it:
    sudo apt install zenity      # Debian/Ubuntu
    sudo dnf install zenity      # Fedora
    sudo pacman -S zenity        # Arch

Requires a normal desktop with OpenGL + X11/Wayland (present on any
standard Ubuntu/Fedora install). Built against glibc; Alpine/musl is not
supported.

Sample army lists are in the "armies" folder - pick one from the lobby's
"Load Army" button.

Keep the whole folder together - the binary needs its native libraries
and the Assets folder next to it.
EOF
  fi
}

# --- Windows ------------------------------------------------------------------
if [[ $BUILD_WIN -eq 1 ]]; then
  publish_one "win-x64" "FdgRaylib-win-x64"
  echo ">> Zipping FdgRaylib-win-x64-$RULES_VERSION.zip"
  ( cd "$DIST_DIR" && zip -rq "FdgRaylib-win-x64-$RULES_VERSION.zip" "FdgRaylib-win-x64" )
fi

# --- Linux --------------------------------------------------------------------
if [[ $BUILD_LINUX -eq 1 ]]; then
  publish_one "linux-x64" "FdgRaylib-linux-x64"
  # Ensure the launcher is executable, then tar (tar preserves the +x bit).
  chmod +x "$DIST_DIR/FdgRaylib-linux-x64/FdgRaylib"
  echo ">> Tarring FdgRaylib-linux-x64-$RULES_VERSION.tar.gz"
  tar -czf "$DIST_DIR/FdgRaylib-linux-x64-$RULES_VERSION.tar.gz" -C "$DIST_DIR" "FdgRaylib-linux-x64"
fi

# --- macOS --------------------------------------------------------------------
# tar.gz (not zip) so the +x bit on the launcher and dylibs survives the trip to
# a Mac; macOS unpacks .tar.gz on double-click just like Linux does.
if [[ $BUILD_MAC_ARM -eq 1 ]]; then
  publish_one "osx-arm64" "FdgRaylib-osx-arm64"
  chmod +x "$DIST_DIR/FdgRaylib-osx-arm64/FdgRaylib"
  echo ">> Tarring FdgRaylib-osx-arm64-$RULES_VERSION.tar.gz"
  tar -czf "$DIST_DIR/FdgRaylib-osx-arm64-$RULES_VERSION.tar.gz" -C "$DIST_DIR" "FdgRaylib-osx-arm64"
fi

if [[ $BUILD_MAC_X64 -eq 1 ]]; then
  publish_one "osx-x64" "FdgRaylib-osx-x64"
  chmod +x "$DIST_DIR/FdgRaylib-osx-x64/FdgRaylib"
  echo ">> Tarring FdgRaylib-osx-x64-$RULES_VERSION.tar.gz"
  tar -czf "$DIST_DIR/FdgRaylib-osx-x64-$RULES_VERSION.tar.gz" -C "$DIST_DIR" "FdgRaylib-osx-x64"
fi

# --- summary ------------------------------------------------------------------
echo
echo ">> Done. Artifacts in $DIST_DIR:"
for f in "$DIST_DIR"/*.zip "$DIST_DIR"/*.tar.gz; do
  [[ -e "$f" ]] && printf '   %-32s %s\n' "$(basename "$f")" "$(du -h "$f" | cut -f1)"
done
