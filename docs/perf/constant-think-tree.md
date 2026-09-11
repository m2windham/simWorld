# What the constant think tree costs

Measured for `ai.interrupts` — the module that lets something interrupt a job
already in flight. Companion to [`baseline.md`](baseline.md), in its own file
rather than a section appended to it: several lanes run in parallel and
`baseline.md` is the file every one of them wants to append to.

Harness: `tools/bench/SimWorld.Bench`, suite `interrupts`
(`Suites/ConstantThinkTreeSuite.cs`).

```sh
dotnet run -c Release --project tools/bench/SimWorld.Bench -- \
  --suite interrupts --warmup 1 --runs 3 --constant-tree-ticks 3000
```

Same box and the same discipline as `baseline.md` §9-§10: 4 vCPU Xeon @ 2.10GHz,
Ubuntu 24.04, .NET SDK 8.0.424, Release build, fixed seed 12345, 1 warmup trial
discarded + 3 measured, median reported. Absolute numbers are not comparable
with `baseline.md` §1-§8 — see "The scenario, and why it is not baseline.md's"
below — but the A/B ratios within each table are, since every row in a table
was taken in the same process minutes apart.

## The question

`ThinkTreeDef` evaluation is the expensive part of this port's AI, and the
constant think tree asks for it on a schedule rather than only when a pawn runs
out of work. The brief that produced this module put the choice plainly: every
tick for every Full-tier pawn is the obvious answer, and also potentially the
most expensive thing in the tick loop. So: measure first, at
`TieringTuning.FullTierBudget` (500), which is the population the tiering work
already committed to keeping at Full.

## The scenario, and why it is not `baseline.md`'s

`ScalingSuite` (measurement 1) generates pawns and never spawns them. That is
right for measuring tracker cost and useless here: `JobGiver_AIFightEnemies`
returns on its first line for a pawn with no `Map`, so measured that way the
constant tree would cost nothing and the number would be a lie.

Everything below runs 500 generated colonists **spawned on a real 150x150 map**
(22,500 cells, ~45 per citizen), registered on the tick list, really pathing,
really running work-givers, really wandering. That costs far more per pawn-tick
than `baseline.md` §1's unspawned pawns — 1.66 ms/tick for 500 of them here
against the 0.12 ms/tick §1's figures imply — and it is the honest denominator
for "what fraction of a Full-tier settlement's tick does this add".

"Peacetime" means one faction and no hostiles anywhere: the state a settlement
spends nearly all of its life in, and the state in which the constant tree is
pure overhead that buys nothing.

## Measured, as first written

### One evaluation, in isolation

| case | ns/evaluation | ms/tick if every pawn evaluated every tick | ms/tick at 1-in-30 |
| --- | --- | --- | --- |
| peacetime (no hostile on the map) | 4,031.7 | 2.0159 | 0.0672 |
| under raid (20 hostiles) | 4,580.8 | 2.2904 | 0.0763 |

### The whole tick loop, A/B

500 pawns, 3,000 ticks per trial. "off" sets the bench's escape hatch
(`ConstantThinkTreeTuning.IntervalTicksOverride`) to 0, which is exactly the
behaviour before this module: a think tree consulted only when a pawn has
nothing to do.

| cadence | median ms / 3,000 ticks | ms/tick | vs. off |
| --- | --- | --- | --- |
| off (pre-`ai.interrupts`) | 4,978.0 | 1.6593 | - |
| every 30 ticks | 5,508.0 | 1.8360 | +10.6% |
| every tick | 18,194.4 | 6.0648 | +265.5% |

**Every tick is not affordable and is not what RimWorld does.** At the Full-tier
budget it costs more than two and a half times the entire pawn-tick budget it is
added to — a settlement that took 100 seconds of wall clock per in-game day
would take 364. The 1-in-30 cadence costs about a tenth of that budget.

## The cadence, and why it is a port rather than a translation

RimWorld gates its own constant-think-tree evaluation behind
`pawn.IsHashIntervalTick(30)` at the top of `Pawn_JobTracker.JobTrackerTick` —
this port does the same, in `ConstantThinkTreeTuning.IntervalTicks`. So the
measurement above did not have to choose between fidelity and cost: the 1:1
answer and the affordable answer are the same answer, and "every tick" was never
the 1:1 answer, only the intuitive one.

What 30 ticks buys, in the terms the brief asked for: an interrupt arrives
within half an in-game second of the threat appearing, staggered across pawns so
the cost spreads over ticks instead of landing on one. That is "now" for
everything this module is about — a raider crossing a room takes longer than
that — and it is emphatically not the same game as an interrupt that arrives 30
*seconds* late.

