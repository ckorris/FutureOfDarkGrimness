#!/bin/sh
# Fetch bug reports (#226) from the list-server Worker's drop box.
#
#   ./fetch-reports.sh <base-url> [--delete]
#
# The admin token comes from the ADMIN_TOKEN environment variable (falls back to the
# .dev.vars dev value, which only works against `wrangler dev`):
#
#   ADMIN_TOKEN=<secret> ./fetch-reports.sh https://fdg-list-server.<account>.workers.dev
#
# Each report lands in ./reports/ as <receivedAt>-<id>.json (already decompressed by the
# server). --delete removes each report from the server AFTER its download succeeded, so
# an interrupted run never loses anything.
set -eu

if [ $# -lt 1 ]; then
  echo "usage: $0 <base-url> [--delete]" >&2
  exit 2
fi

BASE="${1%/}"
DELETE=0
[ "${2:-}" = "--delete" ] && DELETE=1
TOKEN="${ADMIN_TOKEN:-dev-admin-token}"
OUT_DIR="reports"

LISTING=$(curl -sf -H "X-Admin-Token: $TOKEN" "$BASE/reports") || {
  echo "Could not list reports (wrong token? server down?)" >&2
  exit 1
}

# One reportId + receivedAt pair per line. The listing is our own server's JSON with known,
# machine-generated field values, so line-oriented extraction is safe here.
IDS=$(printf '%s' "$LISTING" | tr '{' '\n' | sed -n \
  's/.*"reportId":"\([^"]*\)".*"receivedAt":"\([^"]*\)".*/\1 \2/p')

if [ -z "$IDS" ]; then
  echo "No reports on the server."
  exit 0
fi

mkdir -p "$OUT_DIR"
COUNT=0
echo "$IDS" | while read -r ID RECEIVED; do
  # Colons aren't valid in Windows file names; keep the timestamp readable without them.
  STAMP=$(printf '%s' "$RECEIVED" | tr -d ':' | tr 'T' '-' | sed 's/\..*Z$//')
  FILE="$OUT_DIR/$STAMP-$ID.json"
  curl -sf -H "X-Admin-Token: $TOKEN" "$BASE/reports/$ID" -o "$FILE"
  echo "fetched: $FILE"
  if [ "$DELETE" = "1" ]; then
    curl -sf -X DELETE -H "X-Admin-Token: $TOKEN" "$BASE/reports/$ID" > /dev/null
    echo "deleted from server: $ID"
  fi
done

COUNT=$(echo "$IDS" | wc -l | tr -d ' ')
echo "Done: $COUNT report(s) in $OUT_DIR/"
