#!/usr/bin/env bash
# #378 - bake the 40 Age of Fantasy .fdgbook bundles from the local OPR JSON snapshots.
#
# Usage: scripts/bake-aof-books.sh [snapshotDir]
#
# The snapshot dir lives OUTSIDE the repo (copyrighted OPR data is only redistributed as imported
# .fdgbook files under CC-BY-SA): default ~/Projects/GDF Armies/Age of Fantasy/opr-json-snapshots,
# pinned at the 2026-08-22 fetch (v3.5.3, the four Giant Tribes Disciples variants v3.5.2). Do not
# refetch casually - #375/#377's census + spell-parity verification ran against exactly these files.
#
# Each book bakes against GDF + AoF supplements, with the per-book AofBookOverrides/<Name>.json LAST
# so its redefinitions win (#375 C9). Output: FdgRaylib/Assets/Books/AoF-<CompactName>.fdgbook
# (the AoF- prefix keeps the four Disciples books from colliding with their GDF namesakes).
set -euo pipefail
cd "$(dirname "$0")/.."

SNAP="${1:-$HOME/Projects/GDF Armies/Age of Fantasy/opr-json-snapshots}"
BOOKS=FdgRaylib/Assets/Books
BIN=FdgRaylib/bin/Debug/net8.0/FdgRaylib

[ -d "$SNAP" ] || { echo "snapshot dir not found: $SNAP" >&2; exit 1; }
dotnet build FdgRaylib/FdgRaylib.csproj >/dev/null

for f in "$SNAP"/*.json; do
    name="$(basename "$f" .json)"
    [ "$name" = "_index" ] && continue
    out="$BOOKS/AoF-$(echo "$name" | tr -d ' -').fdgbook"
    supp=("$BOOKS/GdfRuleSupplement.json" "$BOOKS/AofRuleSupplement.json")
    if [ -f "$BOOKS/AofBookOverrides/$name.json" ]; then
        supp+=("$BOOKS/AofBookOverrides/$name.json")
    fi
    echo "== $name -> $out"
    "$BIN" --import-opr "$f" "$out" "${supp[@]}"
done
