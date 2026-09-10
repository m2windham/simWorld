# Performance baseline

Measured against `tools/bench/SimWorld.Bench` on 2026-09-07, commit `57243b2`
(branch `claude/rimworld-mechanics-spec-rppt8h`).

**Sections 1-6 are the baseline as measured, before any change.** Section 7
records the one fix made in response and what it actually bought, re-measured on
the same box in the same session. The §4 allocation figures are therefore
historical: they describe the code as it was, not as it is.

This is **not** a benchmarking-lab result. The box is a 4-core KVM guest
(shared/virtualized, not bare metal — see below); some runs show visible
run-to-run noise (§1). Numbers are directionally solid and the *relative*
comparisons (multipliers, % splits) are the load-bearing part of this report;
treat single-digit-percent differences between runs as noise, not signal.

## Hardware / runtime

```text
dotnet --info: SDK 8.0.424, Host 8.0.30 (linux-x64), Microsoft.NETCore.App 8.0.30
OS: Ubuntu 24.04, x86_64
CPU: Intel(R) Xeon(R) Processor @ 2.10GHz, 4 vCPU, 1 thread/core, KVM hypervisor (virtualized)
RAM: 15 GiB
Build: dotnet build -c Release (TreatWarningsAsErrors, netstandard2.1 core / net8.0 bench)
```

All runs used `dotnet run -c Release`. Each configuration: 1 warmup run
(discarded) + 3 measured runs, median reported, unless noted otherwise. Fixed
seed 12345 throughout (`Rand.Current = new RandomStream(12345)`), pawn kind
`Colonist`, fixed biological age 30. A 120s-per-trial guard stops a sweep from
climbing further once a single trial exceeds it (noted "(guard)" in tables —
those cells are one raw trial, not a median of three).

The machine was otherwise idle during every *timed* region — the only
concurrent activity was this agent's own low-frequency polling (`sleep 10`
loops checking whether the benchmark process had exited), which does not
compete for CPU. No other benchmark ran concurrently with a timed run.

## 1. Pawn tick scaling

N generated colonists ticked through one in-game day (60,000 ticks).

| N | median ms/day | µs/pawn-day | s/game-day (flat out) | in-game days/real-s (flat out) |
| --- | --- | --- | --- | --- |
| 100 | 1,372.2 | 13,722.0 | 1.37 | 0.729 |
| 250 | 3,499.9 | 14,000.0 | 3.50 | 0.286 |
| 500 | 7,050.4 | 14,100.7 | 7.05 | 0.142 |
| 1,000 | 17,385.3 | 17,385.3 | 17.39 | 0.058 |
| 2,500 | 54,242.5 | 21,697.0 | 54.24 | 0.018 |
| 5,000 | 90,498.1 | 18,099.6 | 90.50 | 0.011 |
| 10,000 | 203,696.6 (guard, 1 trial) | 20,369.7 | 203.70 | 0.005 |

Per-pawn-day cost is **not** flat: 14.0–14.1 **ms**/pawn-day up to N=500, jumping
to 17.4 at N=1,000 and 21.7 at N=2,500, dipping back to 18.1 at N=5,000, and 20.4
at N=10,000 (single trial, more noise). (An earlier revision of this paragraph
wrote those as microseconds — off by a thousand against the table directly above
it, which has been right all along: 7,050.4 ms over 500 pawns is 14.1 ms each.
The conclusions never depended on it, since the population ceilings below are
measured breakpoints rather than derived from this figure, but the number was
quoted onward and is corrected here.) This is superlinear-with-jitter,
not clean O(N) — consistent with GC pressure (§4: ~4.2 MB/pawn-day means a
Gen0 collection roughly every 30 pawn-days at N=1,000) and cache effects as
the working set grows past what fits in the 8 MiB L2 / shared L3.

