# 225 — Audit army lists for wrong base shape/size

**Status**: todo
**Related**: #149 (base shapes), #150 (base-shape geometry everywhere)

## Goal
User has spotted a number of units with default (probably unintended) base shapes/sizes, and some rectangular bases that are wider than they are long when the reverse should be true. Scope: sweep the bundled army/book data (`FdgLab/armies/*.fdgarmy`, `FdgRaylib/Assets/Books/*.fdgbook`) for base entries that are still default-valued or have width > length where that looks backwards, cross-check against real OPR base sizes, and correct them.

## Notes
- 2026-07-15: Filed from user playtest feedback. No specific offending units enumerated yet — first step is to enumerate.

## Decisions

## Outcome
