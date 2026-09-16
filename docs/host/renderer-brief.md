# Brief for the Unity renderer

The settlement-interior seam exists now (`SimWorld.Map.View`). This is what to build against it.
Everything below is the shipped API, measured costs included.

## The loop

```csharp
MapViewSnapshot snapshot = MapViewSnapshot.Capture();   // the settlement the god has open
if (!snapshot.HasMap) { Show(snapshot.AbsenceReason); return; }
BuildScene(snapshot);
MapViewVersions held = snapshot.Versions;

// every frame
MapViewDelta delta = MapViewSnapshot.CaptureChanges(held);
if (!delta.HasMap)     { DropScene(); return; }
if (delta.FullResync)  { DropScene(); }
if (delta.Terrain != null) RebuildTerrain(delta.Terrain);
if (delta.Roofs   != null) RebuildRoofs(delta.Roofs);
foreach (MapViewChunk c in delta.ChangedChunks) RebuildChunk(c);
DrawPawns(delta.Pawns);
held = delta.Versions;
```

**Use the parameterless overloads.** They read `God.AttentionManager`'s focus, so the host cannot
develop a second notion of which settlement is selected. There are `(int tile, …)` overloads;
prefer not to. `Capture(Map.Map)` exists for core-internal callers only — the host can never
obtain a `Map`, by design.

## What it costs

Measured on a real `TribalStart` interior: 200×200, 40,000 cells, 7,570 non-pawn things, 29 pawns.

| call | median | allocated |
| --- | --- | --- |
| full `Capture()` | 3.57 ms | 2,048 KiB |
| `CaptureChanges()`, per tick over 600 ticks | **0.023 ms** | 7.4 KiB |

0.023 ms is **0.14% of a 60 Hz frame**. A full capture every frame would be a fifth of the budget,
so do not do that. Over 600 ticks the seam sent 0.02 chunks per tick out of 49.

## Three change rates, three treatments — and the renderer must mirror them

This is the design, and getting it wrong throws the performance away:

- **Terrain and roofs**: one version each, whole-layer. Change rarely. Palette-plus-index —
  `Cells` is one `int[]` of palette indices, row-major `z * SizeX + x`. Build these into a mesh or
  a texture once and rebuild only when the version moves.
- **Things**: chunked 32×32 (49 chunks at 200×200), each independently versioned. Rebuild only
  the chunks in `delta.ChangedChunks`. This is where instancing lives.
- **Pawns**: never chunked, never versioned, **always complete**. Every spawned pawn arrives every
  call. A pawn absent from `delta.Pawns` has left the map or died — retire whatever you drew for
  it. Pawns are deliberately excluded from chunk dirtying, so a pawn walking across the map
  dirties nothing; this is what keeps the per-tick cost flat.

## Traps, each of which will cost a day if discovered the hard way

1. **In-place mutation does not dirty a chunk.** Stack count, hit points and plant growth are
   field writes with no grid call to hook. If you draw stack numbers or growth stages, re-pull
   those on your own schedule — do not expect a delta. `Notify_ChunkChangedAt(cell)` is the
   escape hatch if the simulation side ever needs to force one.
2. **`ThingView` never contains a pawn.** A corpse is an ordinary item and *does* appear.
3. **A multi-cell Thing is emitted once**, at its own `Position`, in one chunk — but
   `OccupiedMin`/`OccupiedSize` may spill into the neighbour. Rebuilding the neighbouring chunk
   alone must not clear geometry the owning chunk placed.
4. **Do not hardcode `"RoofRockThick"`.** `RoofView.IsThickRoof` and `IsNatural` are carried for
   exactly this reason. Getting it wrong draws a solid mountain as open sky, on the settlements
   where roofs matter most.
5. **`ContentLoaded` before anything else.** An unloaded host otherwise gets a valid-looking empty
   snapshot, which reads exactly like a civilization that has not started. The god view was bitten
   by this once already.
6. **`AbsenceReason` is a sentence, not an exception.** No game, no settlement on that tile, and
   no interior yet are three different things to draw.

## Build order

1. **Terrain only**, flat quads, one colour per terrain `defName`. Proves the palette decode and
   the camera.
2. **Things as coloured boxes**, GPU-instanced per `defName`, one draw call per def per chunk.
   Proves the chunking.
3. **Pawns as capsules**, interpolated between ticks using the stable `ThingId`.
4. **The `defName → prefab` registry**, with the primitive as fallback. Nothing before this point
   needs a single asset.
5. **Models replace primitives**, one `defName` at a time, each independently verifiable.

Steps 1–3 should be built and profiled **before any art exists**. That is the whole point of the
primitive fallback: the renderer never waits for the model agent and the model agent never waits
for the renderer.

## Rendering technique

Do not instantiate a GameObject per Thing. A generated interior carries 7,570 things and one
measured map held 12,991 rock. Use `Graphics.RenderMeshInstanced` (or ECS) with one batch per
`defName` per chunk. The chunk boundary is already the natural batch boundary — it is why things
are chunked at all.

Camera is **fixed isometric and never rotates** (`docs/host/rendering-method.md`), which means
back faces and hidden geometry can be culled at author time and walls can be facades.

## Rules that do not bend

- Never reach into `GodManager`; bind to `God/View` and `Map/View`. A test enforces this.
- Never hold a `Def`, a `Thing` or a `Map`. Every handle is a `defName` string, and a reflection
  test over the whole `SimWorld.Map.View` namespace enforces that the seam cannot hand one out.
- Reference the core **relatively** in `Packages/manifest.json`
  (`file:../../simWorld/src/SimWorld.Core`). An absolute path works on exactly one machine.
