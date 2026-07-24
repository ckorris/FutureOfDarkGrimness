---
name: build-dist
description: Build self-contained, sendable FdgRaylib executables for Windows (x64), Linux (x64), and macOS (Apple Silicon arm64 + Intel x64) via scripts/build-dist.sh. Use when asked to build the distributable, package a release, make a build to send someone, produce the .exe, or cut Windows/Linux/Mac binaries. Not for ordinary `dotnet build` / test runs.
---

# Build distributable executables

`scripts/build-dist.sh` is the only supported way to produce sendable builds. Do not
hand-roll `dotnet publish` — the script pins the flags that keep the output working
(notably `PublishTrimmed=false`, because `.fdgarmy` loading uses reflection-based
Newtonsoft `TypeNameHandling.Auto` and a trimmer silently breaks it).

## Run it

From the repo root:

```bash
scripts/build-dist.sh          # all targets (default): win-x64, linux-x64, osx-arm64, osx-x64
scripts/build-dist.sh win      # Windows only
scripts/build-dist.sh linux    # Linux only
scripts/build-dist.sh mac      # both macOS arches (Apple Silicon + Intel)
scripts/build-dist.sh mac-arm  # macOS Apple Silicon only
scripts/build-dist.sh mac-x64  # macOS Intel only
```

Every target cross-compiles from either Linux or Windows, so there's no need to switch
machines — the macOS builds too (all of raylib/cimgui/tinyfiledialogs ship the needed
macOS dylibs; cimgui's is a universal x64+arm64 binary). The Mac builds are **unsigned**
(no Apple Developer account), so the recipient clears one Gatekeeper quarantine flag on
first run — the script writes those instructions into the macOS `README.txt`.

**Set a long Bash timeout** — the default now runs FOUR self-contained publishes, which
takes several minutes and will blow past the 120s default. Use `timeout: 600000`.

The script **deletes and recreates `dist/`** on every run. That's intended, but it
means any artifact still sitting there from a previous run is gone. If the user has
something in `dist/` they care about, say so before running.

## Success looks like

Exit 0, and a closing summary listing both archives with their sizes:

```
>> Done. Artifacts in <root>/dist:
   FdgRaylib-linux-x64.tar.gz       ~70M
   FdgRaylib-osx-arm64.tar.gz       ~39M
   FdgRaylib-osx-x64.tar.gz         ~39M
   FdgRaylib-win-x64.zip            ~70M
```

Alongside each archive is the unpacked folder (`dist/FdgRaylib-win-x64/`,
`dist/FdgRaylib-linux-x64/`, `dist/FdgRaylib-osx-arm64/`, `dist/FdgRaylib-osx-x64/`)
with a recipient-facing `README.txt` the script writes.

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

The macOS builds can't be *run* from Linux/Windows, so verify them structurally
instead: `file dist/FdgRaylib-osx-arm64/FdgRaylib` should report a Mach-O arm64
executable, and the game's native libs (`libraylib.dylib`, `libcimgui.dylib`,
`tinyfiledialogs.dylib`) should be present and Mach-O of the matching arch (cimgui is
universal). True end-to-end "it launches" needs a real Mac.

Don't tell the user a build is "ready to send" on the strength of the publish
succeeding. Say what you actually exercised and what you didn't.

## Failure modes

- **`zip` / `tar` not found** — the publish succeeds, then packaging dies. Both must
  be on `PATH`; `zip` in particular is not installed by default on many distros
  (`sudo apt install zip`).
- **Unknown RID / missing SDK target** — needs the .NET 8 SDK (`dotnet --version`).
- **`Unknown target 'foo'`** — exit 2. Accepted args: `win`/`windows`, `linux`,
  `mac`/`macos` (both arches), `mac-arm`/`mac-arm64`, `mac-x64`/`mac-intel`. There is no
  "all" argument — pass nothing to build everything.