**Where real-time parity breaks** (1 game day = 60,000 ticks; at 1x that's
1,000 real seconds, at 15x it's 66.7):

- **1x** (1,000 s/day budget): not reached anywhere in the tested range — even
  the guard-capped N=10,000 trial took only 203.7s, a fifth of the budget.
  PROJECTION (not measured): linearly extrapolating the N=5,000 per-pawn-day
  cost puts the break around **N≈55,000**. Labeled a projection because nothing
  in the tested range approaches it, and §1's own data shows scaling isn't
  reliably linear.
- **15x** (66.7 s/day budget): **measured, not projected.** Holds through
  N=2,500 (54.2s/day) and breaks by N=5,000 (90.5s/day) — the ceiling for
  turbo-speed play sits **between 2,500 and 5,000 healthy colonists**.

## 2. Cost attribution (N=1,000, healthy pawns)

Each tracker's own `Tick`/interval method timed directly over a fresh
1,000-pawn set for one in-game day (median of 3). `Pawn.Tick()`'s own overhead
(dead/suspended checks, `base.Tick()`, tick-list dispatch) is the ~2.7%
difference between this sum and measurement 1's N=1,000 total.

| tracker | median ms/day | % of attributed total |
| --- | --- | --- |
| `health.HealthTick` | 12,755.7 | **75.4%** |
| `needs.NeedsTrackerTick` | 2,112.3 | 12.5% |
| `mindState.MindStateTick` | 818.4 | 4.8% |
| `skills.SkillsTick` | 834.1 | 4.9% |
| `ageTracker.AgeTick` | 399.5 | 2.4% |
| **sum** | 16,919.9 | 100% |

Health dominates by a wide margin. §4 pinpoints why: `HealthTick` ends every
tick with an *uncached* capacity check (`ShouldBeDead`), unlike the other four
trackers whose per-tick cost is either O(1) or gated behind a hash-interval
check.

## 3. Hediff load (N=1,000, 1 day)

| variant | median ms/day | multiplier vs. no hediffs |
| --- | --- | --- |
| no hediffs | 17,105.1 | 1.00x |
| 3 injuries (untended cuts, torso/arm/leg) | 424,013.2 (guard, 1 trial) | **24.79x** |
| 3 injuries + Flu | did not finish | see below |

"3 injuries" already tripped the 120s guard on its very first trial (a single
in-game day took 7 min, not the usual ~17s), so that number is one raw trial,
not a median.

**"3 injuries + Flu" at N=1,000 did not complete a single in-game day within
15 minutes of wall clock and was killed.** That is the honest result at the
scale this report otherwise uses. To get an actual (not projected) number, the
same three variants were re-run at N=5 with no warmup (single sample — noisier
in absolute terms because it isn't JIT-warmed, but the *relative* multipliers
are informative):

| variant (N=5, unwarmed, 1 sample) | ms/day | ms/pawn-day | multiplier vs. no hediffs |
| --- | --- | --- | --- |
| no hediffs | 706.5 | 141.3 | 1.00x |
| 3 injuries | 1,269.9 | 254.0 | 1.80x |
| 3 injuries + Flu | 13,313.7 | 2,662.7 | **18.84x** (10.5x on top of injuries alone) |

PROJECTION from that N=5 sample, assuming linear-in-N scaling (which §1 shows
is optimistic, so treat this as a floor, not a ceiling): a full day for 1,000
pawns each carrying 3 injuries + Flu would take on the order of **2,600–3,000
seconds (~45–50 minutes)**. This is a projection, not a measurement.

**Why:** see finding #2 in "Where the ceiling is" below — every hediff whose
comp nudges `Severity` invalidates the pawn's entire capacity cache, and
`HediffComp_Immunizable` (used by Flu, Plague, and WoundInfection) does that
*every single tick*, versus roughly 1,700 times/day for bleeding+healing on
plain untended injuries.

## 4. Allocation pressure (N=1,000, 1 day, healthy pawns) — before the fix

| metric | value |
| --- | --- |
| wall clock | 17,508.4 ms |
| total managed allocation | 4,437,503,664 bytes (**4,231.9 MB**) |
| allocation per pawn-day | 4,437,504 bytes (~4.23 MB) |
| Gen0 collections | 33 |
| Gen1 collections | 4 |
| Gen2 collections | 1 |

Two call sites were flagged by inspection as unconditional per-tick/per-interval
allocations, then measured directly (200,000 calls each, isolated, on a
zero-hediff pawn):

| call site | bytes/call | frequency | implied bytes/day at N=1,000 | share of measured total |
| --- | --- | --- | --- | --- |
| `PawnCapacityUtility.CalculatePartEfficiency(hediffSet, corePart)`, called from `Pawn_HealthTracker.ShouldBeDead()` | 64.0 | every tick, every pawn, unconditionally (60,000×/pawn/day) | ~3,840,000,000 (3,662 MB) | **~86.5%** |
| `HediffSet.GetHediffs<Hediff_Injury>()`, called (×2) from `Pawn_HealthTracker.TryNaturalHealing()` | 48.0 | every `HealInterval` (600 ticks) regardless of injuries (200×/pawn/day) | ~9,600,000 (9.2 MB) | ~0.2% |

The first is the dominant allocator in the whole simulation core by a wide
margin. See finding #2 below for the mechanism and file/line references.

## 5. World generation

| subdivision | tiles | median ms |
| --- | --- | --- |
| 3 | 642 | 10.2 |
| 4 | 2,562 | 24.3 |
| 5 | 10,242 | 118.9 |
| 6 | 40,962 | 250.0 |

Not a bottleneck at any tested scale — subdivision 6 (the spec's ~41k-tile
100%-coverage target) generates in a quarter of a second.

## 6. Save / load (N=1,000 pawns)

| metric | value |
| --- | --- |
| save (`Scribe.SaveToString`) | 169.3 ms |
| load (`Scribe.Load`) | 302.7 ms |
| serialized size | 6,395,518 bytes (6.10 MB) |
| bytes/pawn | 6,396 |

Not a bottleneck at this scale.

## 7. The fix, and what it bought

Finding #2 below was verified independently against the source before acting:
`Pawn_HealthTracker.HealthTick` ends with an unconditional
`CheckForStateChange(null, null)`, which calls `ShouldBeDead()`, which calls
`PawnCapacityUtility.CalculatePartEfficiency(hediffSet, corePart)`. That walked
its part's hediffs through `HediffSet.GetHediffsOnPart`, a `yield return`
iterator that heap-allocates an enumerator on every call whether or not the part
carries any hediff at all.

The fix is the smallest one that exists: iterate the hediff list directly at
that call site, and at the two `GetHediffs<T>()` calls in `TryNaturalHealing`
that run every heal interval regardless of whether the pawn is injured. Same
traversal, same order, same results — no enumerator. No behaviour changed, and
the full suite (519 tests) passes unchanged.

Re-measured, same box, same session, same seed, N=1,000, 1 in-game day:

| metric | before | after | change |
| --- | --- | --- | --- |
| total managed allocation | 4,231.93 MB | 137.53 MB | **30.8x less** |
| allocation per pawn-day | 4,437,501 B | 144,210 B | 96.7% removed |
| Gen0 collections | 33 | 1 | -32 |
| Gen1 collections | 3 | 0 | -3 |
| Gen2 collections | 1 | 0 | -1 |
| `CalculatePartEfficiency` | 64.0 B/call | 0.0 B/call | eliminated |
| wall clock | 17,508.4 ms | 15,787.0 ms | ~10% faster |

The micro-measurement of `GetHediffs<T>()` still reports 48 B/call, and that is
correct: the helper itself is unchanged and still allocates for anyone who calls
it. What changed is that the hot path no longer does.

**The instructive part is the gap between those last two rows.** Allocation fell
by a factor of 31 and wall clock by only a tenth, so garbage collection was
never the main cost — the per-tick CPU work was. That reframes the ceiling: it
is not GC pressure, it is that `ShouldBeDead` recomputes core-part efficiency,
required-capacity checks and total injury severity from scratch every tick for
every pawn, whether or not anything about that pawn changed.

### The next step, and why it was deferred

The natural follow-up is to cache core-part efficiency and total injury severity
on `HediffSet` behind the existing `DirtyCache()` flag, exactly as `cachedPain`
and `cachedBleedRate` already are. That would make the healthy-pawn per-tick
path O(1) instead of several list walks. It was deferred one pass pending an
audit of the invariant every such cache depends on. §8 is that audit and its
result.

## 8. Caching the per-tick death check

### The audit, and a correction to §7

**§7's count was wrong.** It said `hediffs` is mutated at eleven sites, "two of
them outside `HediffSet` itself (`Hediff.cs` and `Damage.cs` both `Add` to the
list directly)", and used that as the reason to defer. Reading all of them:

- `Damage.cs:160` does not touch the hediff set at all. It appends to
  `DamageResult.hediffs`, the damage worker's own bookkeeping list, which merely
  shares a field name. It was never a mutation site.
- `Hediff.cs:616` does add directly, and calls `DirtyCache()` immediately after
  its loop.
- All six sites inside `HediffSet` and all three in `Pawn_HealthTracker` dirty
  the cache.

So the invariant holds everywhere, and the risk §7 named was overstated by a
grep rather than a reading. Recorded here rather than quietly corrected, because
a wrong reason for deferring work is worth the same scrutiny as a wrong number.

### What the audit did find

A dependency neither §7 nor the original report considered: **core-part
efficiency depends on the pawn's age, not only on its hediffs.** Part max health
is `hitPoints × HealthScale` (`BodyDefs.cs`), and `HealthScale` scales with the
current life stage. An injured pawn crossing a life-stage boundary changes its
core efficiency with no hediff changing at all, so a cache dirtied only by
hediff mutations keeps a child's value for the rest of that pawn's life.

`Pawn_AgeTracker` therefore caches its life-stage index with the exact tick
bounds it is valid between, and dirties the health cache when a crossing
actually happens. That removes a second cost as a side effect: `CurLifeStageIndex`
previously rescanned the race's life-stage list on every read, and `HealthScale`
reads it — so the per-tick death check was paying for that scan on every pawn on
every tick.

The bounds are deliberately two-sided. A first attempt guarded only "when does
the next stage begin", which happily keeps an adult stage for a pawn whose age
is set backwards — `DebugSetAge` and mothballed catch-up both do that. Two
existing tests caught it; `HealthCacheTests` now pins it directly.

### Measured

Wall-clock comparisons against §1–§7 are **not valid**: those were taken on an
otherwise-idle box, and this machine was running three build agents. Absolute
numbers here are roughly 60% higher for that reason alone. So this was measured
as an A/B instead — same box, same minute, same load, alternating builds:

| run | N=1000, ms/in-game-day |
| --- | --- |
| with caching | 24,571.7 |
| without (HEAD) | 27,846.2 |
| with caching again | 25,931.8 |

**Roughly 7–12% faster.** Real, and modest — which is itself the finding. The
remaining per-tick cost is not one dominant call any more; it is spread across
`ShouldBeDeadFromRequiredCapacity` walking every lethal capacity, the hediff
tick loop, and the needs and mind-state trackers. There is no third obvious
single-call win here, and the next honest step for population scale is the
tiering in spec §11.3, not another micro-optimization.

## 9. Path sharing (`ai.pathing.sharing`)

Measured on the same box as §1–§8 (dotnet SDK 8.0.424, 4 vCPU Xeon @ 2.10GHz,
Ubuntu 24.04 — see "Hardware / runtime" above), `dotnet run -c Release
--project tools/bench/SimWorld.Bench -- --suite pathing`, 1 warmup + 3 measured
trials per row (median reported), fixed seed 12345. This machine had other
worktree sessions present but not competing for CPU during the timed runs
(same discipline as §1–§8's "otherwise idle" note); small-N rows still show
some run-to-run wobble on a repeat run (±10–20%) — treat the single-digit-N
rows as directional and the trend across the sweep as the load-bearing part,
consistent with this report's own guidance in the intro.

**What this measures:** `AI.PathFinder` now tries a region-graph corridor
(`AI.RegionPathCorridorCache`) before falling back to its old unconstrained
`A*` — see that class's own remarks in source for the full design argument. Each
row scatters N pawns across N distinct rooms of a door-connected grid of 3x3
rooms sized to just exceed N (so population and settlement size grow
together, matching the "civilization gets bigger as its population grows"
framing this item targets), all pathing once to one shared destination room
near the grid's centre. "before" is `PathFinder.DisableRegionCorridor = true`
— exactly today's pre-sharing behaviour, one full-map `A*` per pawn, unchanged
by this pass. "after" is the shared default. Both sides use a **fresh**
`PathFinder` per trial (an empty corridor cache) over the same
already-region-mapped map, so "after"'s cost is one real corridor-tree build
plus N cheap reads — what a population converging on a destination in an
already-settled map actually costs, not a pre-warmed best case.

| N | rooms | before (ms) | after (ms) | speedup |
| --- | --- | --- | --- | --- |
| 100 | 121 | 5.0 | 3.2 | 1.54x |
| 400 | 441 | 29.3 | 11.7 | 2.51x |
| 1,000 | 1,024 | 75.1 | 39.2 | 1.92x |
| 2,500 | 2,601 | 608.0 | 98.0 | 6.21x |
| 5,000 | 5,041 | 1,992.2 | 247.4 | 8.05x |
| 10,000 | 10,201 | 8,889.8 | 918.8 | 9.68x |

**The sharing pays, and increasingly so as the population (and the settlement
it lives in) grows.** The speedup is a modest ~1.5–2.5x at N≤1,000 — noisy at
this end, and the unconstrained search is already fast on a small map, so
there is less to save — but climbs to 6–10x by N=2,500–10,000. That shape is
exactly what the design predicts rather than a coincidence: "before"'s
per-pawn cost is one full `A*` over a map whose area grows with N (roughly
O(N) work per pawn, so O(N²) total), while "after"'s per-pawn cost is a
corridor read bounded by the region-hop distance to the destination — which
grows only with the map's *diameter* (roughly O(√N) as a square-ish map grows
with N) — plus a local search confined to that corridor's own cells, not the
whole map. The one-time corridor-tree build (§ design doc: paid once per
unique destination, amortized across every pawn that shares it) is real but
small next to N searches once N is in the hundreds. Net effect measured here:
"after" grows roughly linearly with N (0.032 ms/pawn at N=100 to 0.092
ms/pawn at N=10,000) where "before" grows clearly superlinearly (0.050
ms/pawn at N=100 to 0.889 ms/pawn at N=10,000) — an asymptotic win, not just a
constant-factor one, which is the property a civilization-scale population
target actually needs.

**What this does not claim:** the corridor is a region-hop shortest path, not
a cell-distance shortest path, so a hierarchical route can be longer than
`PathFinder`'s own unconstrained optimum — disclosed and bounded, not hidden,
by `PathSharingTests.Hierarchical_corridor_can_be_longer_than_optimal_but_is_still_a_valid_path`
(`tests/SimWorld.Core.Tests/AI/PathSharingTests.cs`). And this section measures
only the cost of *finding* paths for many simultaneous movers to one
destination — it says nothing about per-tick ticking cost, which is what §1–§8
are about and where the population ceiling below is actually set.

## 10. Fine-grained cache dirtying for hediff severity drift

Finding #3 in "Where the ceiling is" (below, as it read before this section):
`Hediff.Severity`'s setter dirties `HediffSet`'s *entire* cache — and, through
it, `PawnCapacitiesHandler`'s one shared `dirty` bool covering all ten
`PawnCapacityDef`s — on every severity change, with no regard for whether that
change could possibly move any cached value. `HediffComp_Immunizable` (every
disease: Flu, Plague, WoundInfection) nudges severity every tick it isn't fully
immune, so a cache built to be computed once and reused was instead rebuilt on
demand up to 60,000 times a day per sick pawn. This is the mechanism §3
measured and the third bottleneck the original report named.

It also changes how finding #2 should be read. That finding, as originally
written, said `ShouldBeDead` called `PawnCapacityUtility.CalculatePartEfficiency`
directly, "bypassing `PawnCapacitiesHandler`'s own dirty-flag cache entirely,"
and that the fix landed in §7 covered only the allocation half, leaving "the
CPU half stands: the check still runs every tick and still recomputes from
scratch." That description stopped matching the code once §8 landed
(`086e716`): `ShouldBeDead` reads `hediffSet.CorePartEfficiency`, a cache
behind `DirtyCache()`, not an uncached direct call, and `Pawn_AgeTracker`
covers the one extra input (life stage) that cache depends on beyond hediffs.
Read literally, finding #2's "still recomputes from scratch" claim was already
stale by the time this section was written — but the *symptom* it described
(a sick pawn's death check paying full recomputation cost every tick) was
still real, for the reason finding #3 gives: §8's cache was invalidated as
fast as it was filled, by finding #3's mechanism, so it never got the chance
to pay for itself on a sick pawn. Finding #2's "the CPU half stands" line
below is corrected to say this plainly, in the spirit of §8 correcting §7:
the remaining cost was never an uncached call bypassing a cache. It was a
cache being dirtied by every tick a disease was active, which is finding #3,
and closing finding #3 closes what was left of finding #2 along with it.

### The fix

Every hediff type except `Hediff_Injury` derives every quantity the cache
reads — `PainOffset`, `BleedRate`, `CapMods`, and (through `HediffStage`)
`partEfficiencyOffset` — from `CurStage`, never from raw `Severity`. A severity
nudge that stays inside the current stage's `minSeverity` band cannot move any
of those, so dirtying the cache for it is exactly the waste finding #3 names.
`Hediff_Injury` is the sole exception: its part-health contribution (and so
core-part efficiency), pain, and bleed rate are literally `Severity`, not
`CurStage`, so it must keep dirtying on every change.

A new `Hediff.SeverityAffectsCachesWithinStage` virtual property (default
`false`; overridden `true` only on `Hediff_Injury`) records which case a
hediff type is. `Severity`'s setter now compares `CurStageIndex` before and
after the change and calls `Pawn_HealthTracker.Notify_HediffChanged` with
whether the stage actually moved (always `true` for an injury, since it never
checks). `Notify_HediffChanged` only calls `HediffSet.DirtyCache()` — and so
`PawnCapacitiesHandler.Notify_CapacityLevelsDirty()` — when that flag is set;
`CheckForStateChange` still runs unconditionally either way, so a hediff's
death and downed checks are exactly as fresh as before. `Hediff.CauseDeathNow()`
(lethal-severity death) and `Hediff.ShouldRemove` (severity ≤ 0 removal) were
never part of the cache this skips — both are read live, uncached, on every
`ShouldBeDead()`/`HealthTick()` call regardless of this change, so a hediff
crossing its lethal threshold or dropping to zero mid-stage is caught exactly
as before.

Comparing stage indices is a couple of short backward scans over a def's stage
list (2-3 entries for every disease shipped) — negligible next to the
`PawnCapacitiesHandler.Recalculate()` it now usually avoids, which walks all
ten capacities' body-tree math from scratch.

**Every hediff type in the codebase was checked against the invariant this
relies on** (`Hediff`, `Hediff_Injury`, `Hediff_MissingPart`, `Hediff_AddedPart`
— the whole hierarchy, nothing else exists): `Hediff_MissingPart` overrides
`Severity` itself with a no-op setter, so this change never applies to it;
`Hediff_AddedPart` uses the stage-based base formulas untouched; `Hediff_Injury`
opts out of the skip entirely. Nothing else derives a cached quantity from raw
severity outside its stage.

### Tests

`tests/SimWorld.Core.Tests/Health/HealthCacheTests.cs` gained two tests that
pin the skip is exact, not just plausible:

- Many small within-stage severity nudges on a `WoundInfection` (the same
  shape `HediffComp_Immunizable` produces), asserting after *every* nudge that
  pain, bleed rate, core efficiency, total injury severity, and a capacity
  level all match a fresh, uncached recompute — through the one nudge that
  finally crosses into the next stage, where they must (and do) change.
- A full untended `WoundInfection`, ticked for real through
  `HediffComp_Immunizable`'s actual per-tick drift (not a synthetic severity
  jump) all the way to death, asserting the run actually passes through the
  major and extreme stages and that the recorded death cause is correct.

The pre-existing behaviour-pinning suite this lane was told to protect — death
from the lethal damage threshold (`Total_injury_beyond_the_lethal_threshold_kills`),
from a destroyed core part (`A_pawn_still_dies_when_the_cached_check_says_it_should`),
from a lost lethal capacity (`Destroying_the_brain_kills`, `Destroying_the_heart_kills`),
and the downing thresholds (`Losing_both_legs_downs_but_does_not_kill`,
`Pain_shock_downs_and_healing_lifts_it`, `Force_downed_overrides_the_body`) —
already existed from §7/§8 and needed no changes; it was run before and after
this fix and passes unchanged both times. Full suite: 1,217 tests before this
section's change, 1,219 after (the two above).

### Measured

This box was carrying other concurrent sessions during these runs, the same
caveat §8 raised about its own numbers — a "no hediffs" row here reads ~40-42s
where §1-§3's quiet-box runs read ~17s for the same N=1,000. So, following
§8's own precedent, this is an **A/B on the same box in the same short window,
alternating builds**, not a comparison against §1-§9's absolute numbers.

N=20, 1 in-game day, 1 warmup + 3 measured trials (median), seed 12345, two
alternating passes:

| variant | before (ms) | after (ms) | before→after speedup |
| --- | --- | --- | --- |
| no hediffs | 599.9 (avg of 614.1, 585.7) | 558.9 (avg of 571.1, 546.6) | 1.07x (noise; this path is untouched) |
| 3 injuries | 9,950.5 (avg of 10,110.9, 9,790.1) | 1,143.4 (avg of 1,152.0, 1,134.7) | **8.70x** |
| 3 injuries + Flu | 45,035.2 (avg of 45,381.4, 44,689.0) | 1,819.5 (avg of 1,837.2, 1,801.8) | **24.76x** |

Multiplier vs. that side's own "no hediffs" row: before, 3 injuries is 16.59x
and +Flu is 75.07x — both close to §3's original *quiet-box* figures (24.8x,
and a projected ~45-50 min at N=1,000). After, 3 injuries is 2.05x and +Flu is
3.26x: the disease no longer dominates the cost of the wounds it was riding
alongside.

