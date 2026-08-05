#!/bin/sh
# Smoke test for the FDG list server (#271) + bug-report drop box (#226). Run against
# `npx wrangler dev` (default) or a deployed URL:  ./smoke.sh [base-url] [admin-token]
# The admin token defaults to the .dev.vars dev value; pass the real secret when smoking
# a deployed Worker.
# Exercises: health, register, list, heartbeat, token auth, delete, validation rejects,
# report upload/list/fetch/delete, report auth + rate limit + bad-body rejects.
set -eu

BASE="${1:-http://localhost:8787}"
ADMIN_TOKEN="${2:-dev-admin-token}"
FAILED=0

check() {
  desc="$1"; expected="$2"; actual="$3"
  if [ "$expected" = "$actual" ]; then
    echo "  ok: $desc"
  else
    echo "  FAIL: $desc (expected $expected, got $actual)"
    FAILED=1
  fi
}

body() { sed '$d' < "$1"; }        # response body (all but last line)
code() { tail -n 1 < "$1"; }       # http code (last line)

req() { # method path [json-body] [extra-header]
  method="$1"; path="$2"; data="${3:-}"; header="${4:-}"
  if [ -n "$data" ]; then
    curl -s -o /tmp/fdg_smoke_body -w '%{http_code}' -X "$method" \
      -H 'Content-Type: application/json' ${header:+-H "$header"} \
      -d "$data" "$BASE$path" > /tmp/fdg_smoke_code
  else
    curl -s -o /tmp/fdg_smoke_body -w '%{http_code}' -X "$method" \
      ${header:+-H "$header"} "$BASE$path" > /tmp/fdg_smoke_code
  fi
  cat /tmp/fdg_smoke_code
}

echo "1. health"
check "GET / is 200" 200 "$(req GET /)"

echo "2. register"
REG='{"name":"Smoke Test Table","port":6389,"protocolVersion":3,"typeMapHash":"ABC123","hasPassword":false,"playerCount":1,"maxPlayers":4,"state":"lobby"}'
check "POST /servers is 201" 201 "$(req POST /servers "$REG")"
SERVER_ID=$(sed -n 's/.*"serverId":"\([^"]*\)".*/\1/p' /tmp/fdg_smoke_body)
TOKEN=$(sed -n 's/.*"token":"\([^"]*\)".*/\1/p' /tmp/fdg_smoke_body)
[ -n "$SERVER_ID" ] && echo "  ok: got serverId" || { echo "  FAIL: no serverId"; FAILED=1; }
[ -n "$TOKEN" ] && echo "  ok: got token" || { echo "  FAIL: no token"; FAILED=1; }

echo "3. list contains it (and no token leaks)"
check "GET /servers is 200" 200 "$(req GET /servers)"
grep -q "$SERVER_ID" /tmp/fdg_smoke_body && echo "  ok: listed" || { echo "  FAIL: not listed"; FAILED=1; }
grep -q '"token"' /tmp/fdg_smoke_body && { echo "  FAIL: token leaked in listing"; FAILED=1; } || echo "  ok: no token in listing"

echo "4. rate limit blocks an immediate second post"
check "immediate re-POST is 429" 429 "$(req POST /servers "$REG")"

echo "5. heartbeat with the token (after rate-limit window)"
sleep 4
HB=$(echo "$REG" | sed "s/\"state\":\"lobby\"/\"state\":\"in-game\",\"serverId\":\"$SERVER_ID\",\"token\":\"$TOKEN\"/")
check "heartbeat is 200" 200 "$(req POST /servers "$HB")"
req GET /servers > /dev/null
grep -q '"state":"in-game"' /tmp/fdg_smoke_body && echo "  ok: state updated" || { echo "  FAIL: state not updated"; FAILED=1; }

echo "6. wrong token is rejected"
sleep 4
BAD=$(echo "$REG" | sed "s/\"state\":\"lobby\"/\"state\":\"lobby\",\"serverId\":\"$SERVER_ID\",\"token\":\"deadbeef\"/")
check "bad-token heartbeat is 403" 403 "$(req POST /servers "$BAD")"
check "bad-token delete is 403" 403 "$(req DELETE "/servers/$SERVER_ID" "" "X-Token: nope")"

