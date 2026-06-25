# 082 — AI & player-controller lifecycle nits

**Status**: done
**Related**: audit §10 (`Audit-6-10-2026.md`); branch `082-network-robustness` (first of #082/#075/#076/#077)

## Goal

Three independent robustness fixes flagged by the audit:

1. `AiYesNoResolver` answered `true` to **every** yes/no, current and future — make the AI default explicit per question via an intent tag on the request, so a future question whose correct AI answer is "no" is honored rather than silently accepted.
2. `LocalPlayerController`'s two-phase UI subscription (null-check, then deferred event subscription) has a race window and can double-subscribe — make subscription idempotent and re-run on assignment.
3. `NetworkPlayerController.IsReady` is set from bus dispatch without idempotency — a duplicate ready message double-fires `OnReadyStateChanged`. Guard it.

## Decisions

- **2026-06-25** — **Intent tag = an explicit `bool AiPrefersYes` carried on `YesNoRequest`**, not a semantic enum. The audit asks for "AI defaults … explicit per question"; a bool *is* the explicit default and is the lightest expression. All 5 production call sites pass `aiPrefersYes: true` by name (each is an opt-in to a free/beneficial ability — strike back, Strafing free attack, Ambush deploy, Reactivate, Scout deploy), so behavior is unchanged but now deliberate and reviewable. A future "the AI should decline" question passes `false`.
- **2026-06-25** — **`AiPrefersYes` is a `[JsonProperty]` private-setter property with a `= true` field initializer, kept OUT of the `[JsonConstructor]`.** Newtonsoft passes a *missing* constructor parameter as the type default (`false`), which would silently flip the AI to "no" for any request whose JSON omits the flag — exactly the silent-default footgun the audit targets. As a settable property the field initializer covers the absent-member case (stays `true`), while `[JsonProperty]` forces Newtonsoft to use the private setter so a present member round-trips. A unit test pins all three cases (present-true, present-false, absent→true).
- **2026-06-25** — `LocalPlayerController.EnsureMessageSubscription()` removes-before-adds (`-=` then `+=`) and runs both in the ctor and on every `OnStageResolverAssigned`. Idempotent + re-runnable closes the null-then-non-null window and the double-subscribe path in one helper. The assignment handler is intentionally **not** unsubscribed (so it can re-run), which is safe precisely because the subscribe is idempotent.
- **2026-06-25** — `NetworkPlayerController.OnPlayerReadyMessageReceived` gains an `&& IsReady == false` guard. AI-takeover / disconnect handling are separate items (#076).

## Notes

- **2026-06-25** — Implemented + verified. Engine suite **767/0**, full `dotnet build` clean (the 2 warnings are pre-existing CS0067 unused-event on the controllers), headless smoke exits 0.
  - `StageResolution/Requests/YesNoRequest.cs`: new `AiPrefersYes` property (see Decisions); convenience ctor gains `bool aiPrefersYes = true`.
  - `Ai/Resolvers/AiYesNoResolver.cs`: returns `request.AiPrefersYes`.
  - 5 call sites set `aiPrefersYes: true` explicitly: `OfferStrikeBackStage`, `StrafingStage`, `StartOfRoundExtraActionStage`, `DeterminePlayerTurnStage`, `DeployUnitStage`.
  - `Players/LocalPlayerController.cs`: `EnsureMessageSubscription()` helper (+ `using FDG.TextInterface`).
  - `Players/NetworkPlayerController.cs`: idempotency guard on the ready handler.
  - `Tests/AiControllerLifecycleTests.cs`: 6 tests (AI honors both prefs, JSON round-trip both values, missing→true, duplicate-ready fires once, other-player-ready ignored). Reuses `RequestSystemTests.MockMessageBusHost`.

## Outcome

Shipped all three fixes. The AI's yes/no defaults are now explicit per question (today all "yes", but deliberate and JSON-safe against a missing flag); the local controller's chat subscription is idempotent and re-runnable; duplicate ready messages no longer double-fire. No behavior change in the happy path — these are robustness/correctness backstops. No GUI hand-verification needed (no new visible behavior).
