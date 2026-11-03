# 335 — "Deploy Normally" is its own button, not the Back key

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Renumbered twice, same day (2026-08-04)**: filed as 331 -> 333 (reconciliation 47: origin/master had
merged and archived 331 = victory fireworks) -> 335 (reconciliation 48: origin/master then landed
333 = confirm unmoved models on Done). Commit messages from before each merge still say "#331" / "#333".
**Related**: #035 slice B (deploy-time embarking), #308 (a resolver must not invent cancel wording), #161 (resolver consistency)

## Goal
Playtest note (2026-08-04, Chris): *"Deploying into a transport is awkward in that you have to press Back to
do it. It should be a separate button that says 'Deploy Normally'."*

When a unit is deployed while a friendly transport with room is already on the table,
`ChooseDeployActionStage` offers an embark prompt whose only other exit was the resolver's generic **Back**
button — an escape hatch, not a choice. Done means the second way to deploy names itself in both front ends,
and that the deployment prompt reads as two alternatives rather than one option plus a way out.

## Notes

- 2026-08-15: **Reversal complete (owner's call, twice in one day): EVERY profile now embarks at
  deploy time** (#191 A5-10 Tactician, A5-10b all profiles). Chris, reviewing a save where the bot
  walked infantry past empty transports: "you should pretty much always do that", then sharpened it
  to the real distinction - deploy-time loading is almost always right, MID-GAME embarking is
  almost always wrong. So: `AiSelectionResolver` accepts the prompt (first offer; Tactician
  refines to tightest fit), both AI layers deploy transports first, and the solo bot gets the
  get-out rule the 2026-08-04 note said was missing (`ShouldDisembark`, 12" arrival trigger).
  What SURVIVES of the decline below: mid-game Embark stays filtered for every profile, and the
  DEPLOY_NORMALLY_CHOICE discriminator both layers key on is unchanged. Tests:
  `TacticianDeployEmbarkTests`, flipped `AiSelectionResolverTests` / `TransportDeploymentChoiceTests`,
  +3 disembark-timing cases in `AiStringSelectionResolverTests`.
- 2026-08-04: **The AI never embarks** (owner's call, same session): *"It's very rarely the correct thing to
  do in a real game, and requires more forethought than that level of AI has."* Two seams, both in the AI
  layer rather than the rules:
  - `AiSelectionResolver<T>` declines the deploy-time prompt (null = deploy normally) when the exit carries
    the `DEPLOY_NORMALLY_CHOICE` label. Deliberately narrow: a blanket "AI cancels cancellable selections"
    would loop every prompt that re-asks after a cancel (melee defender -> Choose Action -> melee defender),
    so the decline keys on the one label that means "this cancel is a choice". Covers all three profiles —
    solo, Gunline, and the Tactician, whose `TacticianUnitSelectionResolver` already documents embark picks
    as falling through to the solo resolver.
  - `AiStringSelectionResolver.ChooseAction` filters `Embark` out of its position-based tail (the ranked
    branches — Charge/Move/Shoot/Pass — can never return it, since Embark arrives as a rule-NAMED action).
    It is still returned if it is the ONLY valid option: the fallback must stay inside `ValidOptions` or
    `ChooseActionStage` faults, and a fault is worse than one unwanted ride.
  - Dedupe found on the way: the Ambush hold prompt already had `ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE
    = "Deploy normally"`, so the embark exit briefly made the same action read two ways in one phase. Now one
    constant, title-cased to "Deploy Normally"; `ChooseDeployActionStage`'s own duplicate constant is gone.
    Every reference (both AI resolvers, `TacticianActionResolver`, tests) goes through the constant, so the
    Ambush prompt just picks up the new casing.
  - Tests: `AiSelectionResolverTests` (new, 3 — declines the embark prompt, still ANSWERS an ordinary
    cancellable selection, still picks option 0 on a mandatory one), `AiStringSelectionResolverTests` (+2 —
    never embarks from the fallback, but answers with it when it is the only option), and
    `TransportDeploymentChoiceTests` (+1 — the real `AiSelectionResolver` driven through the real stage).
    Engine 2768 green, app 1017 green, build clean.
  - Verified headless with the transport army in the **AI's** hands (`printf "2\n1\nTransportTest.fdgarmy\n"`):
    zero "embark" lines in the entire game log, game plays to a result. Before this it embarked at deployment.
  - **Not changed**: an AI unit that starts embarked (scenario or save) still has no policy for getting out
    under the solo profile — the Tactician has A5-5 disembark timing, the solo bot passes. Pre-existing, and
    now only reachable from authored setups; noted rather than fixed.

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
- **The AI declines rather than scores.** `AiSelectionResolver` taking option 0 meant an AI army embarked
  every eligible unit into the first transport in the list. The fix is a flat "never", not a heuristic:
  embarking only pays off with a plan for the drop-off, and neither AI profile has one (the Tactician's
  A5-5 note records cargo riding until the transport died). A future embark policy has one obvious home in
  each resolver, both marked with #335.
- **The decline is keyed to a label, not to cancellability.** Replying null to any cancellable selection
  would livelock the prompts that re-ask after a cancel. Matching `DEPLOY_NORMALLY_CHOICE` follows the
  existing Ambush-hold idiom in `AiStringSelectionResolver` — the AI declines a specific named choice it
  cannot follow through on.

## Outcome
