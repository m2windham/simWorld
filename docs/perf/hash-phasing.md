# What the hash-interval phasing costs on the worst tick

Measured for the fix to `Pawn.HashOffsetTicks()`, which decides *when* in a hash
interval each pawn does its periodic work. Companion to [`baseline.md`](baseline.md)
and [`constant-think-tree.md`](constant-think-tree.md), in its own file for the
same reason those are: several lanes run in parallel and `baseline.md` is the
file every one of them wants to append to.

Harness: `tools/bench/SimWorld.Bench`, suite `phasing`
(`Suites/HashPhasingSuite.cs`).

```sh
dotnet run -c Release --project tools/bench/SimWorld.Bench -- \
  --suite phasing --warmup 1 --runs 3 --phasing-cycles 10
```

Same box and the same discipline as `baseline.md` §9-§10 and
`constant-think-tree.md`: 4 vCPU Xeon @ 2.10GHz, Ubuntu 24.04, .NET SDK 8.0.424,
Release build, fixed seed 12345, 1 warmup trial discarded + 3 measured, median
reported. The scenario is `constant-think-tree.md`'s: 500 generated colonists —
`TieringTuning.FullTierBudget` — really spawned on a real 150x150 map, really
pathing and working, one faction and no hostiles.

## The defect

`Pawn.IsHashIntervalTick(interval)` is how every per-pawn periodic system avoids
doing all of its work on one tick: each pawn gets a fixed offset and fires when
`(TicksGame + offset) % interval == 0`. The offset was

```csharp
public int HashOffsetTicks() => thingIDNumber * 3;
```

Thing ids are handed out consecutively (`Thing.AllocateThingId` is `nextThingId++`),
so the offsets were an arithmetic progression of step 3. Against an interval
divisible by 3, `gcd(3, interval) == 3` collapses that progression onto a third
of the available phases — and **every hash interval this sim uses is divisible by
3**:

| interval | who uses it |
| --- | --- |
| 30 | the constant think tree / job interrupts |
| 60 | `HealthTuning.BleedInterval` |
| 150 | `Need.IntervalTicks`, `MentalState.CheckIntervalTicks` |
| 600 | `HealthTuning.HealInterval` |

