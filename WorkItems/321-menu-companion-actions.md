# 317 — Companion actions: a second button ON the option's row, not a second row

**Status**: in-progress (implemented + tested + CLI hand-verified; awaiting GUI hand-verify)
**Related**: #316 (melee hold-back, the first user of this), #315 (shooting's Hold fire, which is a
footer button and needs none of this), #248 (letter hotkeys), #298 (option subtext)

## Goal
"Attack with this weapon" and "hold this weapon back" are one decision about one weapon, and must read
that way: a second button to the right of the weapon's row sharing its hotkey under Shift — not a peer
list entry whose connection to the weapon the player has to infer from a text prefix.

## Notes

- 2026-08-02: **Implemented. Engine 2589/0, app 894/0, full build clean, default headless smoke exit 0,
  melee menu hand-run in the CLI. User request: "a second button, to the _right_ of the weapon, for
  skipping it, instead of a second list item... that other button should get the same hotkey but with a
  modifier. So if E swings the limited weapon, Shift + E should skip it."**

  **Wire** (`StringSelectionRequest.SecondaryActions`): `Dictionary<string, SecondaryAction>?` mapping an
  option to the companion that BELONGS to it, where `SecondaryAction` is `(Option, ShortLabel)` — the
  companion's own option string (still an ordinary entry in `ValidOptions`/`InvalidOptions`, replied to
  by that string) plus two words for the button. Same optional-per-option-metadata shape as
  `OptionDescriptions`, and a resolver that ignores it still works: the companion just appears as a row.

  **GUI** (`GuiStringSelectionResolver`): companions are skipped when building rows AND when handing out
  letters (`CompanionOptions` / `AssignRowLetters`), then drawn as a right-hand button on their owner's
  row, sized to their text and capped at 42% of the row so a long verb cannot squeeze out the weapon
  line. Label `"[^E] Hold back"` — a caret rather than a Unicode shift glyph, per the ASCII rule. A
  refused companion is drawn greyed with its reason as a tooltip; an available one tooltips its own
  description ("Keeps its Limited once-per-game use for a later melee"), which has no room on a button.

  **Hotkeys** (`ResolverHotkeys.IsLetterPressed(char, bool shift)`): the modifier now DISCRIMINATES —
  the plain overload means "letter, without Shift". It had to: one physical Shift+E would otherwise fire
  both the row and its companion, since `ImGui.IsKeyPressed(E)` is true either way.

  **CLI** (`StringSelectionResolver`): the same pairing in text — the companion prints under its owner as
  `[s3] Hold back` with its description indented beneath, and `sN` selects it. Refused ones print as
  `[--] Hold back (reason)`. EOF still answers the first non-companion option.

  **AI**: `AiStringSelectionResolver` now skips companions generically (any value of `SecondaryActions`)
  rather than by #316's melee-specific prefix.

  **Tests**: 5 new app-side (`GuiStringSelectionCompanionTests`: companion set, letters skip companions
  without shifting the others, available/refused/absent companion labelling) + 2 engine (the melee stage
  declares each hold-back as its weapon's companion, refused ones included).

## Decisions

- **The companion stays an ordinary option on the wire.** The map only says who owns it. That keeps every
  existing resolver correct without knowing about any of this (it renders as a row, exactly as before),
  keeps the reply a plain string, and means the refused case needs no special casing — it is just an
  `InvalidOption` that happens to be owned.
- **Generic on `StringSelectionRequest`, not a melee-specific request type.** The pairing is a
  presentation fact about option lists, and a melee-only request would have forced both front ends to
  grow a second weapon-menu implementation. #316's `HOLD_BACK_PREFIX`/`IsHoldBackChoice` survive for the
  stage's own bookkeeping and tests, but no resolver keys off them any more.
- **Companions do not take pool letters.** They share their owner's under Shift, so handing them one
  would burn the ten-letter pool twice as fast and shift every later option's letter — the letters array
  stays indexed by valid-option index with null holes instead.

## Outcome
(pending — GUI hand-verify)
