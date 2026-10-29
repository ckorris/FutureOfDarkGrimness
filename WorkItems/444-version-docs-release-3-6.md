# 444 — Version, docs, release for OPR 3.6

**Status**: todo (last implementation item; runs after every other upgrade item is closed)
**Related**: #415 (umbrella), #445 (verification, runs after this item's doc/version changes and before the merge)
**Branch**: `feature/opr-3.6` in both repos until the very last step of this item. Model/effort: Sonnet 5.5 / medium.

## Goal

The release surfaces say 3.6 everywhere, the branch is merged, and `v0.4.0` (plan Q7 default) is tagged so
CI publishes "Future of Dark Grimness v0.4.0 (OPR 3.6.0)". Concretely:

1. `RULES_VERSION` -> `OPR_3_6_0` in `scripts/build-dist.sh` and `.github/workflows/release.yml`.
2. `Directory.Build.props` `<Version>` -> `0.4.0` (or whatever Chris answers on Q7).
3. `CLAUDE.md`: the Versioning section's example strings (`OPR_3_5_1`, `v0.2.0 (OPR 3.5.1)`, `0.2.0+opr.3.5.1...`)
   become 3.6.0 examples; the Work Items paragraph can mention the upgrade plan is archived.
4. `README.md`: ruleset version, book counts (48 GDF + 40 AoF), the renamed/new books, a one-paragraph
   "what 3.6 changed for players" note, and the note that 3.5 saves are not loadable while 3.5 army files are.
5. `docs/EngineNotes.md` and `docs/ResolverGuide.md`: every 3.5 number or rule facet the upgrade changed
   (melee 2" -> 3", Counter's impact facet -> Brace, Indirect move penalty, Scout zone, Transport 2" -> contact,
   Hero morale, Fatigue 6s, "all models" -> "most models") - grep both files for `3.5`, `2"`, `Counter`,
   `Indirect`, `Scout`, `Transport`, `all models` and fix each line against the shipped code.
6. `SpecialRulesAudit.md`: a dated header note that the corpus is 3.6 now and the 3.5 audit text is historical.
7. `WorkItems/`: #415-#445 closed and archived per `WorkItems/README.md`; `docs/opr-3.6-upgrade-plan.md`
   gets a final "shipped" status line (keep the file; it is the record).
8. Merge: `git -C FutureOfDarkGrimness checkout master && git merge --no-ff feature/opr-3.6 && git push`,
   then in the superproject `git checkout master && git merge --no-ff feature/opr-3.6`, confirm the
   submodule pointer is the engine master merge commit, push, `git tag v0.4.0 && git push --tags`.
   Before merging, `git fetch origin` and re-verify that `master` has not moved past what the branch
   already merged (CLAUDE.md: re-verify assumptions before shared operations). Stop and report if it has.

## Rules for the agent

Everything in plan section 1. This is the ONLY item allowed to touch `master`, and only in step 8, and only
after #445 has been run on the final branch state and Chris has said "merge". Steps 1-7 are ordinary
branch commits.

## Acceptance

`scripts/build-dist.sh` (local, dev) produces archives named `FdgRaylib-<rid>-v0.4.0-OPR_3_6_0.*`; `git tag`
shows `v0.4.0`; CI release job green with the title containing "(OPR 3.6.0)".

### Survey rows (code survey 2026-10-29)

- Version: `scripts/build-dist.sh:40` (+ comments :24-28, :39, :51-56, :64-65; `RULES_TOKEN` :56; stamp :58;
  suffix :66); `.github/workflows/release.yml:23` (+ :20-22, :66-67, :71); `CLAUDE.md:29-34`;
  `Directory.Build.props:11`; `FdgRaylib.Tests/AnalyticsTests.cs:144` ("+opr.3.5.1").
- Docs numbers: `docs/EngineNotes.md:33` (3" objective), :43 (D3+2), :48 (1" standoff), :52 (2"/4" melee), :56
  (Fatigue 6s), :57 (Rout at half), :62 (Ambush round 2 / 9"), :63 (Dangerous d6 per model);
  `docs/ResolverGuide.md:58` (9" coherency), :244 (Difficult 6"), :251 (1" spacing), :259 (charge gated on the
  2" constant), :260 (1" standoff band); :92 is a UI tolerance, not a rule.
- Engine comments citing v3.5.1: `ObjectivePlacementValidator.cs:14`, `PlaceObjectivesStage.cs:4`,
  `MoraleUtilities.cs:65, :239, :278`, `MeleeRangeUtilities.cs:7`, `PileInUtilities.cs:6`, `CastSpellStage.cs:89`,
  `ResolveRangedMoraleStage.cs:38`, `ChooseActionStage.cs:109, :734`, `CoreRuleCatalog.cs:481`;
  `WorkItemsList.md:89, :95, :108, :259`.
- README: `README.md:8` (GDF-only framing), `:128` (stale "AoF not yet"); no version string or counts today.
- Constants table `FutureOfDarkGrimness/Utilities/GameWideConstants.cs` (:6, :8, :10, :16, :22, :27, :32, :34,
  :41, :43, :53, :58, :65, :72), `TeleportStage.cs:29` - the numbers the docs must match.

## Notes

- 2026-10-29: filed with the plan.

## Decisions

(none yet)

## Outcome

(open)
