# 408 — Activation-start choices (Versatile Attack) block backing out of an activation

**Status**: implemented + tested; awaiting GUI hand-verify
**Related**: #248 (back-out of a pristine activation), #202 (Move/Charge/Disembark back-out), #197 P5a (Activation_OnActivationStart offers)

## Goal

Activating a unit with an activation-start choice never traps the player. Backspace / Back at the "pick
one effect" prompt returns to unit selection with nothing spent, and once an effect is chosen the player
can still back out of the action menu without being forced to complete an action. Every other prompt
that fires at activation start gets the same treatment. "Done" = no activation-start prompt is a one-way
door, with integration tests mirroring the nearest `*RuleIntegrationTests`.

## Notes

- 2026-10-28: **Implemented (apply now, restore on back-out).**
  - **Snapshot.** `ActivationStartStage` captures a `TokenSnapshot` of the unit and its models first
    thing, before the UntilNextActivation sweep. It is stored on `IUnitActionContext.ActivationStartTokens`.
  - **Pick prompt.** A group whose abilities are all `Effect.AddRule` no longer marks the activation
    irreversible, and its pick prompt allows Back (`ChooseAbilityEffectRequest.AllowCancel`, reply
    `CANCEL` = -1). Back restores the snapshot and leaves through the new `ActivationStartStage.OnBackedOut`,
    bound to MainUnitActionStage's #248 exit.
  - **Action menu.** `ChooseActionStage`'s back-out restores the snapshot before leaving. That undoes a
    pick made earlier in the activation, and it also brings back a Versatile Defense grant the sweep had
    retired, which was a latent #248 bug.
  - **Containers.** `ITokenContainer.RestoreTokens` sets the exact snapshot and fires remove/add events, so
    `TokenChangeBroadcaster` syncs the rollback to clients.
  - **Front ends.** The GUI effect panel has a Back button plus Backspace; the CLI lists `[0] Back`. AI
    resolvers never cancel. Deployment's Versatile Defense pick and the end-of-activation pick stay
    mandatory.
  - **Tests.** `ActivationStartBackOutTests` (5, driving the real MainUnitActionStage) covers Back at the
    pick, back-out at the menu after a pick, acting keeping the pick, the sweep being restored, and a
    non-grant ability staying one-way. `TokenContainerTests` covers restore state and events.
  - **Verification.** Engine 3381 (1 skipped) and app 2997 green, build clean, headless smoke exit 0.
  - **Survey outcome.** All 9 activation-start abilities in the corpus that only grant a rule are now
    backable: Versatile Attack, Defense and Reach, Watchborn, Vinci Tech, and the Speed, Protection,
    Precision and Piercing Feats. Still one-way, deliberately, because they roll dice or place models:
    Spawn, Reckless Piercing (rolls), Surprise Attack, Reanimation, and the passive repositions (Wolfborn,
    Bounding, Rapid Blink, Wave-Step). The single-ability Yes/No has no Back, but answering No is safe and
    a Yes is now undone by the menu back-out. Before-attack, Cast and Teleport each still return early on
    Back before their commit point; a custom action resolves on being picked from the menu, an explicit
    choice.
- 2026-10-28: Filed from a Discord report: "when you activate a veteran prime master with versatile
  attack you cannot back out of the main activation stage with backspace. you are forced to pick a mode
  and complete an action". Owner asked for a way back, and a check of other abilities with the same
  problem. Number 408 taken in the same filing pass as #406, no collision.
- 2026-10-28: First look at the cause. `ActivationStartStage` runs before `ChooseActionStage`. For a
  rule offering two or more effects it calls `AbilityEffectChoice.Resolve`, whose
  `ChooseAbilityEffectRequest` is documented as mandatory with no cancel. Before that it calls
  `context.MarkIrreversibleAction()` (#248), because applying the ability spends its cost and grants a
  rule. So the player cannot back out of the pick, and after it the activation is no longer pristine, so
  the action menu cannot back out either.

## Survey: other activation-start prompts with the same shape

- **Pick-one-effect rules**: Versatile Attack, Versatile Defense, Versatile Reach, Watchborn. Same path.
- **Single optional abilities** (Speed Feat style): a Yes/No first. Saying No is safe; saying Yes marks
  the activation irreversible the same way.
- **Passive reposition at activation** (Wolfborn, Bounding, Rapid Blink): `ResolvePassiveEntries` also
  marks the activation irreversible and prompts a placement.
- **Surprise Attack** (`SurpriseAttackStage`, next stage): also marks irreversible.
- **Elsewhere**: `BeforeAttackActionStage`, `CastSpellStage`, `TeleportStage` and `CustomActionStage`
  mark irreversible too, but those follow an explicit action choice, so they are expected one-way doors.
  Confirm each still has a Back before the commit point.

## Options (design fork, needs sign-off before building)

1. **Cancellable pick + deferred apply.** Allow Back on the effect prompt (null sentinel, as other
   resolvers do) and record the choice without applying it; apply it, and mark irreversible, only when
   the first real action commits. Back-out discards the recorded choice. Cleanest for the player.
2. **Cancellable pick + undo.** Apply as now but make the grant and cost reversible on back-out.
   More engine surface; the token grants would need a rollback.
3. **Cancellable pick only.** Back at the prompt returns to unit selection; after picking, the
   activation stays committed. Smallest change, but still forces an action once a mode is chosen.

## Decisions

- **2026-10-28 - apply now, restore on back-out (owner sign-off).** The owner first chose option 1 (defer
  until the first action). Building it showed that deferring hides the pick from the action menu
  (Versatile Reach's +4" range would not enable Shoot) and from the AI's planning. With that tradeoff in
  view the owner switched to a token snapshot/restore. It is safe because every backable ability changes
  only tokens; anything else still marks the activation irreversible.

## Outcome

Pending GUI hand-verify. Activate a unit with Versatile Attack: Backspace at the pick returns to unit
selection. Pick a mode, then Backspace at the action menu: back at unit selection, and re-activating offers
both modes again.
