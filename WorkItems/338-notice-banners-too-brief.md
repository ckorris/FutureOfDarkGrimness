# 338 — Centered Notice banners were gone before they could be read

**Status**: implemented + tested; awaiting GUI hand-verify
**Related**: #275 (the banner tiers), #327 (the stacking this makes reachable), #337 (the Shaken
announcement that went unread because of this)

## Goal
Playtest note (2026-08-04, Chris): *"The tier-2 text (I think - appears in the center) is really really
quick. Could make it linger for longer, and also stack."*

The centered mid-screen band is `EBannerTier.Notice` — roll-off results, casts, failed morale tests, and the
Shaken-recovery announcement of #337.

## Diagnosis
`PresentationDurations.BannerNotice` was **900ms**. Against `PresentationPlayer`'s 120ms fade-in and 400ms
fade-out tail, that left under 400ms at full strength: long enough to register that something was said, too
short to read it.

The "and also stack" half was already built — #327 made every tier stack rather than replace, and
`BannerOverlay.DrawCenteredStack` anchors the oldest and piles newer ones above it, dimmed by depth. It was
simply **unreachable in play**: at 900ms a notice had retired before the next one arrived, so two were never
on screen together. Nothing to build there; the linger is what exposes it.

## Approach
`BannerNotice` 900ms -> **2400ms** (owner's pick from 1.8 / 2.4 / 3.5). Engine-side because pacing is a
domain concern (#275).

What is deliberately unchanged is `BannerNoticeLeadIn` (300ms). A Notice is a *held* beat: it transfers to
the front end's own display track the frame it is dequeued and paces only its lead-in, so the extra 1.5s is
spent entirely on screen while play carries on underneath. The game does not pause one millisecond longer.

Notice now outlasts Toast (2200ms), inverting the old order. That is intended, not incidental: a mid-screen
statement is the one being read head-on, so it should not flash away under a corner-of-the-eye ticker line.

## Notes

### 2026-08-04 — implemented
`BannerTierPlayerTests` gained two tests — a Notice is still at full alpha a second in and still frees the
active slot; two notices a second apart are on screen together (the stack the report asked for).
`Banners_RetireAfterTheirOwnLifetime` was repaired rather than re-pinned: it asserted the Notice was the
shorter of the two, which is now the Toast.

Engine 2817/2817, app 1071/1071, headless smoke exits 0.

Not changed: `MaxBanners(Notice)` stays at 3. The overlay already trims a band that runs out of vertical
room, dropping oldest-first, so a burst self-limits without a lower cap.

## Outcome
_(pending GUI hand-verify)_
