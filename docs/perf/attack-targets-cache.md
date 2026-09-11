# What finding an enemy costs

Measured for `ai.attack-targets` — the per-map index that answers "who on this
map is hostile to me?" without walking the population. Companion to
[`constant-think-tree.md`](constant-think-tree.md), whose closing paragraph named
this as the next real step, and in its own file for the same reason that one is:
several lanes run in parallel and `baseline.md` is the file every one of them
wants to append to.

Harness: `tools/bench/SimWorld.Bench`, suite `targets`
(`Suites/AttackTargetScanSuite.cs`).

```sh
dotnet run -c Release --project tools/bench/SimWorld.Bench -- \
  --suite targets --warmup 1 --runs 3 --constant-tree-ticks 3000
```

Same box and the same discipline as `baseline.md` §9-§10: 4 vCPU Xeon @ 2.10GHz,
Ubuntu 24.04, .NET SDK 8.0.424, Release build, fixed seed 12345, 1 warmup trial
discarded + 3 measured, median reported.

**Every before/after pair below was taken in one process, minutes apart.**
`AttackTargetFinder.BypassCache` restores the pre-cache behaviour exactly — it
routes `BestAttackTarget` through `BestAttackTargetUncached`, which is the same
scoring loop over the whole map — so "before" and "after" are the same build, the
same box and the same generated scenario. That is the escape-hatch pattern
`PathFinder.DisableRegionCorridor` set for the pathing A/B, and it is the only
way a ratio between two numbers means anything.

## The question

`AttackTargetFinder.BestAttackTarget` walked every pawn on the map. The constant
think tree calls it once per Full-tier pawn per
`ConstantThinkTreeTuning.IntervalTicks`, so that is O(population) per call and
O(population squared) per interval across a settlement, against a tick budget
that grows roughly linearly. `constant-think-tree.md` measured the term at one
population — the Full-tier budget of 500 — and a single point has no shape. The
question here is the shape.

## The scenario

Pawns **spawned on a real map**, because `JobGiver_AIFightEnemies` returns on its
first line for a pawn with no `Map` and a suite that forgets to spawn measures a
lie (`ScalingSuite` does exactly that, correctly, for a different question).

The map side grows as `sqrt(N)` so cells-per-pawn stays at 45 — the density
`constant-think-tree.md`'s 150x150 map has at N=500, which makes the N=500 row
below directly comparable with that file. Holding the map fixed while N grew
would have measured crowding: a scan's cost turns on how many candidates fail the
range test, and packing a town tighter changes that answer for reasons that have
nothing to do with population.

"Peacetime" is one faction and no hostiles — the state a settlement spends nearly
all its life in. "Under raid" adds a second, hostile faction of 20.

## Measured: the scan, in isolation

| N | case | before ns/call | after ns/call | speedup | before ns/call/N | after ns/call/N |
| --- | --- | --- | --- | --- | --- | --- |
| 100 | peacetime | 314.9 | 9.4 | 33.6x | 3.149 | 0.094 |
| 100 | under raid | 366.8 | 70.9 | 5.2x | 3.668 | 0.709 |
| 250 | peacetime | 698.6 | 10.5 | 66.4x | 2.794 | 0.042 |
| 250 | under raid | 774.1 | 73.0 | 10.6x | 3.096 | 0.292 |
| 500 | peacetime | 1,513.2 | 10.0 | 150.9x | 3.026 | 0.020 |
| 500 | under raid | 1,519.9 | 71.5 | 21.3x | 3.040 | 0.143 |
| 1,000 | peacetime | 2,860.0 | 10.3 | 278.6x | 2.860 | 0.010 |
| 1,000 | under raid | 3,032.0 | 72.3 | 42.0x | 3.032 | 0.072 |
| 2,000 | peacetime | 7,846.7 | 10.9 | 717.1x | 3.923 | 0.005 |
| 2,000 | under raid | 7,888.1 | 76.3 | 103.4x | 3.944 | 0.038 |

**The `before ns/call/N` column is the whole finding.** It is flat at about 3 ns
across a twenty-fold range of N, which is what "linear in population" looks like
when you divide it out. The `after` column of the same quantity falls by the same
factor N rises, because the cached scan has stopped reading population at all:
roughly **10 ns in peacetime and 72 ns under raid, whatever the settlement's
size**. Under raid it is a function of the raid — 20 hostiles, weighed one at a
time — and that is the number that should scale with the fight, not with the
town.

The speedup column is therefore not a constant and should not be quoted as one:
it is N / (raid size or zero), and it grows without limit as a settlement does.
At the Full-tier budget it is 151x; at 2,000 it is 717x.

## Measured: one constant-think-tree evaluation

The same sweep through the tree that actually issues the call, so these rows line
up with `constant-think-tree.md`'s. `ms/tick` is the arithmetic consequence at
the shipped cadence — every pawn evaluates once per 30 ticks.

| N | case | before ns | after ns | before ms/tick | after ms/tick |
| --- | --- | --- | --- | --- | --- |
| 100 | peacetime | 346.8 | 64.2 | 0.0012 | 0.0002 |
| 100 | under raid | 431.5 | 137.1 | 0.0014 | 0.0005 |
| 250 | peacetime | 709.7 | 64.9 | 0.0059 | 0.0005 |
| 250 | under raid | 853.7 | 144.6 | 0.0071 | 0.0012 |
| 500 | peacetime | 1,471.2 | 73.8 | 0.0245 | 0.0012 |
| 500 | under raid | 1,445.8 | 150.9 | 0.0241 | 0.0025 |
| 1,000 | peacetime | 2,896.2 | 110.1 | 0.0965 | 0.0037 |
| 1,000 | under raid | 2,891.0 | 158.0 | 0.0964 | 0.0053 |
| 2,000 | peacetime | 8,150.2 | 204.0 | 0.5433 | 0.0136 |
| 2,000 | under raid | 7,864.0 | 282.9 | 0.5243 | 0.0189 |