N=1,000, 1 day, same `--guard-seconds 120` §1-§9 use throughout:

| variant | before | after |
| --- | --- | --- |
| no hediffs | 40,262.5 ms (median of 3) | 42,021.5 ms (median of 3) |
| 3 injuries | 501,987.9 ms (guard, 1 trial) | 82,503.8 ms (median of 3) — **6.08x faster** |
| 3 injuries + Flu | did not complete a single in-game day within 27 minutes of wall clock, killed | 138,995.4 ms (guard, 1 trial) |

The before row for "3 injuries + Flu" is not a number, deliberately: at
N=1,000 it never finished, same as §3's original quiet-box finding except
worse, because this box was busier. That itself is the headline result —
after this fix, the exact scenario §3 flagged as unable to complete a single
in-game day at civilization-relevant scale now completes one in under two
and a half minutes, on a busier box than the one that couldn't finish it at
all in fifteen. Extrapolating the N=20 before-side number linearly to N=1,000
(§1 says linear is an *underestimate* of the real superlinear scaling) lands
around 37-38 minutes — consistent with "still running past 27 minutes and not
done" rather than contradicting it.

N=5, unwarmed, 1 sample — §3's own original method, for direct continuity:

| variant | before (ms) | after (ms) |
| --- | --- | --- |
| no hediffs | 1,069.5 | 811.0 |
| 3 injuries | 1,563.2 (1.46x) | 608.6 (0.75x) |
| 3 injuries + Flu | 12,017.5 (11.24x) | 689.0 (0.85x) |

