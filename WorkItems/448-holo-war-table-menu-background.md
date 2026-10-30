# 448 — Holographic war-table main menu background

**Status**: in-progress (slices 1-3 done 2026-10-30; awaiting GUI hand-verify)
**Related**: `docs/menu-background/` (mockup, army colour sheet, generators, pairing check)

## Goal
The main menu gets an animated background: a holographic 6'x4' wargaming table, wireframe-on-black with
bloom (DEFCON / hologram look, bright lines, almost no fill or texture), seen at an angle, with wireframe
terrain pieces and two random armies' units skirmishing on it. The button column moves into a left
"command panel". Done = every slice below has shipped, the menu stays responsive, and an Options toggle
can turn the animation down or off.

Reference image: `docs/menu-background/mockup-terrain.jpg` (rendered by `mockup_generator.py terrain`;
it holds the camera maths, terrain shapes, formation and line-of-fire checks the real version follows).

## Design (signed off by the owner 2026-10-30)

**Table.** 72"x48" slab, bright top edge, visible rim thickness, corner brackets, faint 6" grid (1"
sub-grid fainter), inch ticks every 6" with labels every 12" on the near long side AND one short side.
Camera tilted ~47 deg, slight yaw, table framed in the area right of the panel. No topography (option 1,
contour lines, was rejected in favour of terrain pieces).

**Terrain pieces** (cyan wireframe, faint face fill): ruined walls with broken tops + window holes,
containers, an octagonal bunker with firing slits, forests (cone trees inside a dashed boundary),
barricades, a crater (area terrain, does not block). The pulse ring brightens pieces as it crosses them.

**Units.** Groups of base circles sharing ONE flag (pole on the model nearest the centroid), loose
formation. Infantry squads of 3-10 small bases; vehicles as a single large base + dashed inner ring +
facing tick. Label `<ABBR> <n> x<alive>` (space before the number, e.g. `BAT-BR 2 x10`). Casualties are
dashed, faded bases. Dotted movement trails.

**Activity.** Tracers (white-hot, so gold/amber armies stay readable) from the shooters' nearest models
to the target's nearest models; simple flickering fire + embers on casualties; an expanding radar pulse;
a base-to-base measuring line with the true inch distance. **No shot or measure may pass through
terrain** and no base may sit in terrain.

**Motion.** Camera glides/drifts, periodically framing a new skirmish (camera director); units advance
along trails; tracers travel; fire flickers; pulses repeat; measuring line draws out then fades.

**Menu layout (A + C).** Title top-left, both lines the same size ("FUTURE of" / "DARK GRIMNESS").
Left translucent command panel with corner brackets. Tiers: HOST/SOLO + JOIN (large, primary; hovered =
filled), Army Forge / Combat Calculator / Load Game (medium, solid), Options + Quit (side-by-side ghost
buttons: outline only, dimmer, regular weight). Map darkened behind the panel only.

