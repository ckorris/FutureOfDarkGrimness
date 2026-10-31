#!/usr/bin/env bash
# #438 - bake every bundled OPR army book (Grimdark Future + Age of Fantasy) from the off-repo Army Forge JSON
# snapshots in ONE command, then run the post-bake curation passes.
#
# Usage: scripts/bake-books.sh [gdfSnapshotDir] [aofSnapshotDir] [outDir]
#   gdfSnapshotDir  default ~/Projects/GDF Armies/opr-json-snapshots-3.6.0-refetch-2026-10-30
#   aofSnapshotDir  default ~/Projects/GDF Armies/Age of Fantasy/opr-json-snapshots-3.6.0-refetch-2026-10-30
#   outDir          default FdgRaylib/Assets/Books (pass a scratch dir to dry-run a bake out of tree)
# Env: OFFLINE=1 skips the one live-API pass (--import-section-shapes).
#
# The snapshot dirs live OUTSIDE the repo (copyrighted OPR data is only redistributed as imported .fdgbook files
# under CC-BY-SA): one `<Book Name>.json` per /api/army-books/<uid>?gameSystem=N dump plus `_index.json`. Never copy
# them into the repo. The shipped books were baked from the 2026-10-30 re-fetch (WorkItems/438-rebake-books-3-6.md
# records the provenance).
#
# What it does, in order:
#   1. --import-opr every snapshot. GDF books bake against GdfRuleSupplement.json; AoF books against GDF + AoF +
#      AofBookOverrides/<OPR book name>.json LAST when that file exists (none ships since #435; the hook stays).
#      Output names: <CompactName>.fdgbook (GDF) / AoF-<CompactName>.fdgbook (AoF), compact = spaces and dashes
#      dropped - the AoF- prefix keeps the Disciples books from colliding with their GDF namesakes. The importer
#      skips OPR's 0-unit parent entries ("Wormhole Daemons", "Rift Daemons") itself and writes nothing.
#   2. Post-bake passes (the order the 3.5 books received them):
#      --import-book            SKIPPED: every 3.6.0 (unit, option) pair carries a per-unit price (#437 D7).
#      --import-section-shapes  live API, per-model section shapes (#383); a no-op on a fresh import.
#      --retrofit-effects       faction default effect sets (#239); the import already stamps them.
#      --retrofit-bases         base orientation (#225); the importer is already length-first, so a no-op.
#      --import-spells          spells re-stamped from the same snapshots (#377); a no-op on a fresh import.
#   3. --rule-coverage over outDir (the summary should read Dead: 0) and a list of any .fdgbook in outDir this bake
#      did not write (a stale book from an older bake - delete or git rm it by hand).
#
# Replaces scripts/bake-aof-books.sh (#378, AoF only; retired by #438).
set -euo pipefail
cd "$(dirname "$0")/.."

GDF_SNAP="${1:-$HOME/Projects/GDF Armies/opr-json-snapshots-3.6.0-refetch-2026-10-30}"
AOF_SNAP="${2:-$HOME/Projects/GDF Armies/Age of Fantasy/opr-json-snapshots-3.6.0-refetch-2026-10-30}"
OUT="${3:-FdgRaylib/Assets/Books}"
SUPP=FdgRaylib/Assets/Books
BIN=FdgRaylib/bin/Debug/net8.0/FdgRaylib

for d in "$GDF_SNAP" "$AOF_SNAP"; do
    [ -d "$d" ] || { echo "snapshot dir not found: $d" >&2; exit 1; }
done
mkdir -p "$OUT"
dotnet build FdgRaylib/FdgRaylib.csproj >/dev/null

declare -a written=()
declare -A snapshot_of=()

# bake <system-prefix> <snapshot.json> <supplement...>
bake() {
    local prefix="$1" json="$2"; shift 2
    local name out log
    name="$(basename "$json" .json)"
    out="$OUT/${prefix}$(echo "$name" | tr -d ' -').fdgbook"
    log="$("$BIN" --import-opr "$json" "$out" "$@")"
    echo "$log" | sed "s|^|    |"
    if echo "$log" | grep -q "^Skipped "; then return 0; fi
    written+=("$out")
    snapshot_of["$out"]="$json"
}

echo "== 1. import (GDF: $GDF_SNAP)"
for f in "$GDF_SNAP"/*.json; do
    [ "$(basename "$f")" = "_index.json" ] && continue
    bake "" "$f" "$SUPP/GdfRuleSupplement.json"
done
echo "== 1. import (AoF: $AOF_SNAP)"
for f in "$AOF_SNAP"/*.json; do
    [ "$(basename "$f")" = "_index.json" ] && continue
    supp=("$SUPP/GdfRuleSupplement.json" "$SUPP/AofRuleSupplement.json")
    override="$SUPP/AofBookOverrides/$(basename "$f")"
    [ -f "$override" ] && supp+=("$override")
    bake "AoF-" "$f" "${supp[@]}"
done
echo "== baked ${#written[@]} book(s)"

echo "== 2a. --import-book: skipped (3.6.0 snapshots price every option per unit, #437 D7)"

if [ "${OFFLINE:-0}" = "1" ]; then
    echo "== 2b. --import-section-shapes: skipped (OFFLINE=1)"
else
    echo "== 2b. --import-section-shapes (live Army Forge API)"
    "$BIN" --import-section-shapes "$OUT" | grep -v ": stamped 0 per-model section(s), 0 section(s) not in the live book." || true
fi

echo "== 2c. --retrofit-effects"
"$BIN" --retrofit-effects "${written[@]}" | grep -v "^  unchanged: " || true

echo "== 2d. --retrofit-bases"
"$BIN" --retrofit-bases "${written[@]}" | grep -v "^  unchanged: " || true

echo "== 2e. --import-spells"
stamped=0; changed=0
for out in "${written[@]}"; do
    line="$("$BIN" --import-spells "$(dirname "${snapshot_of[$out]}")" "$out")"
    # the importer's "Spell ... skipped" warnings repeat here (already printed by step 1) - filtered
    echo "$line" | grep -v "^Done: " | grep -v "(unchanged)$" | grep -v "^  Spell '.*' skipped: " | sed "s|^|  |" || true
    echo "$line" | grep -q "(unchanged)$" || changed=$((changed + 1))
    n="$(echo "$line" | sed -n 's/^Done: .*, \([0-9]*\) spell(s)\.$/\1/p')"
    stamped=$((stamped + ${n:-0}))
done
echo "  Spell re-stamp complete: ${#written[@]} book(s) checked, $stamped spell(s) stamped, $changed book(s) changed."

echo "== 3. --rule-coverage $OUT"
"$BIN" --rule-coverage "$OUT" | tail -4
stale=0
for b in "$OUT"/*.fdgbook; do
    keep=0
    for w in "${written[@]}"; do [ "$w" = "$b" ] && keep=1 && break; done
    if [ "$keep" = "0" ]; then echo "  not written by this bake (stale?): $b"; stale=$((stale + 1)); fi
done
echo "== done: ${#written[@]} book(s) baked, $stale stale .fdgbook file(s) in $OUT"
