# 219 — Audit Army Forge upgrades for missing point costs

**Status**: todo
**Related**: #156 (Army Forge builder), #218 (adjacent Replace-All cost bug), `OprBookImporter.cs`, `FdgRaylib/Assets/Books/*.fdgbook`

## Goal
User has spotted multiple upgrade options in-app that should cost points but show/charge 0. Scope: audit the bundled `.fdgbook` catalog (and the `OprBookImporter` mapping that produced it) for options with a missing or zero `Cost` where the source OPR data has a nonzero price, and fix the importer/data. Done = a sweep across all bundled books turns up the offending options, root cause identified (importer mapping gap vs. source data vs. compiler), and costs corrected.

## Notes
- 2026-07-15: Filed from user playtest feedback. No specific offending upgrades listed yet — first step is to reproduce/enumerate.

## Decisions

## Outcome
