# A raid that lands in rock, and a pawn with nothing to do

Measured for `ai.raidstranded`. Its own file rather than a section appended to
[`baseline.md`](baseline.md), for the reason
[`constant-think-tree.md`](constant-think-tree.md) gives: several lanes run in
parallel and `baseline.md` is the file every one of them wants to append to.

## What was seen

On the storyteller bench (`--suite storyteller --solo false --days 60`) the
wealth term made raids about 1.7 times larger, and the run slowed from 24,226
ticks/s to about 4,400. The slowness was not the bigger raids. It was raiders who
were never able to do anything.

Both seeds the lane was given showed it, and for the same reason:

| seed | raid | landed | where the raiders were | stood for |
| --- | --- | --- | --- | --- |
| 777 | day 13.8, 4 raiders | (199, 0, 49) | 3 of 4 inside a limestone face | to day 23.5 |
| 777 | day 33.9, 5 raiders | (199, 0, 44) | first raider not joined to the colony; 71% of its samples had no job | to day 43.6 |
| 777 | day 53.9, 7 raiders | (0, 0, 48) | first raider not joined to the colony; 73% no job | the end of the run |
| 12345 | day 25.2, 5 raiders | (52, 0, 0) | 4 of 5 inside a limestone face | to day 35.0 |
| 12345 | day 46.7, 5 raiders | (1, 0, 197) | first raider not joined to the colony; never came near a colonist | dissolved on arrival |

[`dump-before-777-day15.txt`](stranded-raid/dump-before-777-day15.txt) and
[`dump-before-12345-day26.txt`](stranded-raid/dump-before-12345-day26.txt) are the
state of the stuck raiders on the day they were looked at: every one stands on a
cell whose thing list holds `Limestone(Impassable)`, `walkable=False`,
`region=null`, `roofed=True`, with eight unwalkable neighbours (or the map edge).
The 777 raiders have no job; the 12345 ones are asleep (`LayDown`) and will have
none when they wake.

## Three causes, each with its RimWorld source

1. **A raid took any cell of any edge.** `IncidentWorker_RaidEnemy.Arrive` used
   `rand.Range` to pick one of four edges and a cell along it, and asked nothing
   of the cell. RimWorld's
   `PawnsArrivalModeWorker_EdgeWalkIn.TryResolveRaidSpawnCenter` calls
   `RCellFinder.TryFindRandomPawnEntryCell`, which tries edge cells until one is
   standable, unroofed and `map.reachability.CanReachColony`; if none is, the raid
   does not happen. Each pawn then goes on `CellFinder.RandomClosewalkCellNear`,
   which searches the regions *joined to the root*. This port's closewalk tried
   thirty random offsets, asked each only whether it was standable, and fell back
   to the root. On a rock face every offset failed, and the fallback put three
   raiders on the entry cell itself.
2. **A tree could come back empty, and then ran every tick.**
   `Pawn_JobTracker.JobTrackerTick` asks a jobless pawn's tree for a job on every
   tick, exactly as RimWorld's does. RimWorld's tree cannot come back empty: it
   ends in `Verse.AI.JobGiver_IdleError`, a `Wait` of 100 ticks. This port's ended
   in `JobGiver_WanderAnywhere`, which returns nothing after eight random offsets
   all fail, and up to eight draws from the random stream per tick besides. A pawn in no
   region fails every reachability question in every tier, so the whole tree ran,
   every tick, for as long as the pawn lived.
3. **The lord did give up, and it changed nothing.** `LordJob_AssaultColony`'s
   give-up timer fired inside RimWorld's 26,000 to 38,000 tick window both times
   (seed 777 at 14.30, half a day after landing; seed 12345 at 25.81). It hands
   every member `ExitMapBest`, and a pawn in no region has no exit to walk to.
   RimWorld has no such raider (cause 1 is why), so it has nothing for one: its
   assault with no path digs (`LordToil_AssaultColonySappers`), trashes the
   colony's walls (`JobGiver_AITrashColonyClose`, `JobGiver_AITrashBuildingsDistant`)
   or walks to the nearest hostile it can reach (`JobGiver_AIGotoNearestHostile`,
   ported). This port has no digging and its buildings belong to nobody
   (`Duties_Assault.xml` says why), so an assault with no path *leaves*: the
   `ExitMap` tier of `AssaultSettlement` fires as soon as no enemy is reachable.
   That was already right and is now pinned by a test.

## What changed

