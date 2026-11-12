# 305 — CLI army-file prompt loops forever on EOF

**Status**: done (2026-07-31)
**Related**: found during #197 (recorded in its "Tooling / hygiene found here, not fixed"), `docs/ResolverGuide.md` (EOF-safe defaults)

## Goal

`ArmyLoader.LoadFromFile` terminates at EOF instead of spinning. Piping a bad or missing army path into
`--headless` should fall back the way every other CLI resolver does, not loop until the harness kills it.
Done = a piped run that fails to load an army exits (or falls back) promptly, with a test or probe showing
a bounded run rather than a timeout.

## The bug

`FdgRaylib/Cli/ArmyLoader.cs`, `LoadFromFile()`:

```csharp
string? path = Console.ReadLine()?.Trim().Trim('"');

if (string.IsNullOrEmpty(path))
{
    Console.WriteLine("No path entered.");
    continue;                     // <-- at EOF, ReadLine() is null forever
}
```

`Console.ReadLine()` returns `null` at end of stream, `IsNullOrEmpty` conflates that with "user pressed
enter on an empty line", and the loop retries immediately. With stdin closed this prints
`No path entered.` as fast as the process can write. **Observed during a #197 probe: a stale probe army
produced a 5.8 GB log before the timeout killed it.**

The same file already gets this right one method up — `PromptForArmy` checks `input == null` separately
and falls back to the built-in test army with `(EOF - using built-in test army)`. Only `LoadFromFile`
missed it.

Note the load-failure path (`catch` at the bottom) is not itself the bug: it is the `continue` into a
`ReadLine` that can no longer block that turns one failure into an unbounded loop.

## Suggested shape

Split the null (EOF) case from the empty-string (user typed nothing) case, and treat EOF as terminal —
mirroring `PromptForArmy`'s existing fallback:

```csharp
if (path == null)
{
    Console.WriteLine("(EOF - using built-in test army)");
    return MakeTestArmy(playerLabel);   // needs playerLabel threading, or return null and let the caller decide
}
if (path.Length == 0) { Console.WriteLine("No path entered."); continue; }
```

`LoadFromFile` currently takes no `playerLabel`, so either thread it through or have the method return
`ArmyListFile?` and let `PromptForArmy` supply the fallback. The second reads better — it keeps the
"what do we do when there's no army" decision in one place.

Worth checking while in here: whether any *other* CLI prompt loop has the same `IsNullOrEmpty`-on-EOF
shape. This one was found by accident, not by audit.

## Notes

- 2026-07-31: Fixed and closed. Took the second suggested shape (`LoadFromFile` returns `ArmyListFile?`,
  `PromptForArmy` owns the fallback). Audited every other CLI prompt loop while in here — clean.
- 2026-07-31: Filed out of #197 on its close. The finding itself dates from the #197 probe work; it was
  recorded in that item's hygiene section but is not #197 work.

## Decisions

- **`LoadFromFile` returns `ArmyListFile?`; null means EOF.** The suggested alternative (thread
  `playerLabel` down and build the test army in place) would have put the "what do we do when there's no
  army" decision in two places. `PromptForArmy` already owned one EOF fallback; now it owns both, and
  `LoadFromFile` only answers "did the user give me a loadable file".
- **An empty line is still a retry.** The fix splits `null` out of `IsNullOrEmpty`; it does not make
  pressing Enter terminal. Pinned by a test.

## Outcome

`ArmyLoader.LoadFromFile` treats EOF as terminal instead of as an empty line. `Console.ReadLine()`
returns `null` forever once stdin closes, and the old `IsNullOrEmpty` + `continue` turned any load
failure under piped input into an unbounded print loop.

**Audit (the item asked for it): `ArmyLoader.LoadFromFile` was the only offender.** All 14 CLI resolvers
with a prompt loop already branch on `input == null` explicitly with a documented EOF default, and
`Program.cs`'s only other `ReadLine` is a non-looping "press Enter to exit" on the crash path.

Verification:

- `FdgRaylib.Tests/ArmyLoaderEofTests.cs` — 3 pins: EOF straight at the path prompt, a failed load
  *then* EOF (the bug's actual trigger), and blank-line-then-real-path (proving the retry survives).
  The fixture redirects `Console.Out` to a writer that throws past 500 writes, so a regression fails in
  milliseconds instead of hanging or OOM-ing the suite.
- Suites green: engine 2520/2520, app 854/854, full `dotnet build` clean.
- Headless probe, the item's Done criterion —
  `printf "1\n" | dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless`: **exit 0, 43 KB of
  log, zero `No path entered.` lines**, `(EOF - using built-in test army)` on both players, game played
  to a result. The same shape produced 5.8 GB before the timeout killed it during #197.
