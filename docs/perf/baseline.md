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

Per-pawn-day cost is **not** flat: 14.0–14.1 µs/pawn-day up to N=500, jumping to
17.4 at N=1,000 and 21.7 at N=2,500, dipping back to 18.1 at N=5,000, and
20.4 at N=10,000 (single trial, more noise). This is superlinear-with-jitter,
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

## Where the ceiling is

The engine comfortably handles world generation and save/load at civilization-
relevant scale (§5, §6). The ceiling is entirely in per-pawn ticking, and it is
**much lower than 1,000 full-agent citizens the moment any of them are hurt or
sick** — which, in a running colony, is the normal state, not the exception.

**Top 3 bottlenecks, in order of impact:**

1. **`HealthTick` is 75.4% of all per-pawn tick cost** (§2), roughly 6x the
   next-largest tracker (`needs`, 12.5%). Any optimization pass that doesn't
   touch `Pawn_HealthTracker` is optimizing the wrong 25%.

2. **An uncached, unconditional per-tick capacity check drove ~86.5% of all
   allocation.** *(Allocation half fixed — see §7. The CPU half stands: the
   check still runs every tick and still recomputes from scratch.)*
   `Pawn_HealthTracker.ShouldBeDead()` (src/SimWorld.Core/Health/Pawn_HealthTracker.cs,
   `CheckForStateChange` → `ShouldBeDead`, called unconditionally at the end of
   every `HealthTick`) calls `PawnCapacityUtility.CalculatePartEfficiency(hediffSet, corePart)`
   directly — bypassing `PawnCapacitiesHandler`'s own dirty-flag cache
   entirely (src/SimWorld.Core/Health/Capacities.cs:349-374). That call walks
   `HediffSet.GetHediffsOnPart` (src/SimWorld.Core/Health/HediffSet.cs:164), a
   `yield return` generic iterator that allocates a heap enumerator on every
   call whether or not the part has any hediffs. Measured at 64 bytes/call,
   run 60,000×/pawn/day unconditionally, this one call site alone accounts for
   an estimated 3,662 of the 4,232 MB allocated per pawn per day (§4) — and,
   by extension, for a large share of the 33 Gen0 / 4 Gen1 / 1 Gen2 collections
   per pawn-day, which is a very plausible explanation for §1's non-monotonic,
   worse-than-linear scaling as N grows.

3. **Hediffs with a per-tick severity drift (any `HediffComp_Immunizable` —
   i.e. every disease: Flu, Plague, WoundInfection) invalidate the pawn's
   *entire* capacity cache every single tick, not just when something that
   actually affects capacities changes.** The chain: `Hediff.Severity`'s
   setter (src/SimWorld.Core/Health/Hediff.cs:43-49) calls
   `Notify_HediffChanged` → `HediffSet.DirtyCache()`
   (src/SimWorld.Core/Health/HediffSet.cs:370-374) → `Notify_HediffSetChanged()`
   → `PawnCapacitiesHandler.Notify_CapacityLevelsDirty()`
   (src/SimWorld.Core/Health/Capacities.cs:360-363), which sets one shared
   `dirty` bool covering *all ten* `PawnCapacityDef`s. `HediffComp_Immunizable.CompPostTick`
   (src/SimWorld.Core/Health/Hediff.cs:271-274) adds a nonzero severity delta
   *every tick* while not fully immune, so the cache — designed to be
   recomputed once and reused — is instead invalidated and rebuilt 60,000
   times a day. Measured impact (§3): 3 untended injuries alone (which dirty
   the cache ~1,700×/day via bleeding/healing, not every tick) already cost
   24.8x; adding one disease with an Immunizable comp made a single day fail
   to finish in 15 minutes at N=1,000, and a smaller-N sample shows roughly a
   further 10x on top of the injuries-alone cost.

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