Two things worth reading off it. First, **the scan was essentially the whole
evaluation**: at N=500 the tree cost 1,471 ns and the scan alone cost 1,513 ns in
the table above, the two being the same measurement to within noise. Everything
else the constant tree does — the interruptibility gate, `CanFight`, the posture
lookup — is the 74 ns that is left.

Second, the `ms/tick` columns are where the quadratic lived and where it stopped:
before, doubling the population roughly quadrupled it (0.0245 -> 0.0965 ->
0.5433); after, it grows about linearly and stays under a fiftieth of a
millisecond at twice the Full-tier budget.

## Measured: the whole tick loop

3,000 ticks per trial, peacetime, every other system running.

| N | ms/tick no constant tree | ms/tick before | ms/tick after | before vs. off | after vs. off |
| --- | --- | --- | --- | --- | --- |
| 250 | 0.4022 | 0.4073 | 0.3966 | +1.3% | -1.4% |
| 500 | 1.2127 | 1.2831 | 1.2430 | +5.8% | +2.5% |
| 1,000 | 3.9183 | 4.4183 | 4.0679 | +12.8% | +3.8% |

This is the end-to-end check on the arithmetic, and it is the noisiest thing in
this file — three separate runs of the `off` configuration on this box came back
at 1.2164, 1.2687 and 1.2127 ms/tick, a spread of about 4%, so the N=250 row's
-1.4% is zero and the N=500 row's +2.5% is barely distinguishable from it. The
N=1,000 row is the one comfortably outside the noise: **+12.8% of the tick budget
before, +3.8% after**, which is the same statement the arithmetic makes, made
badly.

**A gap worth recording rather than explaining away.** The measured cost of the
constant tree is consistently three to five times what the isolated
per-evaluation number predicts (at N=1,000: 0.500 ms/tick measured against 0.0965
predicted). `constant-think-tree.md` saw the same gap at the same ratio and put
it down to "the interrupts that actually fire, the jobs they end, the re-paths
that follow" — but these rows are peacetime, no hostile exists, the tree issues
no job and nothing is interrupted, so that explanation cannot be the whole of it.
The likeliest remaining cause is memory locality: the isolated loop calls the
same tree over the same pawns back to back with everything hot, while in a real
tick each evaluation is one cold visit interleaved with every other tracker. That
is a hypothesis, not a measurement, and it is written down here as one. It does
not change any conclusion below — it makes the cache look *better* end-to-end
than the arithmetic says, not worse.

## Was the cache worth it? An honest answer

**At the Full-tier budget of 500, on its own, no — and that was worth measuring
before building anything.** The scan cost 0.0245 ms/tick out of a 1.21 ms/tick
pawn-tick budget: 2% of the tick, nobody's bottleneck. A lane that stopped here
and reported "not yet, and here is the number" would have been reporting the
truth.

**Across the range a settlement actually moves through, yes.** Three reasons, in
the order they matter:

1. **The term was quadratic against a linear budget.** 2% at 500, 2.5% at 1,000,
   4.3% at 2,000 by the same arithmetic — and those are the *peacetime* numbers,
   the ones paid by a settlement with no enemy anywhere. A cost that is small
   today and grows faster than the thing it is a fraction of is the one worth
   removing while it is still cheap to remove.
2. **The invalidation surface turned out to be almost free.** The hooks this
   needed already existed: `Thing.SpawnSetup`/`DeSpawn` already call
   `MapPawns.RegisterPawn`/`DeRegisterPawn` for every pawn, death already routes
   through a despawn (`CorpseMaker`), tier demotion already routes through the
   same despawn, and faction *relations* need no hook at all because the index
   files by membership and asks about hostility at scan time. The whole of the
   new invalidation is two property setters — `Pawn.faction` and
   `Pawn_MindState.angryAt` — and a register/deregister pair. A cache whose
   staleness had to be tracked by hand would have been a bad trade at 2%.
3. **It removes a ceiling rather than a cost.** The 1-in-30 cadence was chosen
   because it is what RimWorld does, but `constant-think-tree.md` also measured
   every-tick evaluation at +123% of the entire tick budget and ruled it out.
   Roughly 95% of that was the scan. It is still not a good idea, but it is no
   longer the scan's fault.

## What the index is, in one paragraph

Spawned pawns filed by faction, plus the (almost always empty) list of pawns
holding a grudge. A searcher visits only the buckets whose faction is hostile to
its own — in peacetime, none — plus the grudge list, plus its own grudge target.
Crucially the index is a **superset** of the candidate set and never an answer:
`AttackTargetFinder` applies every predicate it always did to each candidate the
index hands it, so a stale entry costs nanoseconds and cannot change behaviour.
The only failure mode is *omission*, which is what every invalidation path exists
to prevent, and what `AttackTargetsCacheTests` pins by asserting the cached scan
and a walk of the whole map return the identical pawn across 40 randomly
generated map states.

## Still O(population), elsewhere

`Building.CompTurretGun.FindTarget` walks `map.mapPawns.AllPawnsSpawned` for
every turret every `ScanIntervalTicks`. It is the same shape of loop for the same
reason and it was left alone deliberately: its hostility rule is not
`AttackTargetsUtility.HostileTo` (a turret with no faction treats *every*
factioned pawn as hostile, and a prisoner of the turret's own faction is exempt),
so it cannot read the faction buckets without changing what it targets. Turrets
are also rare where citizens are many, so the product is small. It is the honest
next candidate if turret counts ever rise.