**Privacy popup (#412) restyled to match:** same translucent dark panel, thin cyan border + corner
brackets, and the same primary/ghost button tiers, so it reads as part of the command-panel UI rather
than a stock ImGui modal. The main-menu Options modal gets the same treatment.

**Rejected:** sea level, objective markers, fog of war, mouse reticle, slow terrain morphing, cloud
shadows, palette from `TableBackgrounds`, topographic contours.

### Armies (main armies only; Prime/Brothers variants, Disciples, Titan Lords-Disciples excluded)

| Abbr | Army | Colour | | Abbr | Army | Colour |
|---|---|---|---|---|---|---|
| ALIEN | Alien Hives | `#9C6BFF` | | H-INQ | Human Inquisition | `#E6EAF2` |
| BAT-BR | Battle Brothers | `#4F6BFF` | | INFECT | Infected Colonies | `#D4E157` |
| B-SIST | Blessed Sisters | `#FF4D6D` | | JAK | Jackals | `#D8A878` |
| C-BR | Custodian Brothers | `#F2E4BC` | | M-CULT | Machine Cults | `#E0612A` |
| DAO | DAO Union | `#FF9F40` | | ORK | Orc Marauders | `#3DDC5A` |
| D-ELF | Dark Elf Raiders | `#E04DFF` | | RAT | Ratmen Clans | `#FFB000` |
| D-GUILD | Dwarf Guilds | `#D09050` | | REB-G | Rebel Guerrillas | `#B5A66A` |
| ELF-J | Elven Jesters | `#FF85C8` | | ROBOT | Robot Legions | `#8C4DFF` |
| E-DYN | Eternal Dynasty | `#FFEB3B` | | S-STAR | Saurian Starhost | `#2EE6A6` |
| GOB | Goblin Reclaimers | `#A6F58C` | | S-CULT | Soul-Snatcher Cults | `#C58CFF` |
| HAVOC | Havoc Brothers | `#E8352E` | | TITAN | Titan Lords | `#FFD447` |
| H-ELF | High Elf Fleets | `#8EC9FF` | | DEMON | Wormhole Daemons (all four) | `#FF2E88` |
| HDF | Human Defense Force | `#8CC63F` | | | | |

**Pairings are hard-coded:** the clash groups in `docs/menu-background/army_pairings_check.py`
(`CLASH_GROUPS`) are the source of truth. Two armies sharing a group are never on the table together,
and no mirror matches. The script asserts every allowed pair is >= 35 delta-E apart and every army keeps
>= 12 opponents (currently 238 allowed pairs, min 16 opponents). Port the table verbatim to C#, and
re-run the script after any colour or group change.

## Slice plan
1. **[done 2026-10-30] Static scene + menu layout.** Projected table (rim, grid, ticks, labels) + terrain wireframes
   into a render texture, bloom/scanline/vignette post-process; `MainMenuScreen` window made transparent,
   layout A + C, new labels (HOST/SOLO, JOIN), same-size title; privacy popup + Options modal restyled
   to match the panel.
2. **[done 2026-10-30] Armies + units.** Army table + clash groups in C#, random allowed pairing per menu visit, formations,
   flags, labels, casualties, trails.
3. **[done 2026-10-30] Activity + line of fire.** Tracers, fire, pulse, measuring line; shooter/target choice rejects lines
   crossing terrain. Fork to settle first: reuse the engine's LOS against real terrain pieces vs a local
   2D footprint test (cosmetic only, as in the mockup). **Decided 2026-10-30: the simplest - the local
   2D footprint test** (owner: "use whatever is the simplest").
4. **Motion + camera director.** Drift, glide to skirmishes, unit advances; optional click-to-dive
   transition on leaving the menu.
5. **Options toggle** (Animated / Static / Off) + perf check on a weak GPU; pause/dim while a modal is open.

## Notes
- 2026-10-30: **Slice 3 implemented.** `HoloEngagement.Plan` (pure): enemy squad pairs within 30" whose two
  lines of fire (shooter's 2 models nearest the target -> target's 2 nearest the shooter) are both
  `IsClear` (2D segment >= 0.4" from every footprint, neither end inside one), nearest first, up to 3
  fights with distinct shooters; vehicles excluded so they carry the measure. Fire on a targeted unit
  with casualties (else the first target), at its casualties' centroid. Measure: vehicle to vehicle,
  base edge to base edge (reads `d - r1 - r2`), falling back to the nearest clear idle pair. Pulse centre
  = mean of the engaged units. Renderer now redraws every frame (cache removed): pulse ring (4.5s, out to
  26", clipped to the table, two echoes) that also brightens terrain pieces it crosses; faint line of
  fire + two travelling white-hot dashes + muzzle flash per shot; three-layer flickering flame tongues
  with rising embers and heat rings; measure draws out over 1.2s, holds to 5.5s with its `27.3"` label,
  fades by 6.3s, rests to 7s. The tracer/fire/pulse/measure ANIMATION landed here rather than slice 4
  (they are animated by nature); slice 4 keeps camera motion, unit advances and the director.
  `MenuBackgroundRenderer` split into `Prepare` + `Present`: its passes switch render targets and end on
  the default framebuffer, so the offscreen preview (which draws into its own target) must prepare
  outside that target - found when the first per-frame preview came out blank (the cached frame had
  masked it); the game draws straight to the screen and was never affected. Preview clock frozen at 3s.
  Tests: `MainMenuEngagementTests` (7) - `IsClear` cases (through, grazing within pad, starting inside,
  clear, beside), every shot + measure clear for all 476 ordered pairings, fight shape (<=3 fights, both
  sides, no friendly fire, vehicles kept out, fire on a casualty-bearing target, vehicle measure),
  base-to-base distance, shots from/to living models within range, pulse on table, and a blocked-table
  plan (walls across every open-table line -> new choices must clear them). Mutation-checked: removing
  the shot or measure LOS filter was invisible to the first six tests (the real layout's nearest picks
  are clear anyway) - the blocked-table test was added for exactly that and now reddens on both; no pad,
  centre-to-centre measure and fire-on-last-target each redden their test.
- 2026-10-30: **Slice 2 implemented.** `MenuArmies` (25 armies + 12 clash groups, `CanFace`, `AllowedPairs`
  = 238, `PickPair` uniform over pairs with random near/far), `HoloSkirmish` (the mockup's 8 unit slots:
  near = 3-model command group, 10 squad, 5 squad, vehicle; far = 8 squad with 2 casualties, 5 squad,
  3 large bases, walker; formations seeded per slot; labels `ABBR n`, tag `ABBR n xALIVE`), drawn by
  `MenuBackgroundRenderer.DrawUnits` (base rings, dashed casualties, vehicle inner ring + facing tick, one
  flag + pennant on the leader, tag text, dotted trails). A fresh pairing is dealt each time the menu
  comes back into view (`RaylibRenderer` tracks last frame; Host/Join <-> menu keeps it).
  `--menu-preview` takes a seed (default 1: C-BR vs E-DYN; 7: H-ELF vs D-ELF - H-ELF's pale blue stays
  readable against the terrain cyan). Tests: `MainMenuArmiesTests` (9) - C# table/groups parsed against
  `army_pairings_check.py` (drift fails), pairing rules, every army reachable on the near side, labels,
  squads + one vehicle a side, bases on table / clear of terrain (+0.3") / not overlapping, trails.
  Mutation-checked: colour drift, group drift, squad moved onto a forest, wild jitter, fixed near/far
  each redden their test; dropping the explicit mirror check stays green because every army is in at
  least one clash group (an equivalent mutant - the check guards a future army added to no group).
  Formation jitter tightened to +-0.15" so bases can never touch.
- 2026-10-30: **Slice 1 implemented.** `Rendering/MenuBackground/` (`HoloCamera` fit + lens shift,
  `HoloTableScene` table/grid/ticks/labels + the 13 mockup terrain pieces with tagged footprints,
  `MenuBackgroundRenderer` 2x-supersampled additive scene -> bright-pass + half/quarter Gaussian bloom ->
  scanlines/vignette/panel shade, cached until resize), `MainMenuLayout` (shared by menu + background),
  `HoloUi` (panel, brackets, 3 button tiers, modal style; `UiButton.Tinted` keeps the click cues),
  `IAppScreen.ShowsMenuBackground` (menu + Host/Join dialogs). Privacy notice + Options modal restyled.
  `MenuBoldFont` (bundled `DejaVuSans-Bold.ttf`, ASCII-only atlas) for title + buttons.
  `--menu-preview out.png [WxH] [menu|privacy|options]` renders offscreen (hidden window) - checked at
  1280x720, 1280x1024, 1920x1080, 2560x1080, 3840x2160. Tests: `MainMenuBackgroundTests` (29: fit inside
  region on 8 screen shapes, tight fit, buttons inside panel/no overlap, tiers, footprints on table, no
  overlap, piece lines over table, ASCII labels); mutation-checked (region under panel, no lens shift,
  camera too far, box on forest - each reddens only its test).
  **GUI hand-verify:** launch the game - menu shows the war table right of the panel; hover each button
  (frame lights, chevron on the solid tiers); Host/Solo and Join dialogs show the dimmed table behind;
  Options opens the restyled modal; F11 / resize re-fits the table; Quit still exits.
- 2026-10-30: Design iterated with the owner over five mockup rounds (topographic planet section ->
  6'x4' topo table -> terrain-piece table chosen). Army abbreviations/colours chosen by the owner;
  clash groups added at their request and verified by the pairing script (it caught D-ELF/ROBOT and
  DAO/TITAN, which were added to groups). Number 448 filed clean (see Reconciliations).

## Decisions
- **Projected 2D lines, not Camera3D/GL lines** (slice 1): wide GL lines are not portable (core profile caps
  them at 1px), and projecting every point through one camera function means text, flags and later
  overlays line up with the terrain for free. Depth sorting isn't needed - a hologram is see-through.
- **Bloom bright-pass at ~0.16**: without it the dense 1" grid and the projector pool blurred into a solid
  blue haze over the table (first real render); with it only terrain, rim and labels glow, as in the mockup.
- **Bold title/buttons via a bundled `DejaVuSans-Bold.ttf`** (same family and licence as the existing
  `DejaVuSans.ttf`), baked for printable ASCII only to keep the second large atlas entry small.
- The Host/Join dialogs also opt into the background (behind their existing 60% backdrop) - they open
  from the menu, and a flash to flat grey between them looked broken.
- Terrain pieces over topography: reads as a real tabletop game; topography can't be "carved out" of a table.
- Tracers white-hot instead of yellow so gold/amber/yellow armies (TITAN, RAT, E-DYN) don't blend with fire.
- Labels use a space before the number because several abbreviations contain hyphens.
- Pairing exclusions are hand-listed groups, not a computed colour distance, so the owner controls them;
  the delta-E script is only a guard that the groups didn't miss a pair.

## Outcome
