# The graphical side: method, and what blocks it

Written for the Unity host (`simWorld.Host`) and for whoever generates assets. Everything
below was verified against this repo at `cb4e624`, not assumed.

## There are two graphical surfaces, and only one can be built today

**The god view, at civilization scale.** `God/View/GodViewSnapshot.Capture()` already returns
everything it needs: the civilization rollup (population by tier, mean mood, health, food,
industry skill, era and progress), every settlement (name, tile, founding tick, populations,
whether an interior exists), every edict with its availability *and the reason for a refusal*,
and the chronicle tail with curated moments. The host has this loop proven live. **It needs no
3D models at all** — it is a world map and panels.

**The settlement interior, at RimWorld scale.** This is where models live. It is **blocked**,
and the block is deliberate. Spec §12a, on what the snapshot omits:

> map or rendering data (a settlement reports only whether its interior exists yet)

`SettlementSummary` carries `HasInteriorMap` and nothing else about the map. **The host cannot
ask what is standing on a map, where it is, or who is walking around on it.** No amount of art
changes that. The first thing to build is the read model.

## Three gaps, in the order they bite

### 1. There is no interior read model

`God/View` is the pattern to copy, and its discipline is the reason it works (spec §12a): values
not live references, because a live reference is a write surface, it tears across frames while
the sim ticks, and it turns every internal rename into a host break. Every handle is a
`defName`, so the host cannot hold a `Def` and reach a worker through it. A test enforces that
structurally.

What an interior view needs is the same shape one scale down: terrain per cell, things with
their cell and `defName`, pawns with position, and enough per-pawn state to draw them. It is a
new seam — `Map/View` — not a widening of `God/View`, because the two answer different questions
at different rates: a civilization rollup is cheap and slow, a 200×200 map is 40,000 cells and
must not be rebuilt wholesale every frame.

**Cost is the design constraint, and it is measured.** A generated `TribalStart` interior held
**12,991 granite** and 822 wild plants at tick 0. A snapshot that allocates per thing per frame
is dead on arrival; so is a GameObject per thing. The read model wants to be a dirty-region or
versioned-chunk design, and the renderer wants GPU instancing.

### 2. Content carries no visual identity, and that is correct

There is no `texPath`, no `graphicData`, no `graphicClass` anywhere in `Data/Core/Defs` — zero
hits. The core is engine-free by rule and must stay that way.

So the `defName → mesh` mapping belongs to **the host**, as a registry keyed by `defName`. That
preserves the rule, and it matches the seam's existing discipline: every handle the host already
holds is a `defName`. A missing entry should fall back to a labelled primitive rather than
throwing — that is what lets the renderer ship before the art does.

### 3. Nothing has a footprint

No `<size>` on any building def. Every building in this port is implicitly 1×1.

RimWorld has `ThingDef.size` (an `IntVec2`), so this is a **porting gap, not a design choice**,
and it is far cheaper to close now than after assets exist: a bed is 1×2 in RimWorld and a model
authored 1×1 has to be rebuilt. Close it in the core before the model agent commits to
proportions.

## Build order

The point of this order is that **the renderer never waits for art and the art never waits for
the renderer.**

1. **`Map/View` read model** — core, this repo. Nothing renders until this exists.
2. **Add `ThingDef.size`** — core, small, and it unblocks correct proportions.
3. **Host renderer against the snapshot, drawing primitives** — a coloured box per building
   footprint, a capsule per pawn, a quad per terrain cell. This proves the whole pipeline with
   zero assets and is where the instancing and camera work get done.
4. **Visual registry** — `defName → prefab`, with primitive fallback.
5. **Models replace primitives, one `defName` at a time.** Each drop-in is independently
   testable and nothing regresses if an asset is late.

## Art direction, decided

**Stylized low-poly, fixed isometric camera.**

- Clean readable silhouettes, flat or simple gradient materials, no PBR detail, no normal maps.
- The camera is locked to a fixed isometric angle — RimWorld's readability, in 3D.
- Because the camera never rotates, a model only has to read from one angle. Back faces and
  hidden sides can be omitted, walls can be facades rather than solids, and interiors need no
  occlusion solution.
- This is chosen for the instancing budget as much as the look: 12,991 rock instances on one map
  is comfortable with flat-shaded low-poly and one material per rock type, and is a fight with
  full PBR material sets.

Every asset in the manifest below is authored to that target.

## Asset manifest

Counts are exact as of `cb4e624`.

| Group | Count | Notes |
| --- | --- | --- |
| Buildings (real) | 38 | excludes 34 Blueprint/Frame defs |
| Blueprint / Frame | 34 | **not separate art** — a shader treatment of the real model |
| Rock (mineable + natural) | 14 | the 12,991-instance case; highest render priority |
| Plants | 4 | `Plant_Berry`, `Plant_Potato`, `Plant_Rice`, `WildPlant` |
| Creatures | 4 | `Human` (rigged), `Husky`, `Muffalo`, `Chicken` |
| Items | 34 | resources, minerals, food, stone blocks, drugs, prosthetics |
| Apparel | 6 | attaches to the human rig |
| Weapons | 5 | 2 melee, 3 ranged |
| Terrain | 16 | materials, not meshes |
| Filth / projectiles | 6 | decals and tiny meshes |

### What a first playable scene actually contains

A `TribalStart` interior is overwhelmingly rock, soil, wild growth and people. **About twenty
assets** make the first scene read correctly:

- **Rock**: `Granite`, `Sandstone`, `Limestone`, `MineableSteel`, `MineableCoal`, `MineableFlint`
- **Terrain**: `Soil`, `SoilRich`, `Gravel`, `Sand`, `WaterShallow`
- **Growth**: `WildPlant`, `Plant_Berry`
- **People**: `Human` + the 6 apparel pieces
- **First buildings**: `Wall`, `Door`, `Bed`, `StorageHut`, `FueledStove`

Everything else can stay a primitive without the scene looking broken.

### Ordering for the model agent

1. The 14 rock variants — most instanced thing in the game, and stone walls are what a
   settlement is made of.
2. `Human`, rigged, plus the 6 apparel attachments.
3. The five first-buildings above.
4. `Plant_Berry`, `WildPlant`, then the two crops.
5. The three animals.
6. Items, weapons, remaining buildings, sculptures.

## What the host must not do

- Never reach into `GodManager`; bind to `God/View`. A test enforces this structurally.
- Never hold a `Def`. Every handle is a `defName`.
- Never carry its own idea of which settlement is selected — `GodCommands.FocusSettlement(tile)`
  writes it and `GodViewSnapshot.FocusedSettlementTile` reads it back. Two disagreeing notions of
  what the player is looking at is a bug that takes a week to find.
- Reference the core **relatively** in `Packages/manifest.json`
  (`file:../../simWorld/src/SimWorld.Core`). An absolute path works on exactly one machine.
