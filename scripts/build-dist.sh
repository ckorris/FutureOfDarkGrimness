#!/usr/bin/env bash
#
# build-dist.sh - Produce self-contained, sendable builds of FdgRaylib for
# Windows and Linux (both x64).
#
# Self-contained means the recipient needs NO .NET install. Cross-compiles both
# targets from whatever OS you run this on (Linux or Windows), so you don't need
# to switch machines.
#
# Usage:
#   scripts/build-dist.sh              # build both win-x64 and linux-x64
#   scripts/build-dist.sh win          # build only Windows
#   scripts/build-dist.sh linux        # build only Linux
#
# Output lands in dist/:
#   dist/FdgRaylib-win-x64/     + FdgRaylib-win-x64.zip
#   dist/FdgRaylib-linux-x64/   + FdgRaylib-linux-x64.tar.gz
#
set -euo pipefail

# --- resolve repo root (script lives in <root>/scripts) -----------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$ROOT_DIR/FdgRaylib/FdgRaylib.csproj"
DIST_DIR="$ROOT_DIR/dist"

# Trimming is intentionally OFF: the app uses Newtonsoft with TypeNameHandling.Auto
# (reflection-based) for .fdgarmy files, which a trimmer would break.
COMMON_ARGS=(-c Release --self-contained true -p:PublishTrimmed=false)

# --- which targets? -----------------------------------------------------------
BUILD_WIN=1
BUILD_LINUX=1
if [[ $# -gt 0 ]]; then
  case "$1" in
    win|windows)  BUILD_LINUX=0 ;;
    linux)        BUILD_WIN=0 ;;
    *) echo "Unknown target '$1' (expected: win | linux)" >&2; exit 2 ;;
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

Keep the whole folder together - FdgRaylib.exe needs the DLLs and the
Assets folder next to it.
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

Requires a normal desktop with OpenGL + X11/Wayland (present on any
standard Ubuntu/Fedora install). Built against glibc; Alpine/musl is not
supported.

Keep the whole folder together - the binary needs its native libraries
and the Assets folder next to it.
EOF
  fi
}

# --- Windows ------------------------------------------------------------------
if [[ $BUILD_WIN -eq 1 ]]; then
  publish_one "win-x64" "FdgRaylib-win-x64"
  echo ">> Zipping FdgRaylib-win-x64.zip"
  ( cd "$DIST_DIR" && zip -rq "FdgRaylib-win-x64.zip" "FdgRaylib-win-x64" )
fi

# --- Linux --------------------------------------------------------------------
if [[ $BUILD_LINUX -eq 1 ]]; then
  publish_one "linux-x64" "FdgRaylib-linux-x64"
  # Ensure the launcher is executable, then tar (tar preserves the +x bit).
  chmod +x "$DIST_DIR/FdgRaylib-linux-x64/FdgRaylib"
  echo ">> Tarring FdgRaylib-linux-x64.tar.gz"
  tar -czf "$DIST_DIR/FdgRaylib-linux-x64.tar.gz" -C "$DIST_DIR" "FdgRaylib-linux-x64"
fi

# --- summary ------------------------------------------------------------------
echo
echo ">> Done. Artifacts in $DIST_DIR:"
for f in "$DIST_DIR"/*.zip "$DIST_DIR"/*.tar.gz; do
  [[ -e "$f" ]] && printf '   %-32s %s\n' "$(basename "$f")" "$(du -h "$f" | cut -f1)"
done
