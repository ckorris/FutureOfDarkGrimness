# 448 — Holographic war-table main menu background

**Status**: todo (design signed off 2026-10-30)
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
1. **Static scene + menu layout.** Camera3D-rendered table (rim, grid, ticks, labels) + terrain wireframes
   into a render texture, bloom/scanline/vignette post-process; `MainMenuScreen` window made transparent,
   layout A + C, new labels (HOST/SOLO, JOIN), same-size title; privacy popup + Options modal restyled
   to match the panel.
2. **Armies + units.** Army table + clash groups in C#, random allowed pairing per menu visit, formations,
   flags, labels, casualties, trails.
3. **Activity + line of fire.** Tracers, fire, pulse, measuring line; shooter/target choice rejects lines
   crossing terrain. Fork to settle first: reuse the engine's LOS against real terrain pieces vs a local
   2D footprint test (cosmetic only, as in the mockup). **Decided 2026-10-30: the simplest - the local
   2D footprint test** (owner: "use whatever is the simplest").
4. **Motion + camera director.** Drift, glide to skirmishes, unit advances; optional click-to-dive
   transition on leaving the menu.
5. **Options toggle** (Animated / Static / Off) + perf check on a weak GPU; pause/dim while a modal is open.

## Notes
- 2026-10-30: Design iterated with the owner over five mockup rounds (topographic planet section ->
  6'x4' topo table -> terrain-piece table chosen). Army abbreviations/colours chosen by the owner;
  clash groups added at their request and verified by the pairing script (it caught D-ELF/ROBOT and
  DAO/TITAN, which were added to groups). Number 448 filed clean (see Reconciliations).

## Decisions
- Terrain pieces over topography: reads as a real tabletop game; topography can't be "carved out" of a table.
- Tracers white-hot instead of yellow so gold/amber/yellow armies (TITAN, RAT, E-DYN) don't blend with fire.
- Labels use a space before the number because several abbreviations contain hyphens.
- Pairing exclusions are hand-listed groups, not a computed colour distance, so the owner controls them;
  the delta-E script is only a guard that the groups didn't miss a pair.

## Outcome
