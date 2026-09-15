# What the settlement-interior read model costs

Measured for `Map/View` — the seam a host reads to draw one settlement interior
(`docs/spec/simworld-spec.md` §12a, `docs/host/rendering-method.md`). Companion
to [`baseline.md`](baseline.md), in its own file for the same reason
[`hash-phasing.md`](hash-phasing.md) and
[`constant-think-tree.md`](constant-think-tree.md) are: several lanes run in
parallel and `baseline.md` is the file every one of them wants to append to.

Harness: `tools/bench/SimWorld.Bench`, suite `mapview`
(`Suites/MapViewSuite.cs`).

```sh
dotnet run -c Release --project tools/bench/SimWorld.Bench -- \
  --suite mapview --warmup 2 --runs 5
```

Same box and the same discipline as `baseline.md`: 4 vCPU Intel Xeon @ 2.10GHz
(KVM guest, virtualized — not a benchmarking lab), 16 GiB RAM, Ubuntu 24.04,
.NET SDK 8.0.424 / runtime 8.0.30, Release build, fixed seed 12345, 2 warmup
trials discarded + 5–7 measured, median reported.

The suite was run three times over, and the table below gives each row's median
and the range across those runs, because on a shared KVM guest a single median
reads as more precise than it is — one of the three runs overlapped another
agent's test process and every row moved. **The relative comparison between the
rows is the load-bearing part**, and it does not move at all: the incremental
read is two orders of magnitude cheaper than the full one in every run.

## The scenario, which is a real one

Not a synthetic grid. A `TribalStart` civilization founded through
`Game.NewGame` (solo start, band of 20, world subdivision 3), its settlement
opened through `GodCommands.OpenSettlement`, and the interior that
`MapGenerator` actually produced:

```text
map 200x200 = 40,000 cells, 49 chunks of 32 cells square
7,570 non-pawn things — 5,577 mineable rock, 1,880 plants
29 spawned pawns
```

`docs/host/rendering-method.md` quotes 12,991 granite and 822 wild plants from a
different generation. Both are the same story at the same order of magnitude:
**thousands of things and tens of thousands of cells, versus tens of pawns.**
That ratio is what the design is shaped around, and it does not depend on which
seed you take.

## Results

| call | median | range over 3 runs | allocated |
| --- | --- | --- | --- |
| full `Capture()` | **3.57 ms** | 3.36 – 4.05 | 2,048 KiB |
| `CaptureChanges()`, nothing changed | **0.02 ms** | 0.02 – 0.02 | 6 KiB |
| `CaptureChanges()`, one chunk dirty | 0.13 ms | 0.13 – 0.16 | 86 KiB |
| `CaptureChanges()`, terrain dirty | 0.73 ms | 0.70 – 1.09 | 243 KiB |
| `CaptureChanges()`, once per tick (mean over 600 ticks) | **0.023 ms** | 0.019 – 0.025 | 7.4 KiB |

Over that 600-tick run the seam sent back **0.02 chunks per tick** out of 49.
Read as one delta instead, 500 ticks of a live settlement moved 12 of those 49
chunks — which is the same fact from the other end: change is real but it is
slow, and asking every frame is how you turn it into nothing.

The per-tick row is the one that matters, because it is the only one a host
actually runs in a loop. **0.023 ms is 0.14% of a 60 Hz frame.** A full
`Capture()` every frame would be 3.6 ms — a fifth of the frame budget spent
re-reading a map that did not change — and that is the shape this seam exists
to avoid. That ratio, roughly **150×**, is the measurement; the absolute
milliseconds are this box's.

The tick itself is outside both meters in that row: what is measured is the
seam's cost, not the simulation's.

## Why it is three models and not one

Terrain, things and pawns change at three different rates, and the whole design
is taking that seriously:

- **Terrain and roofs** barely change. One version number each; when it moves,
  the host re-pulls all 40,000 cells as a palette plus one `int` per cell. That
  costs ~0.7 ms and happens when somebody lays a floor or a roof falls in, not
  every frame. A per-cell diff would cost more to maintain than this costs to
  take. The roof palette carries `IsNatural` and `IsThickRoof` beside the
  defName, so a host finds the overhead mountain without hardcoding the string
  `"RoofRockThick"` — get that wrong and a solid mountain draws as open sky,
  which is the roof mistake worth designing against.
- **Things** change rarely and locally. The map is cut into 32-cell-square
  chunks, each with its own version, bumped when a Thing is registered in,
  deregistered from, or moved within it. One wall built re-sends one chunk:
  0.13 ms, not 3.6.
- **Pawns** move constantly and are few — 29 here. They are never chunked and
  never versioned; every read re-captures all of them. That is the 6 KiB and
  ~0.02 ms floor in the steady-state row, and it is the price of never having to
  track which chunk a walking pawn is in.

**A walking pawn dirties no chunk**, and that is the single load-bearing
property. If it did, every step would drag its chunk's ~150 things back across
the seam and the steady-state row above would look like the one-chunk row
instead — for every pawn, every few ticks.
`MapViewTests.A_pawn_walking_across_the_map_dirties_no_chunk` pins it directly,
and `MapViewIntegrationTests` pins the trend on a real settlement, because this
is exactly the kind of property that would quietly stop holding and still look
like it worked.

### Chunk size

