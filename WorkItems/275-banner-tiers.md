# 275 — Banner tiers (Headline / Notice / Toast)

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #056 (presentation beat stream), #053 (sound cues), #232 (casualty cascade — same "Held means its own track" shape), #245 (dice caption strip)

## Goal

Split the single, always-blocking `BannerBeat` into prominence tiers so the big announcement stops
meaning nothing. Done looks like: three tiers with distinct sizes, sounds, and screen bands; the two
lower tiers play concurrently with other beats instead of halting the game; and every one of the 28
existing announce sites re-tiered deliberately rather than inheriting the loudest one by default.

## Notes

- 2026-07-24: Notice cue level fixed. Measuring the set showed the low hit at peak 41% / RMS 17% --
  louder than the Headline chime (33% / 15%) it is meant to sit under, on the tier that fires most
  often. Softened to peak 29% / RMS 11% (lower amplitude, snappier decay, shorter tail), giving a
  monotonic Headline > Notice > Toast (13% / 5%). Numbers are in the source comment so a future
  real-asset mix has the same target.
- 2026-07-24: Headline and Notice cues swapped on owner call. Final pairing: **Headline** = the
  original two-note chime (C5 -> G5), **Notice** = the low struck hit. The familiar chime is what the
  game has always sounded like when announcing something, so it earns the rare tier; the low hit is
  non-melodic and sits underneath it, which suits the tier that fires most often.
- 2026-07-24: Headline cue retuned on owner feedback ("too happy and big"). The first pass was a
  rising major arpeggio (C4-C5-G5-C6, ~0.69s), which reads as a victory fanfare — wrong for a phase
  change, and wrong for the setting. Replaced with a struck low hit: 30ms noise transient into a low
  sine sagging B2 -> F2 over 0.44s. Non-melodic on purpose. Candidates were rendered to .wav via a
  throwaway test harness and auditioned before picking; rejected alternatives were a descending minor
  two-note (still a motif), a plain low swell (no transient, too soft to read as an announcement), and
  a transient + falling fifth (closest runner-up, slightly too cinematic).
- 2026-07-24: Implemented. Engine: `EBannerTier { Headline, Notice, Toast }` on `BannerBeat`, driving
  `Held` / `HoldLeadIn` / `NominalDuration`; `PresentationDurations` gained `BannerNotice` (900ms),
  `BannerNoticeLeadIn` (300ms), `BannerToast` (2200ms). `Announce(...)` gained a `tier` parameter
  defaulting to **Notice**. App: `PresentationPlayer` gained a concurrent held-banner track (cap 5,
  excluded from `IsAnimating`); `BannerOverlay` grew `DrawHeld` with a Notice band at y=40% and a toast
  ticker under the status HUD; `PresentationSoundCues` split `banner` into `banner-headline` /
  `banner-notice` / `banner-toast`.
- 2026-07-24: Re-tiered all 28 sites — 4 Headline, 15 Notice, 9 Toast (table below).
- 2026-07-24: Suites green — engine 2095/2095, app 574/574. Headless smoke exits 0, full 4-round game
  to a tie. The smoke log shows 18 activation banners in 4 rounds, which is the repetitiveness that
  motivated the item.
- 2026-07-24: Drive-by ASCII fix: `DetermineStrikeOrderStage`'s Counter banner contained a literal
  em-dash (U+2014), which the ImGui atlas renders as `?`. Now `-`.

### Tier assignment

| Tier | Sites |
|---|---|
| **Headline** (4) | "Map Setup", "Deployment", "Round N", Victory/Tie |
| **Notice** (15) | Player's Activation; the four roll-off winners (objectives / terrain / deploy / map side); "N Objectives"; Counter strike-first; cast success; cast failure; morale Shaken; morale **Routed**; transport-wreck spillout; Ambush arrival; Aircraft fly-back |
| **Toast** (9) | mid-game embark; disembark; deploy-time embark; "held in Ambush"; accumulator borrow; caster self-boost; conduit relay; per-Caster assist/hinder; per-occupant spillout Shaken |

## Decisions

- **Tier is a property on `BannerBeat`, not new beat types.** One overlay, one sound-cue switch, one
  wire payload. Follows `ERollBeatCategory` on `DiceRolledBeat` (#245).
- **`Headline = 0`.** A beat that somehow arrives without a tier reads as the pre-#275 behavior rather
  than silently becoming a toast. Both ends of the wire are version-pinned by the #075 handshake, so
  this is belt-and-braces, not a compatibility requirement.
- **`Announce`'s default is Notice, not Headline.** The failure mode being fixed is that stopping the
  game was the cheap option. A new call site now has to *ask* to halt play. Notice sites therefore
  carry no explicit tier argument — `Headline` and `Toast` are the ones that are grep-able.
- **Lower tiers reuse the existing `Held` seam** rather than getting a new mechanism: they transfer to
  their own concurrent track in `PresentationPlayer` the frame they are dequeued, exactly as #232's
  overlapped casualties and #238's `AttackBeat` do. A Notice paces a 300ms lead-in (a hitch, so the
  player registers that something was said); a Toast paces nothing at all.
- **Held banners are excluded from `IsAnimating`.** That flag gates interactive prompts; a message that
  does not stop the engine must not stop the player from acting, or "non-blocking" is only half true.
- **A new Notice supersedes the previous one; toasts stack.** Two Notices share one band mid-screen and
  would overlap into mush — the newer statement is the one that matters. Stacking *is* the toast tier,
  bounded at 5 so a caster-heavy table can't paper over the board.
- **Notice got its own band (y=40%) rather than joining the toast ticker.** Owner call. Keeps
  meaningful-but-common lines (activation, cast results) in the player's line of sight; the trade is a
  third screen region to learn.
- **Rout stayed Notice, not Headline.** Owner call, taken as the stated default when the tier question
  came back unselected. A routed unit still gets 900ms of mid-screen text plus its whole-unit death
  animation. One-line change (`MoraleUtilities.RoutWithPresentation`) if it reads as too quiet in play.
- **`MoraleUtilities` still calls `Presenter.Present` directly** rather than `Announce`, so its two
  banners remain the only ones with no matching log line. Left alone deliberately — converting them
  would change logging behavior, which is not what this item is about.

## Outcome

_Open._ Deferred, explicitly: no NEW announce sites were added. The owner asked to see a candidate list
of currently log-only events (objective seizure/contest in `ReconcileObjectivesStage` is the leading
one) before any are added — that is a separate slice, not silently dropped scope.

## Hand-verify checklist (GUI)

1. Start a game: "Map Setup" and "Deployment" still land full-size and still stop the game.
2. Round change: "Round N" full-size, with the new heavier three-note sting.
3. Activation change: mid-size text at y=40%, and the game visibly keeps moving under it.
4. Embark or disembark a transport: a small pill in the top ticker, no pause at all.
5. Cast a spell with a nearby assisting Caster: assist/borrow/boost lines stack as toasts, and the
   cast roll's dice beat is no longer preceded by a queue of full-screen pauses.
6. Two toasts at once: they stack under the HUD without overlapping the dice panel or the round strip.
7. A Notice while a toast is up: no visual collision between the y=40% band and the ticker.
