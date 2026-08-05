# 337 — A Shaken unit's activation looked like any other in the picker

**Status**: implemented + tested + headless-verified; awaiting GUI hand-verify
**Related**: #315 (the transport suffix this stacks with and copies), #292/#336 (the inline rule-hover
treatment the badge reuses), #008 (the Shaken-activation rule itself), #206/#334 (the forced-charge
proximity gate this was mistaken for), #338 (the banner that was supposed to carry this and was too brief)

## Goal
Playtest note (2026-08-04, Chris): *"When choosing which unit to activate, if any are shaken, that should
be made very clear, like with colored text or something, to say '(Unshake)' or something. If possible, that
text should be hoverable."*

Reported in the same breath as *"I got really really close and for some reason didn't have to charge"* — and
those turn out to be the same event. See **Diagnosis** below.

Done means: the activation list says which units are Shaken, in colour, and the marker explains itself on
hover; both front ends carry it.

## Diagnosis (2026-08-04)

The "didn't have to charge" report is **not** a forced-charge bug. Verified two ways before touching
anything:

1. **Read the path.** `ChooseActionStage.GetCanPass` delegates to
   `ForcedChargeUtilities.AnyEnemyWithinStandoff` -> `UnitCompareUtilities.MinDistanceBetweenUnits(...,
   includeVertical: true)` -> `IModelExtensions.BaseDistanceToOtherModel_3D` ->
   `BaseShapeGeometry.SurfaceGap2D`. Base-to-base, shape- and facing-aware, at every hop. There is no
   centre-to-centre comparison anywhere on the chain (`Position.GetDistance2D/3D` has no caller in the
   proximity family).
2. **Ran it.** A scenario with a 0.55"-radius circular-based squad 0.30" base-to-base from a 1.0"-radius
   circular-based enemy — the exact both-bases-circular pairing reported — greys Pass out with *"Within 1"
   of an enemy - must charge (or reposition) rather than stand idle."* and the CLI move preview prints
   *"FORCED CHARGE: 3 models end within 1" of Round Sentinel."*

What DOES bypass the gate: **a unit that started its activation Shaken never reaches the action menu at
all.** `ChooseActionStage.Enter` sees `StartedActivationShaken`, announces, and routes straight to
end-of-activation. Standing nose-to-nose with an enemy, it declines to charge and the activation is simply
over — which is exactly the reported symptom, and reads as the proximity rule being broken.

The only thing that said so was a Toast banner that had already faded (#338, reported in the same message).
The picker itself listed the unit like any other. So the fix for the forced-charge report is this item, not
a change to the rule.

Regression cover added anyway, so the measurement can never quietly become centre-based:
`ChooseActionPassDisableTests.GetCanPass_LargeCircularBases_MeasuredBaseToBase_NotCentreToCentre` — two
3"-diameter circles with centres 3.5" apart (bases 0.5" apart) must gate Pass; a centre-distance check would
call them four times clear. Plus its converse, plus a test pinning the Shaken bypass as deliberate.

## Approach

**Engine.** `Utilities/UnitStatusLabel` owns the badge text (`ShakenSuffix = "(Shaken - recovers)"`) and its
hover body, which is the token catalog's own Shaken description rather than a second copy of the rule.
`ChooseUnitToActivateStage.GetOptionLabel` appends it last, after the #315 transport suffix, so a Shaken
passenger reads `Warriors (in Rhino) (Shaken - recovers)`. Engine-side for the same reason #315 was: the
CLI picker, networked clients and AI-visible labels all get it for free, and the GUI has one string to look
for instead of a rule to re-derive.

Wording: names the state AND what activating it does, because the second half is the part that changes the
decision. (Owner picked this over the bare `(Shaken)` and over the originally-suggested `(Unshake)`.)

**App.** `UnitStatusBadge` locates the suffix inside the *finished* heading and returns
`RuleHoverText.Segment`s — the same treatment #336 gave weapon rules, for the same reason: the label is the
option's identity (#306), so the front end splits it rather than rebuilding it, and concatenating the
segments reproduces the heading verbatim. `GuiSelectionResolver` grew one virtual, `HeadingSegments`,
defaulting to null so every other picker draws through the identical single-`AddText` path it always did;
`GuiUnitSelectionResolver` overrides it. A hovered badge raises the status tooltip and suppresses the row's
full stat block for that frame — the player is asking about the amber run, not about the weapons.

Colour: the amber the Shaken banners use (255,170,60), so the badge and the recovery banner are visibly the
same fact. Dimmed on greyed-out (already-activated) rows.

## Notes

### 2026-08-04 — implemented
Engine 2817/2817, app 1071/1071, `dotnet build` clean, headless smoke exits 0 with
`[1] Blade Squad (Shaken - recovers)` in the CLI picker.

Deliberately NOT done, and not deferred by accident:
- **Fatigued gets no badge.** Offered and declined this pass; it changes melee rolls, not whether the
  activation happens, so it does not meet the bar the Shaken badge is set at. Revisit only on a playtest
  report.
- **Only the activation picker.** The deploy picker uses the same resolver and would light up for free, but
  nothing is Shaken at deploy time.

## GUI hand-verify
`Scenarios/337-shaken-picker.json` (round 2, you are player 1). Blade Squad is Shaken AND standing 0.30"
base-to-base from the circular-based Round Sentinel - the reported situation exactly. Check:

1. The picker row reads `Blade Squad (Shaken - recovers)`, the badge in amber and underlined, the rest of
   the heading in the ordinary colour.
2. Hovering the badge raises the Shaken tooltip, NOT the unit's full stat block; hovering elsewhere on the
   row still raises the stat block as before.
3. Picking it announces the recovery and ends the activation without a charge - which is now explained
   before the click rather than after it. (#338 keeps that banner up long enough to read.)
4. The other two rows are unchanged.

```
dotnet run --project FdgRaylib/FdgRaylib.csproj -- --scenario Scenarios/337-shaken-picker.json
```

## Outcome
_(pending GUI hand-verify)_
