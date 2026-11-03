# 348 — Cast-assist animation: does it stream from casters that did not spend?

**Status**: in-progress (investigated + regression-tested + prompt disambiguated; awaiting GUI hand-verify)
**Related**: #103 (cast assist), #274 (the assist visual), #244 (self-boost)

## Goal
Owner report: "if you spend any points from another unit to help a caster out, it looks like it plays an
animation from ALL the nearby friendly casters that COULD have done this, whether or not they did."
Verify, and fix if true.

## Notes

- 2026-08-05: **Verified — the beat is correct.** `CastSpellStage.CollectCastAssist` appends
  `boostSources` / `hinderSources` only *after* `if (spent <= 0) continue;`, so a caster that declines
  contributes no source, and `SpellOverlay.DrawAssist` streams strictly from `beat.Sources`. Pinned with
  a new integration test, `CastSpellStage_OnlySpendingAssistersStreamIntoTheCaster`: three eligible
  friendly Casters, one spends and two decline — the `AssistBoost` beat carries exactly the spender's
  position and Magnitude 2, and the decliners keep their tokens. The pre-existing #274 coverage could
  not have caught this either way: its `CannedAssistRequester` had *every* eligible assister spend, so
  "only spenders" and "everyone offered" produced identical beats. The requester now takes an optional
  per-prompt override.

- 2026-08-05: **Fixed the thing that actually reads that way.** #103 offers the assist window to every
  eligible Caster *in turn*, and `GuiCastAssistResolver` drew each prompt as a SOLID coloured line from
  that caster into the caster — visually identical to the assist stream `SpellOverlay` plays for real
  spenders. So a cast with three friendly Casters nearby showed three of those lines in sequence,
  whether or not any of them spent. The prompt line is now DASHED and its label reads
  "{unit} - deciding (N tokens available)": the dashed line is the question, the solid stream before the
  roll is the answer, and only spenders get one.

## Decisions

- **No engine change.** The report described a symptom, not the mechanism; changing `CollectCastAssist`
  would have been a fix to correct code. The regression test is the durable half of this item.

- **The AI is not a second cause, but can look like one.** `TacticianCastAssistResolver` spends from
  every eligible caster whose valuation clears the bar, so on an AI cast several nearby AI casters
  genuinely *do* assist and genuinely *should* all animate. Worth remembering before the next report of
  this shape.

- **Not done: naming the spenders on the stream itself.** `SpellEffectBeat` carries positions and a
  total magnitude, no unit names, so the animation cannot say "Cabal of Change +2". The per-spend Toast
  banner already does ("X assists Y's cast of Z (+2)"). Flagged as the follow-up if the dashed-prompt
  change does not settle it.

## Outcome
_(pending)_
