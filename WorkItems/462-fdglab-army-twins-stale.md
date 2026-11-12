# 462 — FdgLab/armies twins are stale copies of the curated 2k lists

**Status**: todo
**Related**: #440 (rebuilt both sets on 2026-10-31), #191 (Tactician lab)

## Goal

`FdgLab/armies/*.fdgarmy` (8 files) duplicates eight of the `armies/GDF/2k - *.fdgarmy` lists. #440 rebuilt both
sets identically; the 2026-11-01 hand-curation of the shipped lists (legal under the 3.6 force-org caps, filled to
bracket, loadouts redesigned) touched only `armies/`, so the lab twins are now the pre-curation 3.6 conversions.
`FdgLab/armies/pool.json` already points at `armies/GDF/...` paths, so decide: delete the twins (and anything in
`FdgLab/Armies.cs` / `GameSpec.cs` that names them), or re-sync them from `armies/GDF` and say in `FdgLab` which
copy is canonical. Recommendation: delete; one source of truth, and the lab reads the shipped folder anyway.
Re-run a short self-play/benchmark pool afterwards so any Tactician baseline that quoted the old lists is re-dated.

## Notes

- 2026-11-01: filed during the curation session. `FdgLab/armies/pool.json` references only `armies/GDF/...`;
  whether `FdgLab/Armies.cs` or any report under `FdgLab/reports/` names the twins directly is unchecked.

## Decisions

- (none yet)

## Outcome

(open)
