# What a raid costs once it actually walks

Measured for `ai.duties` — the tier that gets an arriving squad from the map edge
to the town it came for. Its own file rather than a section appended to
[`baseline.md`](baseline.md), for the reason
[`constant-think-tree.md`](constant-think-tree.md) gives: several lanes run in
parallel and `baseline.md` is the file every one of them wants to append to.

## Read this first: these are not bench-suite numbers

Everything below was taken from the test harness
(`tests/SimWorld.Core.Tests/AI/RaidApproachTests.cs`, in the working copy that
produced them), **Debug build**, one configuration per `dotnet test` process
because founding a second 200x200 world in the same process moves every number by
2-3x. `baseline.md` and `constant-think-tree.md` are Release builds under
`tools/bench` with warmups and medians; **do not compare absolute milliseconds
across the two.** The A/B ratios here are sound — each pair was taken the same
way, minutes apart, on the same box — and that is all they are offered as.

Making these comparable means a `raid` suite under `tools/bench`; it is not
written, and that is the honest follow-up rather than a caveat to argue past.

Scenario, in every row: a settlement founded the ordinary way from shipped
content (`SettlementFounder`, 30 citizens, a 200x200 interior chosen by
`MapGenTuning.MapSizeForPopulation`), and where a raid is present, a real
`RaidEnemy` firing onto that interior — 15 raiders, nobody armed or enfactioned
by hand. 300 ticks run untimed first so the measurement is not a measurement of
the JIT, then 1,000 ticks timed.

## What a raid costs per tick

| case | pawns | ms/tick |
| --- | --- | --- |
| town alone | 30 | 0.1378 |
| town + raid, marching and engaging | 45 | 0.2816 |

A 15-raider squad adds **0.14 ms/tick**, or **~0.01 ms per raider per tick** —
against the 30-citizen town's own 0.1378. That is roughly what a raider costs as
a pawn at all: a marching raider is a pawn with a job, and the job happens to be
walking.

The approach is cheap for a reason worth stating, because it is not the reason
this lane's brief expected: **a march is one path per raider per leg, not one per
tick.** `Pawn_PathFollower` computes the path once in `StartPath` and then spends
hundreds of ticks stepping along it, and the approach job is re-issued only on
arrival or at `JobGiver_AIGotoNearestHostile.ApproachJobExpiryTicks` (an hour).
Fifteen raiders crossing a map therefore ask `PathFinder` for about fifteen paths
in the first tick and a handful more over the next thousand.

## Path sharing does not carry this case, and the map shape says why

`baseline.md` §9 measures `AI.RegionPathCorridorCache` at 9.7x at N=10,000 — one
region-graph BFS per unique destination, reused by everyone walking there. A
squad walking to one place looks like exactly that case. It is not, and the
measurement says so both ways.

Fifteen raiders, one shared destination, timing `PathFinder.FindPath` directly:

| `DisableRegionCorridor` | first path | remaining 14, mean | total |
| --- | --- | --- | --- |
| false (sharing on) | 1.85 ms | 1.02 ms | 16.06 ms |
| true (sharing off) | 1.34 ms | 0.77 ms | 12.15 ms |

Sharing makes this workload **~30% slower**, and the whole-tick A/B agrees within
noise (0.2816 ms/tick with sharing on against 0.2787 with it off).

The reason is the map, not the cache:

```text
settlement interior, 200x200: 5 regions over 40,000 cells
five largest: 34285, 4, 3, 2, 1
```

One region holds **86% of the map**. A corridor that names the regions a path may
enter cannot narrow a search when there is essentially one region to name, so the
BFS over the region graph and the per-expansion "is this cell's region in the
corridor" test are paid for nothing. §9's 9.7x was measured at 10,000 pawns on a
map with real structure; neither half of that holds here.

**Nothing was changed on the strength of this.** `DisableRegionCorridor` is a
bench-only flag, the corridor path already falls back to an unconstrained search
whenever it cannot produce an answer, and a raid at this cost does not need the
help. What it does say is that §9's win is map-shape dependent and that a
settlement interior — the map the game actually plays on — is the shape where it
does not apply. Measuring that properly, and deciding whether the cache should
stand down on a map with one dominant region, is a follow-up for whoever owns
pathing.

## The behaviour these costs buy

Same fixture, before and after the duty tier landed (ticks are game ticks from
the raid's arrival):

| | before | after |
| --- | --- | --- |
| squad's distance to the nearest citizen on arrival | 100 cells | 100 cells |
| closed after 1,000 ticks | 0 | 62 cells |
| closed after 10,000 ticks | 11 cells (idle wander drift) | in contact |
| first attack job by a raider | tick 6,408 | tick 721 |
| how contact happened | a citizen working the map edge walked into them | the raid walked to the town |
| raid resolved | never | by tick 14,000 |
