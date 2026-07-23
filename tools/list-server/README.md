# FDG list server (#264)

The master-server registry behind the in-game server browser. A tiny Cloudflare Worker +
one Durable Object: hosts heartbeat `POST /servers` every ~30s, browsers read
`GET /servers`, entries expire 90s after the last heartbeat. Game traffic never touches
this — it only brokers "who is hosting right now".

Design, API table, and security posture: `WorkItems/264-server-browser.md`.

## Endpoints

| Verb | Path | Auth | Purpose |
|---|---|---|---|
| `POST` | `/servers` | token after first call | Register (201) or heartbeat/update (200) |
| `GET` | `/servers[?protocolVersion=N]` | none | Live listing (tokens never included) |
| `DELETE` | `/servers/{id}` | `X-Token` header | Polite removal; TTL handles crashes |

The advertised address is always the **observed** source IP of the registrant — the
payload has no address field, so listings can't point at third parties. The reachability
probe dials only that observed IP.

## Local dev

```bash
cd tools/list-server
npm install
npx wrangler dev          # serves http://localhost:8787
./smoke.sh                # in another terminal; asserts the whole API surface
```

Under `wrangler dev` the observed IP is loopback, so the reachability probe reports
`null` ("unknown") by design — private/loopback ranges are never dialed.

## Deploy (one-time setup, then one command)

```bash
cd tools/list-server
npm install
npx wrangler login        # opens a browser; free Cloudflare account is sufficient
npx wrangler deploy       # prints the https://fdg-list-server.<account>.workers.dev URL
./smoke.sh https://fdg-list-server.<account>.workers.dev
```

The printed URL is what the game reads from its list-server config (see
`FdgRaylib/ListServer/ListServerConfig.cs`). Free-tier limits (100k requests/day) exceed
any realistic load for this game by orders of magnitude; there is nothing to maintain
day-to-day. Deleting the Worker (or blanking the app config) degrades the game back to
direct-IP connect.
