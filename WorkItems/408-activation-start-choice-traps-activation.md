# 408 — Activation-start choices (Versatile Attack) block backing out of an activation

**Status**: todo
**Related**: #248 (back-out of a pristine activation), #202 (Move/Charge/Disembark back-out), #197 P5a (Activation_OnActivationStart offers)

## Goal

Activating a unit with an activation-start choice never traps the player. Backspace / Back at the "pick
one effect" prompt returns to unit selection with nothing spent, and once an effect is chosen the player
can still back out of the action menu without being forced to complete an action. Every other prompt
that fires at activation start gets the same treatment. "Done" = no activation-start prompt is a one-way
door, with integration tests mirroring the nearest `*RuleIntegrationTests`.

## Notes

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

## Outcome
