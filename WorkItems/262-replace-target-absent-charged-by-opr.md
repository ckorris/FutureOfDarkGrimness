# 262 — Army Forge charges a "Replace X" whose X the unit doesn't have; we charge 0

**Status**: todo — needs a ruling on which behavior is right before anything is built
**Related**: #261 (found while reconciling the same list), #218/#219 (pricing reconciliation family)

## Goal

Decide, and then implement, what our Forge should do with a selected Replace upgrade whose target weapon
the unit does not carry. Done = our total reconciles with Army Forge's on the reported list, or we can say
precisely and deliberately why it does not.

## Evidence (2026-07-23)

After #261 the reported High Elf Fleets list (`share?id=UGDhJMMH0QBP`) reconciles to 2905 against Army
Forge's 2985. The entire residual 80 pts is four Retributors squads, each carrying a selected
"Replace one Shard Carbine -> Twin Shard Carbine (20 pts)":

- Retributors' base loadout is `Energy Sword` x5 plus a `Combat Shield` x5 item. There is no Shard Carbine.
- A Shard Carbine only exists if "Replace all Energy Swords and Combat Shields" (65 pts) is taken, which
  none of the four squads did.
- Army Forge charges the 20 pts anyway and counts it in `listPoints`.
- `ListCompiler` clamps the applications to the matched target count, so it applies nothing and charges
  nothing - our 105/130-pt squads against Army Forge's 125/150.

So on this list Army Forge is billing for a swap that cannot physically apply. The user noted the list is
old and "got auto-updated" on load, so these may be stale selections that survived a book revision.

## Fork (needs a ruling)

1. **Keep our behavior** (a replace with no target applies nothing and costs nothing) and surface the
   difference: the import reconciliation already has a warnings channel, so each unapplied selection could
   be listed explicitly ("Twin Shard Carbine: no Shard Carbine to replace - not applied, not charged").
   Truthful, keeps the compiler honest, and leaves a visible 80-pt gap against Army Forge.
2. **Charge for an inapplicable selection anyway**, matching Army Forge's number without applying the
   swap. Totals reconcile; the unit's cost then reflects gear it does not have, and a genuine data defect
   would be silently absorbed rather than reported.
3. **Treat it as a list error** - refuse the selection and warn loudly, closer to "your list is stale,
   re-pick it in Army Forge".

Recommendation: **1**. It is the only option that never states something false about the unit, and it turns
the discrepancy into a specific, actionable line rather than an unexplained total. But it means accepting
that imported totals can legitimately disagree with Army Forge, which is a product call.

Whichever wins, the import preview should name the affected selections instead of only showing a total.

## Outcome

_(open)_