At this tiny, unwarmed N the absolute numbers are noise-dominated (§3 said the
same about its own N=5 sample) — the after-side "0.75x" and "0.85x" are not a
sick pawn ticking *faster* than a healthy one, they are three variants that
now all cost about the same, small amount, and JIT/GC noise decides which one
comes out on top by a few hundred microseconds. That flatness is itself the
result: the multiplier this fix targets has gone from "dominant" to "within
noise" at this scale.

**What this bought:** the two things named as still open — an uncached
recompute and a cache that never got to keep anything — are both closed now
(see "The fix" and the note above on finding #2). A sick population's tick
cost, measured at N=20 and N=1,000, dropped 8-25x depending on how much
disease-driven severity drift was in play, while the healthy-pawn path (§1's
own ceiling case) is unchanged within noise, as it should be — nothing here
touches a pawn with no hediffs at all.

**What this does not buy:** ticking a sick pawn still costs more than ticking
a healthy one — 2.05x for wounds alone, 3.26x with a disease on top of them,
measured at N=20. That remainder is not a cache bug; it is the real, expected
cost of more state per pawn: more `Hediff.Tick()` calls, comp iteration,
`Rand.MTBEventOccurs` rolls for life-threatening stages, and the periodic
bleed/heal/immunity work every injury and disease genuinely requires. There is
no further single-call win visible here, the same conclusion §8 reached after
its own fix. The next step for population scale, same as §8 said, is the
tiering `docs/spec/simworld-spec.md` §11.3 already calls for, not another
micro-optimization of this path.

## Where the ceiling is

The engine comfortably handles world generation and save/load at civilization-
relevant scale (§5, §6). The ceiling is entirely in per-pawn ticking, and it is
**much lower than 1,000 full-agent citizens the moment any of them are hurt or
sick** — which, in a running colony, is the normal state, not the exception.
Path sharing (§9) is a real, increasingly large win for the *pathing* half of
that "pathing and tick-budget pass" — but it does not move this ceiling: the
ceiling below was never about pathing cost, it is about `HealthTick`, and nothing
in §9 changes that.

**Top 3 bottlenecks, in order of impact:**

1. **`HealthTick` is 75.4% of all per-pawn tick cost** (§2), roughly 6x the
   next-largest tracker (`needs`, 12.5%). Any optimization pass that doesn't
   touch `Pawn_HealthTracker` is optimizing the wrong 25%.

2. ~~An uncached, unconditional per-tick capacity check drove ~86.5% of all
   allocation.~~ **Fixed — see §7 (allocation) and §8 (the CPU cost of the
   call site itself, via caching `hediffSet.CorePartEfficiency`).** This
   finding originally said the allocation fix in §7 left "the CPU half"
   standing, because `ShouldBeDead` still called
   `PawnCapacityUtility.CalculatePartEfficiency` directly every tick. That
   stopped being true once §8 landed (`086e716`): `ShouldBeDead` reads a
   cached `hediffSet.CorePartEfficiency` now, not an uncached direct call.
   This line was left saying otherwise for a section written before that
   correction was folded in — corrected here, in the same spirit §8 corrected
   §7. What was actually still recomputing from scratch on a sick pawn was
   finding #3's mechanism defeating §8's cache, not this call bypassing one.
   See §10.

3. ~~Hediffs with a per-tick severity drift (any `HediffComp_Immunizable` —
   i.e. every disease: Flu, Plague, WoundInfection) invalidate the pawn's
   *entire* capacity cache every single tick, not just when something that
   actually affects capacities changes.~~ **Fixed — see §10.** The chain was:
   `Hediff.Severity`'s setter (src/SimWorld.Core/Health/Hediff.cs:43-49)
   called `Notify_HediffChanged` → `HediffSet.DirtyCache()`
   (src/SimWorld.Core/Health/HediffSet.cs:370-374) → `Notify_HediffSetChanged()`
   → `PawnCapacitiesHandler.Notify_CapacityLevelsDirty()`
   (src/SimWorld.Core/Health/Capacities.cs:360-363), which sets one shared
   `dirty` bool covering *all ten* `PawnCapacityDef`s, unconditionally, for
   every severity change on every hediff. `HediffComp_Immunizable.CompPostTick`
   (src/SimWorld.Core/Health/Hediff.cs:271-274) adds a nonzero severity delta
   *every tick* while not fully immune, so the cache — designed to be
   recomputed once and reused — was rebuilt up to 60,000 times a day per sick
   pawn. Measured impact before the fix (§3, §10): 3 untended injuries alone
   already cost 16.6-24.8x depending on N; adding one disease with an
   Immunizable comp on top made a single day fail to finish within 15-27
   minutes at N=1,000, on either the quiet box §3 used or the busier one §10
   did. §10's fix distinguishes a severity nudge that cannot move any cached
   value (stays inside the current stage, true for every non-injury hediff
   the game ships) from one that can, and only dirties for the latter —
   measured 8.7-24.8x faster on the exact scenarios above, with the healthy-
   pawn path unchanged.

**What the numbers say the engine cannot currently do:** it cannot run 1,000
full-agent citizens with any meaningful rate of injury or illness at anything
faster than 1x, and probably not comfortably even then — a colony where
everyone is hale and hearty sustains 15x turbo speed only up to roughly
2,500–5,000 pawns (measured), but combining that with measurement 3's
multipliers (labeled PROJECTION, not measured: injuries-alone's 24.8x applied
to the 15x ceiling implies well under 200 pawns once the population is
carrying typical wear — closer to what a single RimWorld colony looks like
than a civilization). The spec's own open question — "population ceiling: full-
agent depth at civilization scale needs a pathing and tick-budget pass before a
real target can be set" (`docs/spec/simworld-spec.md`) — is answered by this
report: at today's engine, that ceiling is set by health-tracker cost, chiefly
one uncached per-tick capacity computation and one cache-invalidation pattern
that turns a should-be-rare recomputation into a should-never-happen-this-often
one. Fixing either (or both — they share a root cause) is very likely worth
more than fixing everything else in this report combined.

**Update (§9):** the "pathing" half of that same open question is now built
and measured — region-graph path sharing turns "N pawns to a shared
destination" from a roughly-O(N²) cost into a roughly-O(N) one, an
increasingly large win as the population (and its settlement) grows, 9.7x
measured at N=10,000. It does not move the ceiling above, which was always a
tick-budget (health-tracker) finding, not a pathing one — the two are
independent axes of the same open question, and this report now has a
positive, measured answer for one of them.

**Update (§10):** the "well under 200 pawns" figure above was built on
injuries-alone's pre-fix 24.8x multiplier, and both named findings it rested
on are now closed (§10). Re-running that same arithmetic with §10's measured
post-fix multiplier for the worse of the two cases — wounds *and* a disease
together, 3.26x at N=20, the actual scenario "any meaningful rate of injury
or illness" describes — against the same 2,500-5,000-pawn healthy-population
15x ceiling (§1, unchanged by this fix) puts a sick population's 15x ceiling
at roughly **750-1,500 pawns**. Still a PROJECTION, not a measurement, and
still short of the healthy-population figure it's divided from — a sick
colony is not free — but roughly an order of magnitude higher than the figure
this report gave before §10, because the thing driving that order of
magnitude is exactly what closed. The engine's own state has not changed
since §9: this is a health-tracker finding, path sharing is orthogonal to it,
and the ceiling has never been about world generation or save/load (§5, §6).
