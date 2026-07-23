#!/bin/sh
# Smoke test for the FDG list server (#264). Run against `npx wrangler dev` (default) or a
# deployed URL:  ./smoke.sh [base-url]
# Exercises: health, register, list, heartbeat, token auth, delete, validation rejects.
set -eu

BASE="${1:-http://localhost:8787}"
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

echo ""
if [ "$FAILED" = "0" ]; then echo "SMOKE PASSED"; else echo "SMOKE FAILED"; exit 1; fi
