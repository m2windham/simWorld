# Brief for the 3D model agent

Target project: SimWorld — a civilization-scale god-game with RimWorld's simulation depth.
Unity host, grid-based settlement interiors. This brief is authored from the shipped content, so
every `defName` below is real and spelled exactly as the game will ask for it.

## Art direction

**Stylized low-poly, fixed isometric camera.**

- Clean readable silhouettes. Flat or simple gradient materials. **No PBR, no normal maps, no
  roughness/metallic maps.** Vertex colour or a single flat material per asset.
- The camera is **locked to a fixed isometric angle and never rotates.** A model only has to read
  from that one angle: omit back faces, omit hidden geometry, walls may be facades rather than
  solids. Do not spend polygons on what is never seen.
- Readability over detail. These are read at a glance among thousands of other objects, not
  inspected.

## Technical specification

| Property | Value |
| --- | --- |
| Scale | **1 grid cell = 1 Unity unit.** Everything is authored to this. |
| Pivot | Cell centre, base sitting at **y = 0** |
| Axes | Y-up, −Z forward (Unity convention) |
| Format | `.glb` preferred, `.fbx` accepted |
| Filename | **exactly `<defName>.glb`** — e.g. `Granite.glb`, `WallSandstone.glb`. The host maps `defName → mesh` by this name, so spelling is load-bearing. |
| Materials | One per asset. No texture maps; colour in the material or in vertex colours. |
| Footprint | Assume **1×1 cell** unless this brief says otherwise. |

### Polygon budgets

These are driven by instance counts measured in a real generated map, not guessed:

- **Rock: 100–300 tris.** A single generated interior held **12,991 rock instances**. This is the
  budget that matters most in the whole project.
- **Buildings: 300–1,000 tris.**
- **Plants: 50–200 tris.**
- **Animals: 500–1,500 tris.**
- **Human: 2,000–3,000 tris, rigged.** One humanoid rig; apparel attaches to it.
- **Items: 50–200 tris.**

### Variants

Rock needs **3–4 silhouette variants per type**, named `Granite_a.glb`, `Granite_b.glb`, and so
on. At thirteen thousand instances a single mesh reads as an obvious repeating grid. Variants
should differ in silhouette, not just rotation — rotation is free at runtime.

## Where output goes

Write to the model repo's own staging directory, one folder per batch:

```
A:\dev\simWorld.Model\out\01-rock\Granite_a.glb
A:\dev\simWorld.Model\out\02-people\Human.glb
```

These are then imported into the Unity host, which owns all engine assets:

```
A:\dev\simWorld.Host\Assets\Models\<defName>.glb
```

Assets do **not** go in the `simWorld` core repo. That repo is engine-free by rule and the two
must never share a file path — it is why the host is a separate repository at all.

### Write a manifest alongside each batch

In every batch folder, write `manifest.json`:

```json
{
  "batch": "01-rock",
  "generated": "2026-09-16",
  "assets": [
    { "defName": "Granite_a", "file": "Granite_a.glb", "tris": 184,
      "boundsX": 1.0, "boundsY": 1.0, "boundsZ": 1.0, "materials": 1, "bytes": 14208 }
  ]
}
```

This is how the work gets checked without anyone opening a mesh: the manifest can be read against
the asset list and the polygon budgets in this brief, so a `Granite` variant at 4,000 tris or a
model whose bounds are not 1×1×1 is caught immediately rather than at import. Paste or commit the
manifest; the binaries do not need to travel.

## Batch 1 — the rock (do this first)

Fourteen types. These are mineable stone and ore walls: solid, cell-filling, chunky. Ore types
should read as the base rock with a visible mineral seam in the ore's own colour.

Natural stone: `Granite` · `Sandstone` · `Limestone` · `MineableSteel`

Ore: `MineableFlint` · `MineableSalt` · `MineableCoal` · `MineableCopper` · `MineableTin` ·
`MineableSilver` · `MineableGold` · `MineableJade` · `MineableUranium` · `MineablePlasteel`

3–4 variants each → roughly 45–55 meshes. **This is the single highest-value batch in the
project**; a settlement is carved out of this material and it is most of what is on screen.

## Batch 2 — people

- `Human` — one rigged humanoid. Stylized, readable at isometric distance, neutral proportions.
  Needs a basic locomotion-ready rig (idle, walk, carry, work). Apparel attaches to it.
- Apparel, 6 pieces, authored as attachments to that rig: check
  `src/SimWorld.Core/Data/Core/Defs/ThingDefs_Apparel/Apparel_Basic.xml` for exact `defName`s.

## Batch 3 — the first buildings

`Wall` · `Door` · `Bed` · `StorageHut` · `FueledStove`

**Note on `Bed`, updated.** It is 1×2 in RimWorld. `ThingDef.size` now parses correctly — until
recently no `IntVec2` parser was registered, so a declared `<size>(1,2)</size>` silently became
**(0,0)**, a footprint of no cells with no error anywhere. That is fixed, and the occupancy code
that reads it was already correct.

It still cannot be *used*: `GenConstruct.CanPlaceBlueprintAt` validates a single cell, and the
hand-authored `Blueprint_*`/`Frame_*` defs carry no size of their own, so a 1×2 bed would be
built from a 1×1 frame. **So `Bed` stays 1×1 for now — author it that way, and expect one
revision when those two gaps close.** Everything else in this brief is genuinely 1×1 and will
not change.

## Batch 4 — growth

`WildPlant` · `Plant_Berry` · `Plant_Potato` · `Plant_Rice`

Plants need growth stages if cheap — the simulation tracks growth and harvestability, so a
visibly unripe versus ripe crop is real information, not decoration.

## Batch 5 — animals

`Husky` · `Muffalo` · `Chicken`

## Later batches

Remaining buildings (stonework walls, workbenches, power, security, sculptures), 34 items,
5 weapons, 16 terrain materials. Full counts in `docs/host/rendering-method.md`.

## What not to build

- **Blueprints and frames.** There are 34 `Blueprint_*` and `Frame_*` defs. These are the
  under-construction states of buildings the list above already covers, and they are a **shader
  treatment of the real model** — translucent ghost, then a partial scaffold. No separate art.
- **Terrain.** The 16 terrain types are materials on a flat grid, not meshes.
- Anything not named by a `defName` in the shipped content.
