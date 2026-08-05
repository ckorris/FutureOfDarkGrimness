# FDG list server (#271) + bug-report drop box (#226)

The master-server registry behind the in-game server browser, plus the in-game bug
reporter's drop box on the same free Worker. Two Durable Objects: hosts heartbeat
`POST /servers` every ~30s, browsers read `GET /servers`, entries expire 90s after the
last heartbeat; the game uploads gzipped bug-report bundles to `POST /reports` and the
owner pulls them down with `fetch-reports.sh`. Game traffic never touches this — it only
brokers "who is hosting right now" and stores reports.

Design, API table, and security posture: `WorkItems/271-server-browser.md` and
`WorkItems/226-bug-reporting-system.md`.

## Endpoints

| Verb | Path | Auth | Purpose |
|---|---|---|---|
| `POST` | `/servers` | token after first call | Register (201) or heartbeat/update (200) |
| `GET` | `/servers[?protocolVersion=N]` | none | Live listing (tokens never included) |
| `DELETE` | `/servers/{id}` | `X-Token` header | Polite removal; TTL handles crashes |
| `POST` | `/reports` | none (capped + rate-limited) | Upload one gzipped report bundle (201) |
| `GET` | `/reports` | `X-Admin-Token` header | List report metadata, newest first |
| `GET` | `/reports/{id}` | `X-Admin-Token` header | Download one report, decompressed |
| `DELETE` | `/reports/{id}` | `X-Admin-Token` header | Remove a fetched report |

The advertised address is always the **observed** source IP of the registrant — the
payload has no address field, so listings can't point at third parties. The reachability
probe dials only that observed IP.

Report uploads are unauthenticated by design (any player build can report), so they are
capped (1MB compressed, 16MB decompressed, 200 reports / 50MB total — **rejecting** when
full, never evicting stored reports) and per-IP rate-limited. Reading reports requires
the `ADMIN_TOKEN` secret; if it is not configured the read endpoints answer 503.

## Fetching bug reports

```bash
cd tools/list-server
ADMIN_TOKEN=<secret> ./fetch-reports.sh https://fdg-list-server.<account>.workers.dev            # download to ./reports/
ADMIN_TOKEN=<secret> ./fetch-reports.sh https://fdg-list-server.<account>.workers.dev --delete   # ... and clear the server
```

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
npx wrangler secret put ADMIN_TOKEN   # one-time (#226): the token fetch-reports.sh authenticates with
./smoke.sh https://fdg-list-server.<account>.workers.dev <admin-token>
```

(Local `wrangler dev` reads `ADMIN_TOKEN` from `.dev.vars` instead — a committed,
deliberately public dev-only value that smoke.sh defaults to.)

The printed URL is what the game reads from its list-server config (see
`FdgRaylib/ListServer/ListServerConfig.cs`). Free-tier limits (100k requests/day) exceed
any realistic load for this game by orders of magnitude; there is nothing to maintain
day-to-day. Deleting the Worker (or blanking the app config) degrades the game back to
direct-IP connect.
