#!/bin/sh
# Read anonymous usage analytics (#412) from the list-server Worker.
#
#   ./fetch-stats.sh <base-url> [--days N] [--export FILE.csv]
#
# Prints the JSON summary for the last N days (default 30). --export also pages through every
# stored event and writes them to FILE.csv (props as a JSON column) for a spreadsheet or
# `sqlite3 :memory: '.import --csv FILE.csv events'`.
#
# The admin token comes from the ADMIN_TOKEN environment variable (falls back to the
# .dev.vars dev value, which only works against `wrangler dev`):
#
#   ADMIN_TOKEN=<secret> ./fetch-stats.sh https://fdg-list-server.<account>.workers.dev
set -eu

if [ $# -lt 1 ]; then
  echo "usage: $0 <base-url> [--days N] [--export FILE.csv]" >&2
  exit 2
fi

BASE="${1%/}"; shift
DAYS=30
EXPORT=""
while [ $# -gt 0 ]; do
  case "$1" in
    --days) DAYS="$2"; shift 2 ;;
    --export) EXPORT="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
TOKEN="${ADMIN_TOKEN:-dev-admin-token}"

SUMMARY=$(curl -sf -H "X-Admin-Token: $TOKEN" "$BASE/stats?days=$DAYS") || {
  echo "Could not read stats (wrong token? server down?)" >&2
  exit 1
}
if command -v jq > /dev/null 2>&1; then printf '%s\n' "$SUMMARY" | jq .; else printf '%s\n' "$SUMMARY"; fi

if [ -n "$EXPORT" ]; then
  AFTER=0
  : > "$EXPORT"
  HEADER_WRITTEN=0
  while :; do
    PAGE=$(curl -sf -H "X-Admin-Token: $TOKEN" "$BASE/stats/export?format=csv&limit=5000&after=$AFTER") || {
      echo "Export failed after id $AFTER" >&2
      exit 1
    }
    ROWS=$(printf '%s\n' "$PAGE" | sed '1d' | sed '/^$/d')
    [ -z "$ROWS" ] && break
    if [ "$HEADER_WRITTEN" = "0" ]; then printf '%s\n' "$PAGE" | sed -n '1p' >> "$EXPORT"; HEADER_WRITTEN=1; fi
    printf '%s\n' "$ROWS" >> "$EXPORT"
    # The id is the first column and never quoted, so the last line's first field is the cursor.
    AFTER=$(printf '%s\n' "$ROWS" | tail -n 1 | cut -d, -f1)
  done
  echo "Exported $(($(wc -l < "$EXPORT") - HEADER_WRITTEN)) event(s) to $EXPORT" >&2
fi
