# 464 — Main menu war table: units charge each other into melee

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #448 (the holographic war-table menu background this extends)

## Goal
Add a melee pattern to the main menu's war-table battles: two units, one a side, walk into each other and
fight in base contact, with light, very simple flashes at the points of contact. Done when the random
battle generator deals 0-2 melees per battle, the existing #448 placement invariants still hold, and the
owner has seen it in the running menu.

## Notes
- 2026-11-02 (later still): Owner saw "two models that are faded outlines, that move with the unit". Those are
  the #448 mockup's casualties (the far side's eight-model squad has two dead, drawn dashed and faint). The
  random generator kept them whatever role the squad was dealt, so on an advancer, patroller or charger the
  dead bases walked along with it - pre-existing since #448's random placement, not caused by the melee.
  Fix: casualties lie where they fell, so a unit dealt any role but Holds has its `Dead` zeroed before
  placement. Test `OnlyUnitsThatHoldTheirGround_ShowCasualties` (confirmed failing against the unfixed
  generator: "seed 0: BAT-BR 1 (Charges) carries casualties"). App 4205 green, engine 4624 green.
- 2026-11-02 (later): Owner played it: "the graphics are hard to see... a unit of 6 fighting a big unit of 1
  ... one tiny little flash popping over and over. Compare to the crazy lasers in a pair that's shooting each
  other. There should be about the same amount of activity. Lots of flashes, bigger, non-uniform." Cause: a
  big base met only the one or two squad bases within the old 0.6" touch reach, and each point popped on a
  fixed beat. Reworked:
  - `ContactPoints` -> every base within `FightReach` (2") of an enemy base, on either side, fights the
    nearest one; a spark point midway across each such gap, each pair once, at most 8. A squad round a big
    base now gets a point per model in reach.
  - Sparks: each melee has 12 emitters shared over its points (so a 10-model clash is not a white-out and a
    vehicle clash is not a lone flicker). An emitter's time is cut into 0.16s slots; in half of them it pops
    one spark at a random moment, living 0.18-0.45s, of random size (mostly small, now and then big), height
    (0.3-1.5" up), spread (2" along, 0.8" across), 4 or 6 rays, three embers, and a tint (white-hot, or
    pulled 70% toward either army's colour). Impact flare at every point is bigger (8 rays, 2.4x) and lasts
    0.5s. Close-up growth now zoom^0.75, clamped to 2.2x.
  - Tuned by eye with `--menu-preview` frames 0.1s apart (seed 2: a 5-v-10 squad clash and a vehicle-v-5).
    Bench at 1920x1080: Animated 1.42 ms/frame, Still 0.07. App 4204 green, engine 4624 green.
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
- Spark density is budgeted per melee, not per point: per-point emitters made the many-point squad clash a
  white blob in the overview while the few-point vehicle clash stayed sparse in the close-up.
- `MeleeGroup` is an int, not a reference to the foe: `HoloUnit` is a record, and a reference cycle would
  send value equality (used by `Distinct` on fights) into infinite recursion.

## Outcome