32 cells square, giving a 200×200 map 7×7 = 49 chunks. Not a RimWorld number —
RimWorld has no equivalent, because its renderer and its simulation are one
assembly and it draws from live grids. The band is bounded on both sides: much
smaller and the per-chunk bookkeeping and the delta's own lists dominate; much
larger and one wall built in a corner re-sends a quarter of the map. It is a
constant rather than a tunable until something needs it to be one.

## Allocation

The per-tick row allocates **7.4 KiB per capture**: one `PawnView` per pawn with
its apparel list, plus the versions to hand back. At 60 Hz that is about
440 KiB/s of Gen0 — small, but not nothing, and it is the number to watch if the
pawn count on one map ever grows by an order of magnitude.

`ThingView` is a `readonly struct` rather than a class for this reason. A full
capture carries 7,570 of them; as a class that would be 7,570 heap objects to
allocate and collect on every re-pull, and as structs in one list it is one
array. The 2,048 KiB a full capture allocates is dominated by the two 40,000-cell
`int[]` grids (320 KiB) and that one thing array, plus the doubling of the lists
they are built in.

## What this model does not track, and what to do about it

A chunk version moves when what is drawn **where** changes. It deliberately does
**not** move when a Thing mutates in place without touching the grids:

- a stack growing from 20 to 40 steel,
- hit points falling,
- a plant's growth advancing.

Those are field writes on a `Thing` with no call into `ThingGrid` to hook, and
adding a notification to each of them would mean touching a dozen systems to
catch a redraw nobody has yet asked for. A host that draws stack numbers, damage
or plant growth stages should re-pull on its own schedule; whatever makes such a
change can call `MapViewTracker.Notify_ChunkChangedAt(cell)` and be picked up on
the next read. **This is the one thing about the model that will surprise
someone**, which is why it is stated here as well as in `MapViewTracker`'s own
class doc.

Also absent, because this port has no such thing: **fog of war** and a **snow
grid**. There is no `FogGrid` and no `SnowGrid` anywhere in `SimWorld.Core`, so
the read model carries neither. Nor is there a **drafted** flag on `Pawn` —
`PawnView` reports `Downed`, `Dead`, `Asleep`, `Moving` and the mental state,
which is what this port actually knows about a person's pose.

And deliberately out of scope: **per-citizen detail beyond what drawing needs.**
`PawnView` has a position, a pose, apparel and a weapon. It has no mood, no
skills, no health breakdown and no relations. Opening every person to paint a
settlement is exactly what spec §11.3's tiering exists to prevent, and a named
citizen is a different, narrower query — the same refusal `God/View` makes one
scale up.

## `ThingDef.size`, and what still assumes 1×1

`docs/host/rendering-method.md` called a missing footprint the third gap. It is
now partly closed, and the rest is written down here rather than half-wired.

**What was actually missing was the parse.** The field
(`ThingDef.size`, an `IntVec2` defaulting to 1×1), `GenAdj.OccupiedRect` with
its rotation rule, and every grid that reads them — `ThingGrid`, `EdificeGrid`,
`Thing.Position`'s multi-cell path, `Thing.SpawnSetup`/`DeSpawn`'s path-cost
sweeps, `RoofCollapseUtility`, `BeautyUtility` — were all already here and all
already correct. What was missing was that **no `IntVec2` parser was
registered**, so `<size>(1,2)</size>` in content did not fail: it fell through
`XmlObjectMapper`'s build-an-object path, found no child elements, and silently
produced a footprint of (0, 0) cells. A def could not state a footprint at all,
which is why nothing in content does.

Landed:

- `IntVec2.FromString` / `IntVec3.FromString`, registered in `ParseHelper` the
  way RimWorld's own does — parentheses optional, culture-invariant, and a bad
  value is a load error rather than a silent default.
- `GenSpawn.Spawn` bounds-checks the **whole footprint** rather than the centre
  cell. Both grids silently skip out-of-bounds cells, so without it a 1×2 bed
  spawned on the last row would register in one cell while its own
  `OccupiedRect` kept claiming two — a Thing the grids half-remember and never
  fully release. With all content 1×1 this changes nothing today.
- `ThingView.OccupiedMin` / `OccupiedSize` on the read model, rotation already
  applied, so the host never reimplements `GenAdj.OccupiedRect` and the two can
  never disagree about where a bed's second cell is.

**Still assumes 1×1, and no content sets a size because of it:**

1. `Building.GenConstruct.CanPlaceBlueprintAt` validates exactly one cell —
   affordance, blueprint/frame collision, edifice collision. A 1×2 bed could be
   placed with its far cell inside a wall.
2. The hand-authored `Blueprint_<X>` and `Frame_<X>` ThingDefs carry no size of
   their own. RimWorld generates those and their footprint follows the building's
   automatically; here they are content, so a sized building would have a
   1×1 frame turn into a 2-cell building on completion.
3. `Building.SettlementConstructionInitiative.TryFindPlacementCell` scans single
   cells. That one is the cheapest of the three, because it already delegates to
   `CanPlaceBlueprintAt` — fix (1) and this follows, which is worth knowing
   before anyone plans the work as three jobs.

Those three have to agree before a buildable def states a footprint, and all
three are in `Building/`, which other lanes were mid-recovery in when this
landed. **A field some systems honour and others ignore is worse than one
nothing honours yet**, so shipped content sets no size and
`ThingSizeTests.Shipped_content_sets_no_footprint_yet` is the tripwire: whoever
first sets a real footprint lands on that test, and its message names the three
things to fix. A bed is 1×2 in RimWorld and that value is knowable; what is not
yet true is that this port could honour it.
