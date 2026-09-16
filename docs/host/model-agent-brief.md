# Brief for the 3D model pipeline

Target: SimWorld's settlement-interior renderer. Written against the **actual** pipeline at
`A:\dev\simWorld.Model` (Blender 4.5 LTS, AMD HIP, `run_pipeline.py`), verified by reading it —
not against assumptions.

## The interface

```powershell
python run_pipeline.py --prompt "<description>" --name <defName> --archetype <archetype> --dimensions W D H
```

| Flag | Use here |
| --- | --- |
| `--prompt` | Required. Plain description; drives concept generation. |
| `--name` | **Set this to the exact `defName`.** The host maps `defName → mesh`, so `--name Granite` produces `Granite.glb`. Without it the pipeline invents a name and the mapping breaks. |
| `--archetype` | One of the six below. `auto` lets it guess — don't, be explicit. |
| `--dimensions` | **W D H in metres, and 1 metre = 1 grid cell.** A 1×1 cell wall 2.5 m tall is `--dimensions 1.0 1.0 2.5`. |
| `--thickness` | Wall thickness for modular architecture. Default 0.2. |
| `--poly_budget` | Default 3500. Lower it for anything that instances heavily. |
| `--skip_renders` | Skips the 5-pass turntable. Use for bulk runs; drop it when a shape needs eyeballing. |

Output lands in `output/models/` as `<name>.glb`, `<name>.fbx` and `<name>_manifest.json`, with
ORM and dual (GL/DX) normal maps under `output/models/textures/`. **Unity wants
`_Normal_GL.png`** — GL is the +Y convention Unity uses; `_Normal_DX.png` is for Unreal.

The manifest the pipeline already writes carries `asset_name`, `archetype`, `dimensions`,
`vertex_count`, `polygon_count` and every texture and render path. That is better than the format
this brief previously invented, so **use the pipeline's own manifest** — nothing extra needed.

## What the pipeline can build today, and what it cannot

Six archetypes are registered:

`prop_crate` · `prop_cylinder` · `prop_medieval_home` · `modular_wall` · `modular_floor` ·
`modular_column`

Note `modular_column` is registered in `generator_registry` but **is not in `run_pipeline.py`'s
`--archetype` choices**, so the CLI will reject it until that list is updated. One-line fix.

Every one of these is **hard-surface architectural**. That is the constraint that reorders
everything below.

### These need generators that do not exist yet

- **Rock** (14 defs, the single most-instanced thing in the game) — irregular natural masses. No
  archetype produces them. `modular_wall` would give cuboid blocks, which is wrong for a mined
  cave face.
- **Plants** (4 defs) — organic, needs growth stages.
- **Human** (rigged, plus 6 apparel attachments) — a skeletal rig is a different pipeline entirely.
- **Animals** (3 defs) — same.

**This reverses the batch order in the previous version of this brief.** Rock was listed first
because it is the highest-value asset in the game; it is now last-but-one because the pipeline
cannot make it. Writing a `natural_rock` generator is the highest-value *pipeline* task, and it is
Python work in `pipeline/generators/`, not a prompt.

## Batch 1 — what is deliverable right now

All 1×1 cells. Heights are a judgement call; these read correctly at a fixed isometric camera.

```powershell
python run_pipeline.py --name Wall            --archetype modular_wall  --dimensions 1.0 1.0 2.5 --poly_budget 400  --prompt "Rough tribal timber-and-daub wall segment"
python run_pipeline.py --name WallGranite     --archetype modular_wall  --dimensions 1.0 1.0 2.5 --poly_budget 400  --prompt "Dry-stacked grey granite block wall segment"
python run_pipeline.py --name WallSandstone   --archetype modular_wall  --dimensions 1.0 1.0 2.5 --poly_budget 400  --prompt "Dry-stacked warm sandstone block wall segment"
python run_pipeline.py --name WallLimestone   --archetype modular_wall  --dimensions 1.0 1.0 2.5 --poly_budget 400  --prompt "Dry-stacked pale limestone block wall segment"
python run_pipeline.py --name Door            --archetype modular_wall  --dimensions 1.0 1.0 2.5 --poly_budget 500  --prompt "Simple hinged timber door in a stone frame"
python run_pipeline.py --name StorageHut      --archetype prop_medieval_home --dimensions 1.0 1.0 2.0 --poly_budget 1200 --prompt "Small thatched storage hut, tribal"
```

Six assets, every one on an existing archetype. This is enough to draw a settlement's built
structures, which is the half of the scene that is not rock.

## Batch 2 — benches and containers

`prop_crate` and `prop_cylinder` cover these; all 1×1, roughly waist height.

`FueledStove` · `TableButcher` · `TableStonecutter` · `Smithy` · `TableTailor` · `ResearchBench`
· `Battery` · `WoodFiredGenerator` · `Heater`

## Batch 3 — floors and terrain

`modular_floor`, `--dimensions 1.0 1.0 0.05`. Sixteen terrain types, but they are materials on a
flat grid more than meshes — **check with the renderer before generating all sixteen**, since a
tinted shared quad may serve better than sixteen meshes.

## Batch 4 — the rock generator (pipeline work, not prompts)

Write `pipeline/generators/natural_rock.py` registering `natural_rock`. Requirements:

- Irregular cell-filling mass, **3–4 silhouette variants per type** (`Granite_a`, `Granite_b`, …).
  At 12,991 instances on one measured map, a single mesh reads as an obvious repeating grid.
- **`--poly_budget` 300 or lower.** This is the budget that matters most in the entire project.
- Ore types are the base rock with a visible mineral seam in the ore's colour.

Then the fourteen: `Granite` `Sandstone` `Limestone` `MineableSteel` `MineableFlint`
`MineableSalt` `MineableCoal` `MineableCopper` `MineableTin` `MineableSilver` `MineableGold`
`MineableJade` `MineableUranium` `MineablePlasteel`

## Batch 5 — organics

`Human` (rigged) + 6 apparel, then `Plant_Berry` `WildPlant` `Plant_Potato` `Plant_Rice`, then
`Husky` `Muffalo` `Chicken`. All need new generators and a rig; largest piece of work here.

## On art direction

Earlier guidance here said "no PBR, no normal maps, flat materials". **That was written without
knowing the pipeline, and it fights it** — the refinery's whole purpose is ORM plus dual normals,
and stripping that means rewriting the refinery to gain nothing.

Take the PBR output as-is. The fixed isometric camera and the low poly budgets are what deliver
the stylized read; the maps cost nothing at runtime and leave the assets engine-ready if the
camera ever changes. **The budget is the lever, not the material.**

## What not to build

- **Blueprints and frames.** 34 `Blueprint_*` / `Frame_*` defs are the under-construction states
  of buildings already listed — a shader treatment of the real model, not separate art.
- **`Bed`.** It is 1×2 in RimWorld, and this port cannot honour a footprint yet:
  `GenConstruct.CanPlaceBlueprintAt` validates one cell and the `Blueprint_`/`Frame_` defs carry
  no size. Author it 1×1 and expect one revision, or wait.
- Anything not named by a `defName` in the shipped content.