| change | RimWorld |
| --- | --- |
| `Map/CellFinder.cs`: `RandomEdgeCell`, `TryFindRandomEdgeCellWith` (100 random tries, then every edge cell shuffled), `RandomClosewalkCellNear` (a standable cell in the root's own connected area) | `Verse.CellFinder` |
| `AI/RCellFinder.cs`: `TryFindRandomPawnEntryCell` (standable, unroofed, reaches the colony) | `RimWorld.RCellFinder` |
| `AI/Reachability.Colony.cs`: `CanReachColony`, `CanReachBiggestMapEdgeRoom` | `Verse.Reachability` |
| `IncidentWorker_RaidEnemy.Arrive` uses the above and returns false when there is no entry cell | `PawnsArrivalModeWorker_EdgeWalkIn` |
| `AI/JobGiver_IdleError.cs`, `JobDefs_Wait.xml`, the last node of both main think trees | `Verse.AI.JobGiver_IdleError`, `JobDefOf.Wait` |

Two translations, recorded rather than improvised. `CanReachColony`'s middle leg
(a colonist-owned building can be touched) is not ported: buildings belong to
nobody here. `CanReachBiggestMapEdgeRoom` measures an area in cells, not regions:
RimWorld's regions are at most 12 cells a side (`Region.GridSize`) so a region
count is an area there, and this port's regions are as large as the open ground
they flood, so counting them called a one-region strip the equal of the map (the
test that found it is `With_no_colonist_able_to_walk_the_biggest_open_area_...`).

## Speed

Release, `--suite storyteller --solo false --days 60`, one bench at a time on a
shared 4-core box (load average 2 to 10 over the runs; wall seconds move with it,
**process user CPU is the steadier column**). Before is `4dcbfec`, after is this
branch. Raw rows in [`bench-summary.txt`](stranded-raid/bench-summary.txt).

| seed | build | ticks/s | seconds/day | wall s | user CPU s |
| --- | --- | --- | --- | --- | --- |
| 777 | before | 3,963 | 15.14 | 908 | 908 |
| 777 | after | 23,520 | 2.55 | 153 | 177 |
| 12345 | before | 3,742 | 16.03 | 962 | 863 |
| 12345 | after | 26,840 | 2.24 | 134 | 154 |

The user CPU is for the whole process, which includes the 8-day determinism
re-run. About five times less CPU in both seeds, and back to the 24,226 ticks/s
the wealth lane started from.

What one stuck squad costs, from the probe (seconds of wall time for each
in-game day, sampled while the squad stood; an ordinary day is 2 to 3 seconds):

| seed | days | seconds/day |
| --- | --- | --- |
| 777 | 15 to 23 (raid 1) | 24.6 to 43.1 |
| 777 | 35 to 43 (raid 3) | 17.1 to 23.5 |
| 777 | 55 to 60 (raid 6) | 33.4 to 41.5 |
| 12345 | 26 to 35 (raid 2) | 41.9 to 147.4 |

Seed 12345's slowdown is the same cause, not another: four raiders of its
day-25 raid in a limestone face at (52, 0, 0), and a second squad whose first
raider was not joined to the colony at (1, 0, 197) on day 46.

## Raids

[`probe.cs`](stranded-raid/probe.cs) runs the same arm as the storyteller suite and
follows every lord on the watched map. "Connected" is whether the first raider's
region joins a colonist's at the moment of arrival; "reached" is whether any raider
came within 45 cells of a living colonist. Outputs:
[`probe-before-777.txt`](stranded-raid/probe-before-777.txt),
[`probe-after-777.txt`](stranded-raid/probe-after-777.txt),
[`probe-before-12345.txt`](stranded-raid/probe-before-12345.txt),
[`probe-after-12345.txt`](stranded-raid/probe-after-12345.txt).

| seed | build | raids | not connected at arrival | stood on the map more than a day |
| --- | --- | --- | --- | --- |
| 777 | before | 6 | 2 (and 1 more with 3 of its 4 raiders in rock) | 3 |
| 777 | after | 5 | 0 | 0 |
| 12345 | before | 7 | 2 | 1 (and 1 dissolved on arrival) |
| 12345 | after | 4 | 0 | 0 |

After the change every raid lands on a cell joined to the colony, comes within 45
cells of a colonist, holds an attack job, and is over (dissolved, fled or beaten)
inside 0.02 to 0.16 of a day.

## What did not stay the same, and why that is not a result

The two builds do not play the same game after the first changed draw. A pawn whose
wander failed used to draw eight numbers from the global stream every tick; it
now draws none, and a raid's entry cell draws differently, so from the first day
either of those happens the storyteller sees a different world. The day-14 raid of
seed 777 is 191 points before and 405 after, and seed 12345 fired 7 raids before
and 4 after (none refused: 0 of 23 firings were `fired? = no`). Outcomes are
therefore two samples of a distribution, not an A/B:

| seed | build | moments | eventful days | deaths | peak downed | end pop |
| --- | --- | --- | --- | --- | --- | --- |
| 777 | before | 21 | 19/60 | 10 | 4 | 17 |
| 777 | after | 21 | 18/60 | 10 | 2 | 17 |
| 12345 | before | 20 | 23/60 | 19 | 2 | 10 |
| 12345 | after | 18 | 23/60 | 20 | 3 | 7 |

Determinism reads "yes" on all four series in all four runs. No constant, curve or
def was tuned; none of these columns is a target (`CLAUDE.md`).

## Mutation proofs

[`mutation.txt`](stranded-raid/mutation.txt), produced by
[`mutate.py`](stranded-raid/mutate.py), which aborts if a mutation does not change
the file, if the restore is not byte-identical, if the rebuilt assembly (or, for
content, the copy shipped beside the tests) is not newer than or identical to the
restored source, or if a run prints no `Passed!`/`Failed!` summary line. Nine
mutations, each caught, each followed by a clean restored run:

| mutation | caught by |
| --- | --- |
| the entry cell stops asking `CanReachColony` | the cut-off-map arrival test |
| the closewalk ignores the root's area | the closewalk test and the arrival test |
| `CanReachColony`'s colonist leg always true | three |
| the biggest edge area counted in regions | four |
| the biggest-edge-area leg always true | three |
| the humanlike tree loses `JobGiver_IdleError` | six |
| the animal tree loses it | two |
| the wait is 5 ticks | four |
| the wait never expires | four |
