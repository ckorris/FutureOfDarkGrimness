# 406 — Deploy a unit inside a transport that is in Ambush or not yet deployed

**Status**: implemented + tested; awaiting GUI hand-verify
**Related**: #035 (Transport core, slice B deploy-time embark), #335 (Deploy Normally button), #202 (Ambush reserve is unit state), #404 (reserve units in the army list)

## Goal

At deployment, a unit can always be loaded into a friendly transport with room for it, whatever the
transport's own state: already on the table, held in Ambush (or any equivalent reserve), or not yet
deployed at all. The loaded unit then travels with the transport - it arrives when an Ambush transport
arrives, and is placed on the table with it when the transport deploys later. "Done" = all three
transport states offer the embark choice, the cargo follows the transport through arrival, and each is
covered by an integration test mirroring the nearest `*RuleIntegrationTests`.

## Notes

- 2026-10-28: **Implemented.** `ChooseDeployActionStage.GetEligibleTransports` no longer requires the
  transport to be on the table. It skips only the unit itself, dead transports and transports that are
  themselves embarked, and leaves the rest to `CanUnitEmbark`. Off-table options are labelled
  "Embark into Rhino (in reserve)" / "(not yet deployed)".
  - **Cargo travels for free.** Occupancy is the `EmbarkedIn` token, so nothing has to move: the transport
    deploying normally, holding in Ambush, or arriving from Ambush all leave the token in place.
  - **Gap closed along the way.** The round's activation pool admitted every embarked unit, so cargo of a
    transport waiting in Ambush would have been offered an activation (and a disembark) while the
    transport sat at origin. That was already reachable mid-game through Ambush Re-Deployment.
    The pool now admits cargo only via the new `TransportUtilities.IsAboardTransportOnTable`, and the
    activation list explains the gap as "Aboard a transport in reserve." `GetEffectivePosition` returns
    null for cargo of an off-table transport instead of the origin.
  - Tests: `TransportDeploymentChoiceTests` (the old `UndeployedTransport_NotOffered` pin inverted;
    Ambush offered; off-table capacity counted; no self-ride; cargo joins the activation pool only once
    the transport arrives) and `TransportUtilitiesTests` (reserve transport -> no position, not aboard on
    table). Engine 3367 (1 skipped) green, app build clean, headless smoke exit 0.
  - **Not built, by choice:** a transport that never arrives takes its cargo out of the game with it.
    Nothing new is needed for that; it is how an Ambush unit that never arrives already behaves. The AI
    needed no change: it already embarks at deployment (#191 A5-10) and now simply sees more options.
- 2026-10-28: Filed from a Discord report: "If you ambush a transport, there is no way to deploy a unit
  inside that transport. You should always have the ability to deploy to a transport regardless of
  whether the transport is deployed on the field, deployed to ambush or something equivalent, or
  undeployed." Numbering from `origin/master` at `0174787` after `git fetch`: detail files max **405**,
  archive max **404**, no `40[6-9]` branch on any remote. No collision.
- 2026-10-28: First look at the cause. `ChooseDeployActionStage.GetEligibleTransports` filters out any
  transport where `GetIsOnBattlefield()` is false, by design in #035 slice B ("you can only load a
  transport that is already down"). That one filter excludes both the Ambush case and the
  not-yet-deployed case.

## Facets to settle

- **Ambush transport.** Offer it; cargo must arrive with it on the Ambush arrival, then disembark
  normally. Check the arrival path actually carries `EmbarkedIn` occupants, and what happens if the game
  ends or the transport never arrives.
- **Undeployed transport.** Offer it; cargo is placed implicitly when the transport is placed. Decide
  whether the transport's own later choice to Ambush takes the cargo with it (it should).
- **Order of choices.** A unit that picks the transport first and the transport then picks Ambush, and
  the reverse order, must end in the same state.
- **Capacity.** `CanUnitEmbark` already checks remaining room; confirm it counts occupants of a
  transport that is off-table.
- **Scout / other deploy-time moves** for a transport carrying cargo, and the AI, which currently never
  embarks (#335).
- **UI.** The embark prompt and army list (#404) should say the transport is in reserve or undeployed.

## Decisions

## Outcome

Pending GUI hand-verify. Load a unit into (a) an undeployed transport, then deploy the transport; (b) a
transport held in Ambush, then let it arrive in round 2. The cargo should be absent from the activation list
until the transport lands, then disembark normally.