It was [recorded rather than fixed](constant-think-tree.md#two-things-worth-knowing-recorded-rather-than-fixed)
when the constant think tree landed, on the grounds that the budget decision
rests on the average and this headless core has no frame to miss. That reasoning
still holds for the *average*. This is the other number.

RimWorld hashes the id (`Verse.Gen.HashOffset`) rather than scaling it. Its exact
constants could not be sourced, so the port uses this codebase's own
`Rand.HashInt` — MurmurHash's finaliser, a bijection on the id — masked to 31
bits:

```csharp
public int HashOffsetTicks() => Rand.HashInt(thingIDNumber) & int.MaxValue;
```

Masked rather than `Math.Abs`'d, because the hash really can return
`int.MinValue` and `Math.Abs` of that throws.

## Measured: pawns on the busiest phase

Counted, not timed. Offsets are a pure function of `thingIDNumber`, so these
numbers are exact and identical on any machine. "Phases used" is how many of the
interval's ticks any pawn ever fires on; an even spread would put
`500 / interval` pawns on each.

| interval | even share | phases used (before) | peak (before) | phases used (after) | peak (after) | peak reduction |
| --- | --- | --- | --- | --- | --- | --- |
| 30 (constant think tree) | 16.67 | 10 / 30 | 50 | 30 / 30 | 24 | **2.08x** |
| 60 (bleeding) | 8.33 | 20 / 60 | 25 | 60 / 60 | 15 | **1.67x** |
| 150 (needs, mental breaks) | 3.33 | 50 / 150 | 10 | 147 / 150 | 8 | 1.25x |
| 600 (healing) | 0.83 | 200 / 600 | 3 | 333 / 600 | 5 | **0.60x** |

Two things to read off this rather than one.

**The fix does what it was meant to where the load actually is.** At 30 ticks —
the interval every Full-tier pawn pays for the constant think tree, the most
expensive per-pawn periodic work in the sim — the busiest tick went from 50 pawns
to 24, against an even share of 16.67. The old offset was not merely uneven; it
was *exactly* 3x the even share on every phase it used, and empty on the other
two thirds.

**At 600 it is slightly worse, and that is not a rounding error.** With 500 pawns
and 600 phases there is less than one pawn per phase, so there is nothing to
de-cluster; an arithmetic progression of step 3 spreads 500 ids over 1,497 ticks
and wraps two and a half times, which is *flatter* than random. A hash produces
ordinary Poisson lumps, and the lumpiest phase holds 5 pawns instead of 3. Two
extra pawns healing on one tick is not a cost anybody will find, and the same
hash is what turns 50 into 24 at the interval that matters — but the honest
statement is that this fix trades a slightly worse peak at the sparse end for a
much better one at the dense end, not that it improves every case.

## Measured: per-tick wall clock

The same population really ticked, each tick timed on its own and folded into a
30-tick phase profile over 6,000 ticks (200 samples per bucket).

**Why the fold is 30 and not 600.** 30 is the gcd of every hash interval in play,
so every pawn's firing phase stays coherent under it — a pawn on the 150-tick
needs cadence fires at ticks congruent to its phase mod 150, and therefore mod 30
too. Everything else the tick loop does periodically (map sync at 250, weather at
500, the long-interval pass at 2,000) shares no factor with 30, so it smears
evenly across the buckets instead of planting a spike in one. Folding at 600 was
tried first and the profile was dominated by exactly that other work: a peak 25x
the mean in *both* arms, which measures the tick loop, not the phasing.

| offset | mean ms/tick | median bucket | peak ms/tick | peak / mean |
| --- | --- | --- | --- | --- |
| before (`thingIDNumber * 3`) | 1.2810 | 1.1827 | 1.8024 | 1.41 |
| after (hashed id) | 1.2564 | 1.2489 | 1.3848 | **1.10** |

Repeated at seed 777, same box, minutes later:

| offset | mean ms/tick | median bucket | peak ms/tick | peak / mean |
| --- | --- | --- | --- | --- |
| before (`thingIDNumber * 3`) | 1.3162 | 1.2181 | 1.8110 | 1.38 |
| after (hashed id) | 1.2720 | 1.2685 | 1.4012 | **1.10** |

**The worst tick got about 30% cheaper: 1.81 ms to 1.39 ms.** The mean did not
move (1.28 → 1.26 ms, inside the run-to-run spread), which is the control — phasing
moves work between ticks and never removes any, so a mean that *had* moved would
mean the measurement was wrong.

The clearest number in the table is the last column. Before, the costliest tick
in an interval cost 41% more than the average one; after, 10% more. The tick
profile is nearly flat.

## Why the wall-clock win is 1.30x and not the counted 2.08x

Because the hash-phased work is only part of a tick. Every Full-tier pawn also
does per-tick work that no offset moves — the pather, the job tracker, the verb
tick — and that cost is in every bucket. Subtracting the flat part makes the two
numbers agree: the excess of the peak bucket over the light buckets was
`1.80 - 1.18 = 0.62` ms before and `1.38 - 1.25 = 0.13` ms after. The spike
itself is what shrank; the floor it sits on did not.

This is also why the honest headline is "peak tick 1.30x cheaper" and not "3x
less peak load". The 3x was real and it was real about the *hash-interval work*;
that work is roughly a third of a Full-tier pawn's tick at this population, so
removing its clustering removes about a third of the spike, not the whole tick.

## What this does not measure

**Turrets and fires still cluster.** `CompTurretGun` (scan every 15 ticks) and
`Fire` (complex calcs every 150) both open-code the same `thingIDNumber * 3`
offset rather than going through `Pawn.HashOffsetTicks`, and both intervals are
divisible by 3, so both have the same 3x peak. They were left alone here: they
are other lanes' files, the populations involved are one or two orders of
magnitude smaller than 500 pawns, and a shared helper for all three is a better
fix than three copies of this one. Worth a follow-up, not worth a contested edit.

**Nothing about correctness.** Re-phasing changes *when* every hash-interval
system fires, not whether it fires or how often: over any window of `interval`
ticks each pawn fires exactly once, before and after. The full suite passes
unchanged, and `PawnHashIntervalTests` pins the property the bench measures —
full coverage of the modulus, a peak near the even share — as a property of the
distribution rather than as any of the literals above.
