# Testing checklist — 2026-07-23 security + server-browser work

Covers #271 (server browser), #189 (broadcast gating + configurable port), #272/#186/#273
(deserialization + connection hardening). Everything engine-level is already tested (2015/2015);
this is the by-hand / setup work that needs a display and, for the browser, a running list server.

Delete this file when done.

---

## Prerequisites

- [ ] You're at your machine with a display (the GUI needs X).
- [ ] **Node 18+ installed** — required ONLY for the list server (Section 3+). The session-local
      copy I used is gone. Install: `sudo apt install -y nodejs npm` (Ubuntu 24.04 gives Node 18),
      or nvm. Skip if you're only doing Sections 1-2.

---

## Section 1 — Sanity (5 min, no setup)

- [ ] `dotnet build` — clean.
- [ ] `printf "2\n2\n" | dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless` — exits 0
      with a "wins!/tie" line. (Confirms #272 didn't break the store/save path.)
- [ ] Existing content still loads (this is the only user-visible check for the #272/#186 security
      work): launch the GUI, open **Army Builder / Army Forge**, load one of `FdgLab/armies/*.fdgarmy`
      -> loads fine. Optionally **Load Game** on one of the root `*.fdgsave` files -> resumes.
      (I already confirmed all 35 armies + 3 saves load programmatically; this is just eyeballing it.)

---

## Section 2 — #189 configurable port + direct connect (no list server, ~15 min)

Two app instances on this one machine, over loopback.

- [ ] Terminal A: `dotnet run --project FdgRaylib/FdgRaylib.csproj` -> **Host**.
      - [ ] The HOST SERVER dialog now has a **Port** field, default `6389`.
      - [ ] Change it to `7000`. Fill name/army, CREATE -> reaches the lobby.
      - [ ] Try CREATE with Port = `80` or `abc` -> blocked with "Port must be a number from
            1024 to 65535." (then set it back to 7000).
- [ ] Terminal B: `dotnet run --project FdgRaylib/FdgRaylib.csproj` -> **Join** (or "Connect").
      - [ ] The connect form has a **Port** field, default `6389`.
      - [ ] Host Address `127.0.0.1`, Port `7000` -> CONNECT -> lands in the host's lobby.
      - [ ] (Negative) With Port `6389` (wrong) -> connect fails/times out, doesn't join. 
- [ ] Host clicks LAUNCH -> both reach the game. (Confirms the port plumbing carries through.)

---

## Section 3 — #271 server browser, local (needs Node, ~20 min)

Run the registry locally; no Cloudflare account needed yet.

- [ ] Terminal C: `cd tools/list-server && npm install` (first time only).
- [ ] Same terminal: `npx wrangler dev` -> serves `http://localhost:8787` (leave running).
- [ ] Terminal D: `cd tools/list-server && ./smoke.sh` -> ends with **SMOKE PASSED**.

Now point the game at it (env var = no rebuild needed). Set it in **each** terminal that launches
the app:

- [ ] Terminal A: `export FDG_LIST_SERVER_URL=http://localhost:8787` then run the GUI -> **Host**.
      - [ ] The host dialog now shows a **"List publicly ..."** checkbox (it's hidden without the
            env var — that's the intended fallback). Tick it, CREATE.
      - [ ] Confirm it registered: `curl -s localhost:8787/servers` shows your server (name, port,
            `"state":"lobby"`, `"host":"127.0.0.1"`).
- [ ] Terminal B: `export FDG_LIST_SERVER_URL=http://localhost:8787` then run the GUI -> **Join**.
      - [ ] The join screen opens on a **SERVER BROWSER** tab by default, with **DIRECT CONNECT**
            as a second tab.
      - [ ] Your host appears in the list (Players, Access, Build=OK, Port). Reachability shows
            **"?"** — expected locally (the probe skips loopback), not a bug.
      - [ ] Click **JOIN** -> lands in the lobby. (If you set a host password, a password popup
            appears first.)
- [ ] Host LAUNCHes -> the listing flips to **in-game** (browser greys/labels it; `curl` shows
      `"state":"in-game"`).
- [ ] Teardown: quit the host to the main menu (or quit the app) -> the entry disappears from
      `curl -s localhost:8787/servers` within a few seconds (polite delete; otherwise ~90s TTL).

---

## Section 4 — OPTIONAL: real deploy + two-machine internet test (~30+ min)

Only if you want the browser live for real, or to test NAT/reachability.

- [ ] `cd tools/list-server && npx wrangler login` (opens a browser; free Cloudflare account is fine).
- [ ] `npx wrangler deploy` -> prints `https://fdg-list-server.<account>.workers.dev`.
- [ ] `./smoke.sh https://fdg-list-server.<account>.workers.dev` -> SMOKE PASSED.
- [ ] Make it the built-in default (so no env var needed): put that URL in
      `DefaultBaseUrl` in `FdgRaylib/ListServer/ListServerConfig.cs`, then `dotnet build`.
      (Or ask me to do this once you have the URL.)
- [ ] Two different machines/networks: host on one (tick List publicly), browse+join from the other.
      - [ ] Reachability now shows **Open** or **Blocked?** for real. "Blocked?" = your router isn't
            forwarding the port; either forward it, use a non-default port + forward, or connect over
            Tailscale/ZeroTier (type the Tailscale hostname in DIRECT CONNECT).
      - [ ] **UPnP auto-forward (new):** with UPnP ENABLED on your home router, host a game WITHOUT
            manually forwarding the port. The mapper tries automatically on host-create. Success looks
            like: reachability flips to **Open** on its own, and a "Future of Dark Grimness" TCP
            mapping for the game port appears in your router's port-forward / UPnP table. Close the
            lobby/game and confirm that mapping DISAPPEARS (we remove it on teardown). If your router
            has UPnP off (common) it simply stays "Blocked?" - that's expected, not a bug. This path
            is not auto-tested (it needs a real router), so it only gets exercised here.

---

## Section 5 — When you're satisfied: push

Nothing is pushed yet. **Engine submodule FIRST, then the superproject** (or the bumped pointers
reference commits GitHub doesn't have):

- [ ] `git -C FutureOfDarkGrimness push origin HEAD:master` - engine tip is now the merge commit
      `67e3bbe` on branch `merge-264-security` (my 5 security commits 842c43b/2ecf201/e292e18/f432c03/46f387d
      merged with origin/master `b8cf870`). `b8cf870` is an ancestor, so this fast-forwards cleanly.
- [ ] Confirm it succeeded, THEN: `git push` (superproject: server browser + UPnP + reconciliation 22
      + the merge of origin/master). Nothing pushes until you run these.
- [ ] Sanity-check `git status` / remote state first (branch is `264-server-browser`, 0 behind / 16 ahead).

---

## Already verified — you do NOT need to test these by hand

- Wire + file `$type` hardening (#186/#272), pre-auth connection caps + frame cap (#273), broadcast
  gating (#189) — all covered by the engine suite (2015/2015), incl. real-TCP tests and hostile-payload
  tests. There's no meaningful by-hand check for these beyond "existing content still loads" (Section 1).
- List-server API (register/heartbeat/list/delete, token auth, rate limits, validation) — `smoke.sh`.
