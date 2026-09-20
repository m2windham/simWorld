# The loop

What SimWorld is, mechanically. Decided after benchmarking the three games it is
meant to sit beside. **This document supersedes `goal-renewal.md` entirely and
corrects `phase-2-pressure.md` where they disagree.**

Short on purpose. The last attempt at this question produced a long document
arguing for an abstraction that measurement then killed; the useful version is
the one that says what the game is and stops.

## 1. What the benchmark found

Cities: Skylines, Dwarf Fortress and Nova Roma — the three named as the target —
are **all single-settlement**. Not by accident and not as a limitation:

| | Settlements at once | Where the next problem comes from |
| :--- | :--- | :--- |
| Cities: Skylines | One | Your own growth jamming your own roads. Disasters are an optional DLC bolt-on, not core pressure. |
| Dwarf Fortress | One (retire before starting another) | Fortress complexity, plus periodic sieges |
| Nova Roma | One | Growth scarcity, plus god demands that escalate with population |
| *RimWorld (contrast)* | *Up to 5* | *A storyteller that scales threats to your wealth and colonist count* |

Multi-settlement play is the **exception** in this genre, not the norm.

## 2. The two decisions

**SimWorld is one settlement.** The settlement is the game. Other civilizations
exist — you trade with them, go to war with them, watch them rise — but **you
never run them.** They are the world your settlement sits in.

**Pressure comes from a storyteller scaled to your own growth.** RimWorld's
model, and the one thing that genre does which the other three do not: growth and
external event are the *same mechanism*. The bigger and richer you get, the more
the world brings. We already ported this —
`StorytellerUtility.DefaultThreatPointsNow` reads wealth and living colonists,
applies difficulty, adaptation and era factors, and clamps. **The engine exists
and is tested.** What remains is aiming it at one settlement and tuning it.

## 3. What this retires

Three things, all of which were mine rather than the vision's, and all of which
made the design harder to reason about:

- **"Attention is the scarce resource."** The idea that looking away must cost
  something. With one settlement there is nothing to look away from.
- **"A settlement that is well built must be genuinely fine unwatched."**
  (`phase-2-pressure.md`.) There are no unwatched settlements you own. The
  measured finding that a founding band dies within five years unwatched was
  measuring a requirement that no longer exists — and in RimWorld, the one
  comparable game, an unvisited colony genuinely can be lost. **Not a bug.**
- **Goal renewal, tensions and prospects** (`goal-renewal.md`). An abstraction
  layer none of the benchmark games have. The storyteller is the renewal
  mechanism. `MomentCurator` goes back to being exactly what it is — the
  chronicle's curator, pointed backwards, which is what it is good at.

## 4. What this keeps, unchanged

Everything the player can actually do. The eight lanes shipped a sandbox with
knobs on it and none of it depended on the frame that just went:

- **Settlement scale** — directed orders, bills, stockpile filters, growing
  zones, blueprints, the home area. This is now *the whole game*, not one of two
  scales.
- **Seeing** — the drill-down from rollup to one named citizen and their history.
  More important under this decision, not less: with one settlement, the people
  in it are the whole cast.
- **The civilization layer** — edicts, research, roles, diplomacy, trade. These
  stay, reframed as **how your settlement relates to a world it does not run**.
  A war is something that arrives at your gates, not a map you manage.

## 5. What to build next

In order, and deliberately short:

1. **Point the storyteller at the settlement and tune it.** The threat curve
   exists; whether it is aimed and scaled correctly for a founding band of 20–40
   is now the first question, because that is the opening hour of the game.
2. **Make growth bind.** Cities: Skylines' pressure is a bigger city straining
   its own systems. Ours should strain food, hauling, housing and mood as it
   grows. Much of this exists; whether it *binds* — whether scale actually
   creates difficulty — is measurable and unmeasured.
3. **Then look at whether the loop is fun**, with the bench's own instruments:
   curated moments, days with anything happening, how far mood and food swing.
   Flat is the failure state.

## 6. The rule that survives all of this

From `player-first.md`, unchanged and still the thing every decision is held to:

> A player input is legitimate if the simulation can carry it to its consequence
> through its own machinery. To give the player a new kind of influence, you
> build the causal path — you never expose a setter.

And the one that should have caught the detour earlier: **a design claim is an
unproven claim.** This document is short because the previous one was long and
wrong, and the difference was not effort.
