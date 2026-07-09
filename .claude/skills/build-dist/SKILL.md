---
name: build-dist
description: Build self-contained, sendable FdgRaylib executables for Windows and Linux (both x64) via scripts/build-dist.sh. Use when asked to build the distributable, package a release, make a build to send someone, produce the .exe, or cut Windows/Linux binaries. Not for ordinary `dotnet build` / test runs.
---

# Build distributable executables

`scripts/build-dist.sh` is the only supported way to produce sendable builds. Do not
hand-roll `dotnet publish` — the script pins the flags that keep the output working
(notably `PublishTrimmed=false`, because `.fdgarmy` loading uses reflection-based
Newtonsoft `TypeNameHandling.Auto` and a trimmer silently breaks it).

## Run it

From the repo root:

```bash
scripts/build-dist.sh          # both targets (default)
scripts/build-dist.sh win      # Windows only
scripts/build-dist.sh linux    # Linux only
```

Both targets cross-compile from either OS, so there's no need to switch machines.

**Set a long Bash timeout** — two self-contained publishes take multiple minutes and
will blow past the 120s default. Use `timeout: 600000`.

The script **deletes and recreates `dist/`** on every run. That's intended, but it
means any artifact still sitting there from a previous run is gone. If the user has
something in `dist/` they care about, say so before running.

## Success looks like

Exit 0, and a closing summary listing both archives with their sizes:

```
>> Done. Artifacts in <root>/dist:
   FdgRaylib-linux-x64.tar.gz       ~70M
   FdgRaylib-win-x64.zip            ~70M
```

Alongside each archive is the unpacked folder (`dist/FdgRaylib-win-x64/`,
`dist/FdgRaylib-linux-x64/`) with a recipient-facing `README.txt` the script writes.

Report the artifact paths and sizes back to the user. `dist/` is gitignored, so
nothing here should ever be committed.

## Before sending a build to anyone

A green `dotnet publish` does **not** mean the binary runs. Raylib ships native
libraries per-RID, and a cross-compiled target can package cleanly yet fail on first
launch. Confirm the artifact actually starts on its target platform before it goes
out — at minimum the Linux one locally:

```bash
dist/FdgRaylib-linux-x64/FdgRaylib --headless
```

Don't tell the user a build is "ready to send" on the strength of the publish
succeeding. Say what you actually exercised and what you didn't.

## Failure modes

- **`zip` / `tar` not found** — the publish succeeds, then packaging dies. Both must
  be on `PATH`; `zip` in particular is not installed by default on many distros
  (`sudo apt install zip`).
- **Unknown RID / missing SDK target** — needs the .NET 8 SDK (`dotnet --version`).
- **`Unknown target 'foo'`** — exit 2. Only `win` / `windows` / `linux` are accepted;
  there is no "both" argument, you get both by passing nothing.
