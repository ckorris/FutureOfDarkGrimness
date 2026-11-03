# 464 — Main menu war table: units charge each other into melee

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #448 (the holographic war-table menu background this extends)

## Goal
Add a melee pattern to the main menu's war-table battles: two units, one a side, walk into each other and
fight in base contact, with light, very simple flashes at the points of contact. Done when the random
battle generator deals 0-2 melees per battle, the existing #448 placement invariants still hold, and the
owner has seen it in the running menu.

## Notes
- 2026-11-02: Owner asked how hard this would be; assessment given (placement is the work, sparks are
  cheap), then four forks put and answered: (1) "two units designated to move into each other - but do
  whichever is simpler" -> both units move (same cost as a lone charger once the pair is placed together,
  and it keeps each side in its own half); (2) 0-2 melees in every battle; (3) held clash, no casualties;
  (4) a white spark that pops and fades. Implemented on branch `464-menu-melee-clash`:
  - `EHoloRole.Charges` + `HoloUnit.MeleeGroup`. A charger's progress is ease-in only (x^2), so it hits at
    full speed; both units of a melee share one `ArriveAt`, so they meet as they arrive.
  - `HoloSkirmish.Generate`: melee count `rng.Next(3)`; each melee takes each side's 4th dealt role, a
    second melee takes its patroller. Pairs are placed first (they need a clear stretch of the centre
    line): formation centred on a random x, slid forward until the foremost base edge is half of
    `ContactGap` (0.15") short of the centre line, far unit slid sideways so the near flag stands 1.5-2.5"
    right of the far one (tags lean apart), both walking in 6-11" from behind. The pair is checked against
    everything already placed with the usual gaps, but not against each other. Shared fit/commit checks
    refactored into local functions; non-melee placement unchanged.
  - `HoloEngagement`: `HoloMelee` (A near, B far, contact points), `ContactPoints` (bases within 0.6" edge
    to edge, midpoint of the gap, at most 6; each base of the unit with more bases used once, so a
    vehicle's big base can meet a whole front rank). Chargers neither shoot, are shot, nor are measured.
    Melees head `Clashes`, so the camera's close-up comes round to them.
  - Renderer: per contact point a white spark (dot + four rays flying out as it fades) on its own beat
    (~0.85s cycle, visible 30% of it), jittered and turned per pop; every point flares bigger for 0.45s at
    the moment of contact; sparks grow by up to 2x (sqrt of zoom) in the close-up, since line widths do not.
  - Tests: `EverySide_HoldsAdvancesAndPatrols` became `..._AndPatrolsUnlessTwoOfItsUnitsAreInMelee`
    (owner-approved rule change); new: 0/1/2 melees each turn up, every charger is in a melee front rank
    to front rank on the centre line with flags the right way round, melee units never shoot/are
    shot/measured but are in the close-ups, contact-point geometry, a two-charger plan, and the charge's
    acceleration. All existing #448 invariants (bases apart, own halves, paths clear, fallback <= 1%) pass
    unchanged. App 4203 green, engine 4624 green. Checked visually with `--menu-preview` (seed 2 has two
    melees: a squad clash mid-table and a vehicle against a squad, the close-up target).

## Decisions
- Both units move rather than one charging a holder: same placement cost once the pair is placed as one
  item, and contact on the centre line lets each unit keep to its own half (only the 2" centre-line
  clearance is waived for them).
- Bases in melee keep a 0.15" gap rather than touching exactly, so the "bases never overlap" invariant
  needed no exception.
- `MeleeGroup` is an int, not a reference to the foe: `HoloUnit` is a record, and a reference cycle would
  send value equality (used by `Distinct` on fights) into infinite recursion.

## Outcome