The other half of the module does not wait at all. Being *hit* runs through
`Pawn_JobTracker.Notify_DamageTaken`, which is called from the damage pipeline on
the tick the hit lands: a sleeping pawn wakes on the same tick it is shot, not up
to 30 ticks later. The cadence governs only how fast a pawn notices something it
has merely *seen*.

## What made one evaluation cost four microseconds

Two things, both of which the constant tree turned from cold paths into hot ones,
and both fixed here:

1. **`AttackTargetFinder.BestAttackTarget` tested the expensive predicate
   first.** It walked the map's pawn list asking `AttackTargetsUtility.HostileTo`
   (two grudge checks and a faction-relation lookup) of every candidate before
   asking whether the candidate was even in range (two int subtractions). With
   the constant tree calling it once per pawn per interval, that is O(population)
   per pawn and O(population squared) per interval across a settlement, with the
   costly half of each comparison paid first. Every one of those tests is a pure
   predicate with no side effect, so ordering them cheapest-first cannot change
   which pawn is returned — only what it costs to find out. Especially so for the
   common case: an unarmed citizen's acquire radius is
   `CombatAITuning.MeleeReachCells`, so essentially every candidate is rejected
   on distance alone.

2. **`JobGiver_AIFightEnemies` built a `Verb` before it had a target.** The
   original order was deliberate — "a pawn with nothing to attack with pays
   nothing for a scan whose answer it could not act on" — but the set it protects
   is empty: `AttackVerbUtility.NaturalWeaponFor` returns null only for a pawn
   that is neither Humanlike nor Animal, and no such pawn exists in shipped
   content. Every pawn has fists or teeth, and building them allocates a `Tool`,
   a `VerbProperties` and a `Verb` **per evaluation**. Scanning first means a
   settlement with no enemy in it allocates nothing here at all. Both orders
   produce the same answers: returning null for the first of two independent
   refusals or the second is the same null.

Neither is a behaviour change, and the full suite passes unchanged across both.

## Measured, after those two fixes

Same box, same session, same seed, same scenario, minutes later.

### One evaluation, in isolation

| case | ns/evaluation | before | change |
| --- | --- | --- | --- |
| peacetime (no hostile on the map) | 2,031.3 | 4,031.7 | **1.98x faster** |
| under raid (20 hostiles) | 2,009.8 | 4,580.8 | **2.28x faster** |

Note what the second row now says: with the ordering fixed, an evaluation costs
the same whether or not there is a raid on. Before, a raid made every citizen's
evaluation *more* expensive — 4,580 ns against 4,031 — because a hostile
candidate got further down the old predicate chain. Now the first test is
distance and the answer costs what it costs regardless of who is on the map.

### The whole tick loop, A/B

| cadence | median ms / 3,000 ticks | ms/tick | vs. off | was |
| --- | --- | --- | --- | --- |
| off (pre-`ai.interrupts`) | 4,921.7 | 1.6406 | - | 1.6593 |
| every 30 ticks (shipped) | 5,102.3 | 1.7008 | **+3.7%** | +10.6% |
| every tick | 10,994.1 | 3.6647 | +123.4% | +265.5% |

**The shipped cadence costs a Full-tier settlement about 4% of its tick.** That
is what the module is bought for: a sleeping citizen who wakes when shot, a
hauler who drops the rock when a raider walks in, and a think tree that is
finally consulted while a pawn is doing something rather than only when it has
run out of things to do.

Every tick is still more than twice the whole tick budget even after the fixes,
which is the same conclusion from a different distance: this belongs on an
interval, and 30 is the interval RimWorld chose.

## Two things worth knowing, recorded rather than fixed

**The hash stagger is coarser than the interval.** `Pawn.HashOffsetTicks()` is
`thingIDNumber * 3`, and `gcd(3, 30) = 3`, so pawns spread across only 10 of the
30 ticks in an interval rather than all 30 — three times the intended peak load
on those ticks, though exactly the intended *average*. The budget decision rests
on the average, and this headless core has no frame to miss, so it is recorded
here rather than fixed: `HashOffsetTicks` is shared with the health module's heal
and bleed intervals, and changing it would silently re-phase those too.

> **Since fixed.** `HashOffsetTicks` now hashes the id instead of scaling it, and
> the re-phasing turned out to break nothing — the suite passes unchanged. The
> busiest tick at interval 30 went from 50 pawns to 24 and the peak tick got ~30%
> cheaper; measured in [`hash-phasing.md`](hash-phasing.md).

**`BestAttackTarget` is still O(population) per call.** RimWorld avoids this with
a per-map `AttackTargetsCache` keyed by faction; this port has no such cache, so
finding the nearest enemy means walking the map's pawn list. Cheapest-first
ordering makes each step of that walk very cheap, but it is still a walk, and it
is the reason the per-evaluation cost is a function of settlement size rather
than a constant. A cache is the next real step if the constant tree ever needs to
run more often than it does.
