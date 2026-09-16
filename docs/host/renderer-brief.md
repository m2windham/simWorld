# Brief for the Unity renderer

The settlement-interior seam exists now (`SimWorld.Map.View`). This is what to build against it.
Everything below is the shipped API, measured costs included.

**Steps 1-3 are built and running** (`simWorld.Host`, `Assets/Scripts/MapRenderer.cs`). What that took,
and the four traps that were not in this brief, are at the bottom under
[What building it took](#what-building-it-took).

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
4. **The `defName → prefab` registry** — **this now exists**: `Assets/Scripts/VisualRegistry.cs`
   in the host repo, with EditMode tests. `Resolve(defName, thingId)` returns a prefab or null;
   null means draw `VisualRegistry.FallbackMesh`. Nothing before this point needs a single asset.

   Two decisions baked into it. **FBX, not glTF** — the model pipeline emits both, Unity imports
   FBX natively and needs a package for `.glb`. And **variants are chosen by `ThingId`, never by a
   roll**: rock ships as `Granite_a`…`Granite_d` because one mesh at ~13,000 instances reads as a
   repeating grid, and a pure function of the id keeps a given rock stable across frames, across a
   save and load, and across machines — the same no-draw-in-a-tick-path discipline the core holds
   itself to.
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

## What building it took

Steps 1-3 landed in one sitting against a live editor. The seam itself needed no changes and no
workarounds, and every number in the table above held. What follows is the part that was not in this
brief, recorded so the next person does not rediscover it.

### It works, and here is the shape of it

A generated `TribalStart` interior, 200x200, rendered isometric: **15 instanced batches, 26 pawns**
(20 citizens carrying a faction, 6 wild animals), world generated in **89 ms** and the interior in
**156 ms**. Terrain decoded exactly: 37,151 `Soil` + 1,303 `SoilRich` + 754 `Gravel` + 792
`WaterShallow` = 40,000 cells, no remainder. All three roof types arrived with their `IsNatural` and
`IsThickRoof` flags set, so nothing had to guess at `RoofRockThick`.

Terrain and roofs are **textures, not meshes** — one `Texture2D` per layer, point-filtered, on a
single double-sided quad. The brief allowed either; the texture is strictly better here, because a
whole-layer rebuild is a `SetPixels32` rather than 160,000 vertices, and it is one draw call at any
map size.

### The four traps

1. **`TerrainLayer` is ambiguous.** `UnityEngine.TerrainLayer` is a real type and
   `com.unity.modules.terrain` is in the default manifest, so a bare `TerrainLayer` fails with CS0104
   rather than with anything that points at the cause. Same family as the `Map.Map` and `World.World`
   shadowing the core's `CLAUDE.md` already warns about. Alias it:
   `using ViewTerrainLayer = SimWorld.Map.View.TerrainLayer;`.

2. **Unity does not compile the core the way CI does.** `Directory.Build.props` sets
   `<Nullable>enable</Nullable>` repo-wide, but Unity compiles `SimWorld.Core` as a package with its
   own defaults, so every `?` annotation in the core raises CS8632 in the host's console — **49
   warnings on a clean tree**, with a real error sitting underneath them. Fixed by
   `src/SimWorld.Core/csc.rsp` carrying `-nullable:enable`. Unity reads that file from the folder
   holding the `.asmdef` and applies it to that assembly alone; MSBuild does not read it at all, so
   the dotnet build and CI are untouched.

   **`csc.rsp` does not support comments.** A `#` line is not stripped, it is tokenised and handed to
   the compiler, which then reports `CS2001: Source file '?' could not be found` because the prose
   contained a question mark. Keep the file to flags only.

3. **A World Space Canvas is a HUD only by accident.** The god view's Canvas was `World Space`, which
   looked like a screen overlay for exactly as long as the camera was the default one pointing at it.
   The moment an isometric camera existed, the UI lay down flat in the world and skewed along the map
   axes. `Screen Space - Overlay` is what it wanted.

4. **A roof drawn at its real height lands in the wrong place.** Under an orthographic camera a plane
   at height h is displaced in screen space by a constant offset; at 30 degrees of pitch a roof at
   head height sits almost four cells from the wall it belongs to. Registering the overlay with the
   terrain beats being at the right altitude, so roofs render flat just above the floor. Tall geometry
   then depth-occludes them, which reads correctly anyway.

### One thing the seam could carry, and does not

`TerrainLayer.Palette` and `ThingView.DefName` are bare defNames: no colour, no category. A host with
no art has to invent an appearance from a string, and the honest version of that is a hash — stable
and distinct, but meaningless, which paints shallow water olive and soil pink. The host currently
hashes and then nudges the hue toward whatever family the *name* admits to (`water`, `soil`, `stone`,
`plant`). That nudge is the soft form of the mistake trap 4 above warns about: a content pack whose
stone def is not spelled "stone" simply falls through to the hash.

**A category per palette entry would remove the guess entirely**, and it is values rather than
references, so it costs the seam nothing structurally. Proposed here rather than faked convincingly on
the host side; until it exists, `DefColors.cs` is deliberately one small file that can be deleted
whole.

### Still open

- **Step 5** — models replace primitives. `VisualRegistry` already resolves a prefab's mesh, so a
  defName gains a model without `MapRenderer` changing. What it does not yet do is take the prefab's
  own *material*, which is the next increment.
- **Profiling.** This brief asks for steps 1-3 to be profiled before art. The renderer has been run
  and watched, not measured: no frame timings are recorded yet.
