# Phase 2: pressure, and how we measure it

Phase 1 ported RimWorld's systems and proved each one worked in isolation. Phase 2 adds
things that go wrong, and — the harder half — makes their cost measurable.

This document records decisions that were previously only in commit messages and pull
request descriptions. It is a record of what was decided and why, not a plan.

> ## ⚠ Status: the framing in "The idea the game rests on" is retired
>
> SimWorld is **one settlement**, decided in [`the-loop.md`](the-loop.md) after
> benchmarking Cities: Skylines, Dwarf Fortress and Nova Roma — all three of which are
> single-settlement. There are no unwatched settlements you own, so "looking is the scarce
> resource" and "a settlement that is well built must be genuinely fine unwatched" no
> longer describe the game. `the-loop.md` supersedes this section where they disagree.
>
> **Everything below that section survives intact**, and is more useful than it was: the
> incident effects, the ablation rule, the attribution ledger and the measurement
> discipline are how pressure gets applied and counted at *any* scale. Only the
> justification changed, not the machinery.

## The idea the game rests on

> **Retired — see the status block above.** Left as written because the ablation rule and
> the attribution discipline in the rest of this document were derived here.

A god can look at one place at a time. Looking is the scarce resource.

That only works if looking away costs something. A threat that only exists on a map the
player has open is free to ignore, so it creates no pressure and no decision. Every defect
in phase 2 is judged first on one question: **does it still cost you something when you are
not watching?**

The second rule matters as much and is easier to get wrong:

> A settlement that is well built must be genuinely fine unwatched.

If watching is always better, the best strategy is to watch everything. Attention stops
being scarce, and the player becomes a maintenance worker checking on each settlement in
turn. The intended shape is that **infrastructure buys the right to not look** — you spend
capability to reduce how much attention a place demands, which frees your attention for
somewhere that needs it.

## What shipped

| Defect | Costs you when unwatched? | How its cost is measured |
| --- | --- | --- |
| Manhunter pack | Yes — resolved abstractly against the settlement | Deaths credited to the incident that spawned the killer |
| Drought | Yes — acts on the food ledger, no map needed | Nutrition denied, summed where the multiplier is applied |
| Disease | Being fixed — see "Known problems" | Deaths credited to the incident that caused the hediff |
| Severe weather (Flashstorm) | No — watched maps only | Not yet measured |

Each defect can be switched off by name (`--without <DefName>`) so its contribution can be
isolated. The rule for doing that correctly: **ablate the effect, never the selection.** An
ablated incident is still chosen, still fires, still spends its refire timer, and still
consumes exactly the random draws it would have. Only its observable effect is skipped.
Otherwise the two runs diverge from the moment of the draw and nothing can be compared.

## Two rules about measurement

Both were learned by getting them wrong first.

### Measure a defect where it acts, not at its outcome

The first attempt at measuring the manhunter pack ran the same seed twice — once with the
pack switched off — and subtracted the death totals. Across three seeds and two attention
settings that subtraction returned `0, +1, -2, 0, 0, +1`. It changes sign, and the `-2`
would mean a manhunter pack saved two lives.

The method was wrong, not the numbers noisy. Switching off an incident holds every random
draw identical but cannot hold the *world* identical: from the moment a pack spawns, the two
runs take different jobs, eat different meals and injure different people. Two weeks later
the difference in total deaths is mostly that divergence. The noise was larger than the
effect.

What replaced it: record the cost at the point the defect acts.

- A killer remembers which incident put it in the world; a death is credited to it.
- A drought's cost is the difference between the food that was produced and the food that
  would have been produced without it, added up as it happens.

Both are exact from a single run with no comparison run at all — which also means the
numbers exist in a real game, not only in the test bench.

Different defects need different instruments. A threat with a killer is measured in deaths.
An economic defect is measured in the resource it destroyed. Forcing everything into "count
the bodies" produces a number that is either wrong or meaningless.

### An instrument may under-claim, never over-claim

This has now been violated three times, in three different systems, by three different
authors, in the same shape every time: a total is computed correctly, then handed out
without checking that the parts add up to the whole.

1. Deaths from an abstract raid were credited from the raid's own body count, which reported
   **four kills against a total of zero deaths** — the ledger counts only the player's own
   people, and that settlement belonged to no registered civilization.
2. A drought's lost food was credited in full to *every* active condition, so two conditions
   running at once reported twice the food that was actually lost.

Both were invisible in every situation the code shipped with, and would only have become
wrong later, when someone added a second source. Both were caught by asking the same
question: *if two of these are active, do the parts still sum to the whole?*

When a total has to be split between several causes, the split must be principled. Growth
factors multiply, so a shortfall is divided by log-share rather than evenly — with factors
of 0.9 and 0.1, that attributes 4% and 96%, where an even split would claim half each and
describe neither.

## What survival numbers cannot tell you

The test bench reports population, food, mood and deaths. None of those is the point. Tuned
against them alone, the best possible settlement is one that never starves, never loses
anybody and never has a bad day — which is a spreadsheet, not a game.

So the bench also reports how many moments the chronicle's own curator thought were worth
remembering, how many days had anything happen, and how far mood and food swung. A flat run
is the failure state, not the goal.

These are not scores to maximise either. They exist so that "nothing went wrong" stops
reading as success.

## Known problems

**Disease is inverted.** Hediffs only advance for citizens at Full detail. An unwatched
citizen who catches plague is frozen — they cannot die of it and cannot recover from it, and
no letter is sent, so the player is never told. The effect is that *looking* at a settlement
is what allows its people to die of disease, and looking away protects them. This is
backwards and is being fixed.

Worth noting how it happened: the behaviour was written down. The code says "no bulk hediff
physics at this tier — hediffs already present stay exactly as they were" and calls it an
honest limitation, which it was, in its own scope. Nobody connected that recorded performance
limitation to its consequence one level up. Most defects found in this phase have this shape
— a comment stating an assumption that was true where it was written.

**Three defects are blocked on the same thing.** A settlement carries a name, a founding
date, a list of citizens and a store of goods. It has no buildings. So an earthquake, a
flood or a storm has nothing to damage unless someone is looking at that settlement, which
makes all three free to ignore. Giving settlements abstract infrastructure unblocks all
three at once, and is the natural partner to the disease work above: medical capacity is the
first thing that should let a settlement cope on its own.

**Nothing is tuned.** Every severity and duration is an unsourced placeholder. Tests
deliberately pin behaviour — that an unwatched settlement's larder fills more slowly, that
an untended plague kills — and never the literal numbers, so tuning later does not break
them.

## Still open

- What the player is rewarded for, beyond surviving. The loop assumes a run ends and another
  begins, with the player carrying knowledge forward, but nothing yet measures or rewards
  that progression.
- Whether attention should be spendable in a finer way than "which settlement am I looking
  at" — a budget rather than a spotlight.
- Whether a defect should ever be unrecoverable, or whether every one should have an answer
  the player could have prepared for.
