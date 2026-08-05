# 331 — "Deploy Normally" is its own button, not the Back key

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #035 slice B (deploy-time embarking), #308 (a resolver must not invent cancel wording), #161 (resolver consistency)

## Goal
Playtest note (2026-08-04, Chris): *"Deploying into a transport is awkward in that you have to press Back to
do it. It should be a separate button that says 'Deploy Normally'."*

When a unit is deployed while a friendly transport with room is already on the table,
`ChooseDeployActionStage` offers an embark prompt whose only other exit was the resolver's generic **Back**
button — an escape hatch, not a choice. Done means the second way to deploy names itself in both front ends,
and that the deployment prompt reads as two alternatives rather than one option plus a way out.

## Notes

- 2026-08-04: **Shipped.** `SelectionRequest<T>` gained `CancelLabel` (default `"Back"`, `DEFAULT_CANCEL_LABEL`),
  worded by the stage, exactly like `PlaceObjectsRequest.CancelHint` from #308.
  - `ChooseDeployActionStage` passes `cancelLabel: DEPLOY_NORMALLY_CHOICE_NAME` ("Deploy Normally"), drops the
    parenthetical "(Cancel to deploy normally.)" from its instructions — now
    "Deploy Grunts inside a transport, or on the table?" — and passes a `displayName` so the #322 "Waiting on"
    HUD stops showing the C# type name (`Select UnitData`).
  - `GuiSelectionResolver<T>` labels the exit button from the request and takes its key hint from
    `ResolverKeybinds.Back` (#295) instead of the hard-coded "Back  (Backspace)". A *named* exit also loses the
    dim back-out tint — it is an action, not an escape hatch — while a plain Back keeps it.
  - `SelectionResolver<T>` (CLI): **`AllowCancel` had no CLI representation at all.** Headless play could not
    decline the transport in any way; with an eligible transport the only inputs accepted were 1..N, all of
    which embark. Now `[0] <CancelLabel>` is listed and replies null like the GUI button.
  - The mid-game `EmbarkStage` prompt deliberately keeps "Back": cancelling that one really does return to the
    action menu, which is what the default is for.
- 2026-08-04: Tests — engine `TransportDeploymentChoiceTests` (+2: the prompt names the choice and stops
  explaining Back; an unlabelled request still says "Back"), app `SelectionResolverTests` (new, 4: the `[0]`
  row is listed and named, defaults to Back, is absent + rejected on a mandatory selection, and EOF still
  auto-picks option 1). Engine 2762 green, app 1017 green, `dotnet build` clean, headless smoke exits 0.
- 2026-08-04: **Hand-verified headless end to end** with `TransportTest.fdgarmy` (Car = Transport(11), Guys =
  5 models): the real deployment prompt prints `[1] Embark into Car` / `[0] Deploy Normally`, typing `0`
  places all 5 models on the table with no "embarked" log line, and the game plays out to a result. The GUI
  button itself is still ImGui drawing, so it stays on the hand-verify list.

## Decisions

- **The exit is relabelled, not duplicated.** The alternative was adding "Deploy Normally" as a third option
  ROW, which needs a sentinel `DataBinding<UnitData>` in a list typed to transports — a null-ish row every
  `SelectionRequest<UnitData>` consumer would then have to special-case. One label on the request reaches both
  front ends, keeps Backspace working for players who already learned it, and leaves the reply contract
  (null = don't embark) untouched.
- **Wording belongs to the stage, resolvers just draw it** — #308's rule, now applied to the cancel *label*
  the same way it was applied to the cancel *hint*. Only the stage knows whether cancelling rewinds or acts.
- **CLI EOF still takes option 1, cancellable or not.** Making EOF cancel would be the friendlier answer for
  this one prompt and a hang for others: a stage that re-prompts after a cancel (melee defender -> Choose
  Action -> melee defender) would spin forever under piped input. Pinned by a test so the reasoning survives.
- **Not fixed here, deliberately**: `AiSelectionResolver` always takes option 0, so an AI army with a deployed
  transport embarks every eligible unit into it rather than weighing the choice. That is AI behaviour, not
  resolver UX; noted for the Tactician deployment work (#191 / #296) rather than folded in silently.

## Outcome