echo "7. validation rejects"
sleep 4
check "empty name is 400" 400 "$(req POST /servers '{"name":"","port":6389,"protocolVersion":1,"typeMapHash":"","hasPassword":false,"playerCount":0,"maxPlayers":4,"state":"lobby"}')"
sleep 4
check "bad port is 400" 400 "$(req POST /servers '{"name":"x","port":80,"protocolVersion":1,"typeMapHash":"","hasPassword":false,"playerCount":0,"maxPlayers":4,"state":"lobby"}')"
sleep 4
check "bad state is 400" 400 "$(req POST /servers '{"name":"x","port":6389,"protocolVersion":1,"typeMapHash":"","hasPassword":false,"playerCount":0,"maxPlayers":4,"state":"nope"}')"

echo "8. delete with the real token"
check "delete is 200" 200 "$(req DELETE "/servers/$SERVER_ID" "" "X-Token: $TOKEN")"
req GET /servers > /dev/null
grep -q "$SERVER_ID" /tmp/fdg_smoke_body && { echo "  FAIL: still listed after delete"; FAILED=1; } || echo "  ok: gone from listing"

req_gz() { # POST a gzipped file to a path
  curl -s -o /tmp/fdg_smoke_body -w '%{http_code}' -X POST \
    -H 'Content-Type: application/octet-stream' \
    --data-binary "@$1" "$BASE$2"
}

echo "9. report upload rejects a non-gzip body"
printf '{"description":"not gzipped"}' > /tmp/fdg_smoke_raw
check "plain-JSON POST /reports is 400" 400 "$(req_gz /tmp/fdg_smoke_raw /reports)"

echo "10. report upload"
printf '{"description":"smoke test report SMOKEMARKER","appVersion":"smoke-1","protocolVersion":3,"isHost":true,"save":"{}","log":["line one","line two"]}' \
  | gzip -c > /tmp/fdg_smoke_gz
check "gzip POST /reports is 201" 201 "$(req_gz /tmp/fdg_smoke_gz /reports)"
REPORT_ID=$(sed -n 's/.*"reportId":"\([^"]*\)".*/\1/p' /tmp/fdg_smoke_body)
[ -n "$REPORT_ID" ] && echo "  ok: got reportId" || { echo "  FAIL: no reportId"; FAILED=1; }

echo "11. report rate limit blocks an immediate second upload"
check "immediate re-POST /reports is 429" 429 "$(req_gz /tmp/fdg_smoke_gz /reports)"

echo "12. report reads are admin-only"
check "GET /reports without token is 403" 403 "$(req GET /reports)"
check "GET /reports with a wrong token is 403" 403 "$(req GET /reports "" "X-Admin-Token: nope")"

echo "13. report listing and fetch"
check "GET /reports is 200" 200 "$(req GET /reports "" "X-Admin-Token: $ADMIN_TOKEN")"
grep -q "$REPORT_ID" /tmp/fdg_smoke_body && echo "  ok: listed" || { echo "  FAIL: not listed"; FAILED=1; }
grep -q '"hasSave":true' /tmp/fdg_smoke_body && echo "  ok: hasSave extracted" || { echo "  FAIL: hasSave missing"; FAILED=1; }
check "GET /reports/id is 200" 200 "$(req GET "/reports/$REPORT_ID" "" "X-Admin-Token: $ADMIN_TOKEN")"
grep -q 'SMOKEMARKER' /tmp/fdg_smoke_body && echo "  ok: body round-tripped" || { echo "  FAIL: body did not round-trip"; FAILED=1; }

echo "14. report delete"
check "DELETE /reports/id without token is 403" 403 "$(req DELETE "/reports/$REPORT_ID")"
check "DELETE /reports/id is 200" 200 "$(req DELETE "/reports/$REPORT_ID" "" "X-Admin-Token: $ADMIN_TOKEN")"
req GET /reports "" "X-Admin-Token: $ADMIN_TOKEN" > /dev/null
grep -q "$REPORT_ID" /tmp/fdg_smoke_body && { echo "  FAIL: still listed after delete"; FAILED=1; } || echo "  ok: gone from listing"

echo ""
if [ "$FAILED" = "0" ]; then echo "SMOKE PASSED"; else echo "SMOKE FAILED"; exit 1; fi
