# 370 - Ability-derived action menu entries carry no descriptor text

## Goal

The Choose Action menu lists every activated ability as a bare rule name ("Courage Buff",
"Guarded Buff", "Precision Shooter Buff" on a Blessed Sisters Procession Altar). Nothing on the
row says what taking it does, so a player has to already know the book to use it. Built-in
actions at least explain themselves when greyed out ("Move (Procession Altar has already
moved.)"); a bonus action explains itself never.

Every such rule already carries a player-facing `SpecialRuleDefinition.Description` - all 66
book rules with activated abilities have one, as do all 93 core-catalog rules - and
`StringSelectionRequest.OptionDescriptions` already renders as subtext under the button (GUI)
and as indented lines (CLI). The text simply was not being plumbed from the offer to the
request.

## Notes

### 2026-08-08 - implemented

- `AbilityOffer` gains `Definition` (the `SpecialRuleDefinition` the ability came from).
  `RuleEvaluator.GatherOffersFromRules` already holds the `ResolvedRule`, so it fills it in;
  defaults to null so directly-constructed offers (tests) are unaffected.
- `ChooseActionStage` collects `Definition.Description` for every offer-derived menu entry -
  generic custom actions, Disembark, Embark (valid AND greyed), Teleport, Storm of X, and the
  before-attack abilities - into `optionDescriptions` on the request.
- Chose `OptionDescriptions` over `OptionRules`: the row's whole label IS the rule name, so
  always-visible subtext is the point; `OptionRules` (#336) exists to underline a rule name
  sitting inside a longer label and hover it, which would hide the text behind a hover here.
- Front ends needed no change - both already render `OptionDescriptions`.

### Deferred / out of scope

- Built-in actions (Move / Charge / Shoot / Cast / Pass) still have no descriptor text when they
  are AVAILABLE; they only explain themselves when greyed. Not part of this item.
- A rule whose `Description` is empty still shows a bare label. No corpus rule with an activated
  ability is in that state today, so nothing is synthesized as a fallback.

## Outcome

Implemented + tested (engine suite 2936/2936 green, 4 new tests in
`Tests/AbilityActionDescriptionTests.cs`). CLI-verified end to end against the real
`2k - Blessed Sisters.fdgarmy`: the Procession Altar's menu now prints all three buff descriptions
under their rows. Awaiting GUI hand-verify.
