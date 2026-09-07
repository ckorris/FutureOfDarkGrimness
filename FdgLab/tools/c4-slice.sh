#!/bin/bash
# #191 step 15 (C4 integration slice, plan sec 10 / campaign doc step 15). Plays the named arms on the
# four low-ceiling ring pairs of the 2k pool, paired seeds (same seeds every arm), one bench cell per
# (pair, arm), arms interleaved per pair so an interruption leaves balanced data. Self-play is paused
# for the whole run (wall-clock search budgets are only honest on a quiet box) and unpaused at the end.
#
#   FdgLab/tools/c4-slice.sh <out-dir> <benchmark|interactive> <games-per-cell> <arm>...
#   arm = hand | net | blend   (net/blend read $NET_WEIGHTS; default the full-v3 serving model)
#
# Example (the screen):  FdgLab/tools/c4-slice.sh FdgLab/reports/c4-slice-2026-09-06 benchmark 48 hand net blend
# Example (the confirm): FdgLab/tools/c4-slice.sh FdgLab/reports/c4-confirm-2026-09-07 interactive 48 hand net
set -u
cd "$(dirname "$0")/../.." || exit 1
OUT=${1:?out dir}; BUDGET=${2:?benchmark|interactive}; GAMES=${3:?games per cell}; shift 3
ARMS=("$@"); [ ${#ARMS[@]} -gt 0 ] || { echo "no arms named"; exit 2; }
BIN=${FDGLAB_BIN:-FdgLab/bin/Release/net8.0/FdgLab}
NET_WEIGHTS=${NET_WEIGHTS:-FdgLab/python/models/serving-full-weights.json}
DOP=${DOP:-6}; SEED_BASE=${SEED_BASE:-1000}
mkdir -p "$OUT"
export DOTNET_GCHeapHardLimit=0x300000000 DOTNET_GCRetainVM=1 DOTNET_GCName=libclrgc.so
A=FdgLab/armies
declare -a P=("Alien Hives 2k - Horde Melee" "Battle Brothers 2k - Elite Shooting" "Dark Elf Raiders 2k - Transport" "Dwarf Guilds 2k - Ambush and Scout-Heavy" "High Elf Fleets 2k  - Caster-Heavy" "Human Defense Force 2k - Tough and Vehicle-Heavy" "Orks 2k - Horde Mixed" "Robot Legions 2k - Mixed")
# The four ring pairs where the hand-leaf Strategist sits lowest on the P4 slice (45.8 / 66.7 / 37.5 / 50.0):
# a leaf swap can only be seen where the control is not already at the ceiling.
PAIRS=(2 3 5 7)
arm_flags() {
  case "$1" in
    hand)  echo "" ;;
    net)   echo "--evaluator $NET_WEIGHTS" ;;
    blend) echo "--evaluator $NET_WEIGHTS --blend 0.5" ;;
    *) echo "unknown arm $1" >&2; exit 2 ;;
  esac
}
touch FdgLab/.pause-selfplay
echo "=== C4 SLICE START budget=$BUDGET games=$GAMES arms=${ARMS[*]} bin=$BIN net=$NET_WEIGHTS === $(date)"
for i in "${PAIRS[@]}"; do
  j=$(( (i+1) % 8 ))
  for arm in "${ARMS[@]}"; do
    label="$arm/pair$i"; mkdir -p "$OUT/$arm"
    echo "=== CELL: $label (${P[$i]} vs ${P[$j]}) === $(date)"
    # shellcheck disable=SC2046
    "$BIN" bench --a "$A/${P[$i]}.fdgarmy" --b "$A/${P[$j]}.fdgarmy" --profile-a strategist --profile-b tactician \
      --dop "$DOP" --search-budget "$BUDGET" --games "$GAMES" --seed-base "$SEED_BASE" --timeout 1800 \
      $(arm_flags "$arm") --out "$OUT/$label" > "$OUT/$label.log" 2>&1
    echo "=== CELL $label exit=$? === $(date)"
    grep " vs " "$OUT/$label/bench.md" | grep "^|" | sed 's/  */ /g'
  done
done
rm -f FdgLab/.pause-selfplay
echo "=== C4 SLICE DONE (self-play unpaused) === $(date)"
python3 FdgLab/tools/c4-summarize.py "$OUT" "${ARMS[@]}" | tee "$OUT/summary.md"
