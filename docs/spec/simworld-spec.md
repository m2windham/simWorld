# SimWorld — Technical Spec / Outline

Civilization-scale god-game on RimWorld-depth simulation. Every citizen is a full
agent. Phase 1 ports RimWorld's systems 1:1 as isolated tested modules
(see `docs/research/rimworld-mechanics.md`); the god layer comes after.

This document describes the architecture **as built**. `docs/status.json` is the
source of truth for per-system progress; the Blueprint tracker renders it.
Sections marked _planned_ have no code yet.

## 1. Pillars

- Emergent story from simulation, not scripted narrative.
- Data-driven content: designers add content via XML Defs, not code.
- Deterministic simulation: same seed + same inputs → same outcome.
- 1:1 RimWorld mechanics first, translated to civilization scale second.
- Engine-free core: no `UnityEngine` reference anywhere in `SimWorld.Core`.

## 2. Layered Architecture

```mermaid
flowchart TB
  subgraph L0[Layer 0 — Data]
    Defs[XML Defs + DefDatabase]
  end
  subgraph L1[Layer 1 — Simulation Core]
    Tick[TickManager + TickList buckets]
    Rand[Seeded RandomStream]
    Scribe[Scribe save/load]
  end
  subgraph L2[Layer 2 — World & Generation]
    WorldGen[World generation]
    MapGen[Map core and generation]
    PawnGen[Pawn generation]
  end
  subgraph L3[Layer 3 — Actor Simulation]
    Needs[Needs and Mood]
    Health[Health and Capacities]
    Skills[Skills and Work]
    AI[AI: think tree, jobs, pathing]
    Social[Social: opinion, relations, interactions]
  end
  subgraph L4[Layer 4 — World Simulation]
    Economy[Crafting, trade, factions]
    Building[Construction, power, climate]
    Combat[Combat]
    Belief[Belief: ideology, precepts, rituals]
  end
  subgraph L5[Layer 5 — Director]
    Director[Threat director and Chronicle]
    Quests[Quests and scenario]
  end
  subgraph L6[Layer 6 — God and Presentation]
    God[God layer: edicts, eras, civ view]
    Render[Unity host: render and UI]
  end

  L0 --> L1 --> L2 --> L3 --> L4 --> L5 --> L6
```

- Dependencies point downward only. A module never references a layer above it.
  Social sits in Layer 3 rather than beside belief in Layer 4, and that placement
  is deliberate: opinion, relations and interactions are per-pawn state of
  exactly the same kind as needs and mood, which is why `Pawn_RelationsTracker`
  can own them without reaching upward. Belief — ideology, precepts, rituals — is
  genuinely world-scale and stays in Layer 4 for the god layer to own. An earlier
  draft lumped the two together as one Layer 4 box, which made the perfectly
  correct `Pawns -> Social` reference look like a violation of this rule.
- Layer 6 is one-way: the host reads simulation state and never mutates it, so
  the core stays headless-testable.

## 3. Data Layer (Defs)

RimWorld's Def system, ported whole. Content is XML under
`src/SimWorld.Core/Data/Core/Defs/<DefTypeName>s/`; the element name selects the
Def class.

```xml
<HediffDef ParentName="DiseaseBase">
  <defName>Flu</defName>
  <label>flu</label>
  <lethalSeverity>1</lethalSeverity>
  <comps>
    <li Class="SimWorld.Health.HediffCompProperties_Immunizable">
      <immunityPerDaySick>0.7</immunityPerDaySick>
      <severityPerDayNotImmune>0.5</severityPerDayNotImmune>
    </li>
  </comps>
</HediffDef>
```

- **Inheritance**: `Name` / `ParentName` / `Abstract="True"`, with
  `Inherit="False"` and list append.
- **Polymorphism**: `Class="…"` on a list item picks the concrete type;
  `Type`-valued fields (`workerClass`, `hediffClass`) resolve by full name.
- **Cross-references** are deferred: bare `defName` strings resolve after every
  file loads, so files may reference each other in any order.
- **Comps**: composition over inheritance — a properties object per comp in XML,
  paired with a runtime comp class.
- **DefOf**: `[DefOf]` static classes bind named Defs to fields at load; a
  missing Def is a load error, which the content test asserts is empty.
- Lifecycle: `PostLoad` → cross-refs → `ResolveReferences` → `ConfigErrors`.
- Duplicate `defName`: later pack wins (mod load-order semantics).

_Planned_: mod content packs and XPath `PatchOperation`s.

```mermaid
flowchart LR
  Xml[XML Def file] -->|reflection map| Def[Def instance]
  Def -->|registered| DB[DefDatabase typed registry]
  DB -->|binds| DefOf[DefOf static fields]
  Def -->|Class= items| Comps[CompProperties list]
  Comps -->|paired at runtime| Comp[Comp instance on the thing]
```

## 3a. Stat Pipeline

RimWorld's stat resolution, ported whole (`src/SimWorld.Core/Stats/`). `ThingDef.GetStatValueAbstract` —
§3's bare `statBases` lookup — stays for the handful of callers with no pawn, trait, hediff or capacity
concerns (`BaseMaxHitPoints`, `BaseMarketValue`); everything that reads a pawn's health, traits or hediffs
goes through this pipeline instead.

- `StatRequest` addresses either a live `Thing`, or an abstract `(ThingDef, ThingDef? stuff)` pair with no
  Thing at all. RimWorld addresses a `BuildableDef` — the base of `ThingDef` and `TerrainDef` — but this port
  has no such base yet and `TerrainDef` carries no stats, so the abstract mode narrows to `ThingDef`.
- `StatWorker.GetValue` runs `GetValueUnfinalized` (base value → pawn offsets → pawn and stuff factors →
  capacity factors) then `FinalizeValue` (stat parts → post-process curve → min/max clamp).
- `StatDef.capacityFactors` reads the Health module's `PawnCapacityDef` levels — the joint that lets a
  wounded pawn's stats degrade and recover with the body. `RestRateMultiplier`'s BloodPumping, Metabolism and
  Breathing (weight 0.3 each) are the shipped example: hurt the pawn's heart and it rests slower; heal it and
  the multiplier recovers.
- `StatPart` (`TransformValue(StatRequest, ref float)`) is real and tested but ships no concrete subclass yet.
  RimWorld's own quality and stuff-derived StatParts need a live `CompQuality`/apparel-stuff on a spawned
  Thing; Crafting's `QualityCategory` exists only on `ItemStack` today, so those parts would read nothing.
- `Thing.GetStatValue(stat)` and `ThingDef.GetStatValue(stat, stuff)` are the call-site sugar (`StatExtension`),
  reading like RimWorld's `GetStatValue`/`GetStatValueAbstract` — named identically on the Thing side, but the
  def-side overload keeps the `GetStatValue` name (rather than RimWorld's `GetStatValueAbstract`) so it cannot
  collide with the pre-existing `ThingDef.GetStatValueAbstract` above.

```mermaid
flowchart TD
  Base["Base value: statBases entry, else defaultBaseValue"] --> Off1["+ trait statOffsets"]
  Off1 --> Off2["+ hediff-stage statOffsets"]
  Off2 --> Fac1["x trait statFactors"]
  Fac1 --> Fac2["x hediff-stage statFactors"]
  Fac2 --> Stuff["x stuff statFactors, then + stuff statOffsets"]
  Stuff --> Cap["Capacity factors: lerp(value, value x factor, weight) per PawnCapacityDef"]
  Cap --> Parts["Stat parts: TransformValue"]
  Parts --> Curve["Post-process curve"]
  Curve --> Clamp["Clamp to minValue / maxValue"]
```

## 4. Simulation Core

- Fixed tick: 60 ticks/second, decoupled from frame rate. `TimeSpeed`
  multipliers 1/3/6/15; pause is multiplier 0. A per-frame cap drops backlog
  rather than spiralling.
- **Tick buckets**: `TickList` per cadence — normal, rare (250 ticks), long
  (2000 ticks) — with deferred register/deregister so a tick may spawn or
  destroy. Per-object work is additionally spread by hash interval.
- Tick order: pre-tickers (world, map) → normal → rare → long → post-tickers.
- **Randomness**: MurmurHash-based `RandomStream`, one per system, plus a
  thread-static `Rand` facade keeping RimWorld's call shape (`Rand.Value`,
  `Rand.MTBEventOccurs`). Thread-static, so parallel tests never share a
  sequence.
- **Calendar**: `GenDate` — 60,000-tick days, quadrums, years, longitude-local
  time, latitude seasons.
- **Save/load**: `Scribe`, a reflection serializer over `IExposable`, in three
  passes — `LoadingVars` → `ResolvingCrossRefs` → `PostLoadInit`. Handles
  polymorphic deep saves (`Class=`), forward and cyclic references, and
  collections.
- **Services**: `Find.TickManager`, `Find.ResearchManager`, `Find.Storyteller`
  are thread-static, matching RimWorld's global-service shape without sharing
  state across tests.

_Note_: this is an object graph with trackers, not an ECS component store. The
port follows RimWorld's real architecture; a data-oriented rewrite would break
1:1 fidelity and is not planned for phase 1.

```mermaid
sequenceDiagram
  participant TM as TickManager
  participant Pre as Pre-tickers
  participant N as Normal tick list
  participant R as Rare (250)
  participant L as Long (2000)
  loop each tick
    TM->>Pre: world and map
    TM->>N: pawns, projectiles, jobs
    TM->>R: needs decay, mood
    TM->>L: growth, disease, research
  end
```

## 5. World & Generation Layer

- **World gen**: seeded noise (Perlin, ridged multifractal) → elevation
  calibrated to a target land fraction → hilliness, temperature by latitude and
  elevation, rainfall, swampiness → biome workers score each tile → rivers flow
  downhill → factions and settlements placed → roads pathed between them.
- Tiles live on a subdivided icosahedron (10·4ⁿ+2 tiles, 5 or 6 neighbours).
- Saves store the seed and world objects; the grid regenerates on load.
- **Pawn gen**: backstory pair → trait roll (exclusion-aware) → skill and
  passion roll → name → age and life stage. Life stages scale body size, health
  and hunger; newborns record a life event, the seed of lineage-driven
  generation.
- **Map gen**: elevation/fertility noise → terrain by biome, fertility and
  rainfall, plus a carved river channel where the tile carries one → rocky
  outcrops and mountains as natural edifices, scaled by hilliness and the
  tile's Stone/Ore deposits → caves cut through mountain by a directional
  random walk → roofs over mountain and cave cells → chunks and wild plants
  scattered by biome density. `MapGen.MapGenerator.GenerateMapFor(worldTile)`
  is the §11.2 seam: a settlement's interior is generated from the world tile
  it sits on, so a tile the world map promised ore or a river on produces a
  map with ore or a river crossing it.
- Every generation step takes an explicit seed → reproducible.

```mermaid
flowchart TD
  Seed[World seed] --> Noise[Noise fields]
  Noise --> Terrain[Elevation, temperature, rainfall]
  Terrain --> Biomes[Biome workers score tiles]
  Biomes --> Factions[Factions and settlements]
  Factions --> Roads[Roads by cheapest path]
  Terrain --> Rivers[Rivers flow downhill]
```

## 5a. Map Core & Thing Runtime

The object layer every later system stands on, ported from RimWorld's `Thing`.

- **Geometry**: `IntVec3`/`IntVec2`, `Rot4`, `CellRect`, adjacency and radial
  cell patterns, cell/index conversion.
- **Things**: `Thing` → `ThingWithComps` → `Pawn`. A Thing owns its def,
  identity, position, rotation, stack count and hit points, and ticks through
  the same buckets as everything else. `ThingComp` is the runtime half of the
  Comp pattern whose data half §3 describes.
- **Grids** per map: terrain, roof, things by cell, edifices, and a path-cost
  grid recomputed when terrain or occupancy changes.
- **Listers**: `ListerThings` by def and group, `MapPawns` per map.
- **Save/load**: run-length terrain and roof grids plus a polymorphic list of
  spawned Things, re-spawned at their saved positions on load.

```mermaid
flowchart TD
  Def[ThingDef] -->|ThingMaker| Thing
  Thing --> WithComps[ThingWithComps]
  WithComps --> Pawn
  Thing -->|SpawnSetup| Map
  Map --> Grids[Terrain, roof, thing, edifice grids]
  Grids --> Path[Path cost grid]
  Map --> Listers[ListerThings, MapPawns]
```

## 5b. Regions, Sites & Settlement Founding — _planned_

**Design, not built.** A game opens with a two-stage choice modelled on Manor
Lords and Nova Roma: pick a region on the world map, then place the settlement
inside that region against markers showing what is actually there.

### 5b.1 Stage one — the world map, by region

A raw icosphere tile is a lottery ticket, not a place, so the first choice is
made at a coarser grain. A **region** is a contiguous group of tiles with an
identity: a name, a dominant biome, a climate, and a summarised resource
profile.

- The partition floods out from seed tiles and is cut on natural boundaries —
  ridge lines, major rivers, and the coast — so region edges fall roughly where
  real frontiers fall, rather than on an arbitrary grid. Growth never crosses
  water at all, so the coast is expressed as a cost between two _land_ tiles
  that disagree about whether they touch the sea: a shoreline tile and an inland
  one belong to different places.
- At this stage the player reads climate, terrain character, a coarse resource
  profile and what lies adjacent. Not exact deposits: a region promises a _kind_
  of place, and the specifics are stage two.

### 5b.2 Stage two — the site, by what is visibly there

Inside the chosen region the map shows what a scouting party would see: markers
for fresh water, arable soil, timber, stone, clay, flint, ore, salt, game,
fords and defensible high ground. The player places the settlement centre
against them.

- Deposits are **derived from the terrain that already exists**, not sprinkled
  at random: clay in floodplains and river bends, flint in chalk lowland, ore in
  hills and mountains, salt at coasts and springs, deep soil in valleys and on
  floodplains, timber from the biome. A player who learns to read the land is
  reading something real.
- Site scoring still exists — hard necessities times weighted advantages — but
  for the player's own founding it is **advisory**: it shades the markers rather
  than deciding for them. The same score is what emergent and NPC foundings use,
  and there it is decisive.
- **The necessities are era-independent and the advantages are not.** A
  necessity is a flat gate: fresh water in reach, _some_ food source in reach
  (arable soil, game or timber), a survivable climate. No people of any era
  settle where there is no water. Which food source a civilization prefers, and
  what else it values, is era-dependent — and that belongs entirely to the
  advantage weights. (An earlier draft made "land that feeds the group at this
  era's technology" a necessity, which contradicted the split in the same
  breath and would have needed a farming-technology model that does not exist.)
- The advantage weights are where the historical accuracy lives, and they are
  keyed to the eras that actually exist on the authored ladder — Sticks & Stones
  through Exotic — rather than to loose period names. A Sticks & Stones founding
  reads water, game and flint and is indifferent to defensibility; by Bronze and
  Medieval the ford and the defensible ridge carry real weight, along with ore;
  by Industrial it is mineral wealth and navigable water, scored through trade
  position.
- _Open:_ there is no Coal deposit — Ore stands in for industrial mineral
  wealth. Whether coal deserves its own category is worth deciding before this
  is player-facing, since coal-versus-metal is a real historical distinction in
  where industrial cities went.
- Trade position comes almost free: the road generator already paths by terrain
  cost, so scoring a tile by how many cheap routes would pass through it is the
  same computation, inverted.

### 5b.3 The founding band

Twenty to forty people in several households — the archaeological range for a
neolithic founding group, and the smallest number at which demography works
unaided: enough unrelated adults for marriage to have real choices, and enough
households for lineages to diverge instead of collapsing into one (§7.5). Every
founder is Full-tier from the first tick (§11.3).

### 5b.4 Alone at the start

The player's civilization is the only one placed at world generation. The
faction step gains a solo mode that creates the player's faction and nothing
else; rival civilizations **emerge** from the simulation later rather than
existing at time zero. This is the truer sticks-and-stones arc, and its cost is
accepted deliberately: factions, trade and diplomacy sit idle through the
opening hours. Emergence is its own system and is not designed here.

### 5b.5 What this requires that does not exist

| Piece | Today |
| --- | --- |
| Region partition over the icosphere, with names and profiles | missing |
| Resource and landmark deposits derived from terrain | missing — tiles carry climate and elevation only |
| Site scoring: necessities × era-weighted advantages | missing — placement is a biome lottery with spacing |
| Settlement as an entity: population, stores, age, name, growth | missing — a def, a tile and a faction |
| Solo-start world generation | a flag on an existing step |
| A settlement interior when the player enters it | the §11.2 seam |

```mermaid
flowchart TD
  W[World map · regions with name, climate, resource profile] -->|player picks a region| R[Region view]
  R --> Markers[Markers: water, soil, timber, stone, clay, flint, ore, salt, game, ford, high ground]
  Terrain[Existing tile data: elevation, rainfall, rivers, biome] -->|deposits derived, not sprinkled| Markers
  Era[Era ladder] -->|weights which advantages matter| Score
  Markers --> Score[Site score · advisory for the player, decisive for emergent foundings]
  Score -.shades.-> Markers
  Markers -->|player places the centre| Found[Founding: 20-40 people, several households, all Full-tier]
  Found --> Chron[Founding chronicle entry]
  Found --> Settle[Settlement entity: population, stores, age, name]
  Settle -->|player enters at settlement scope| Map[Interior map generated and persisted]
```

## 6. Cross-System Contracts

There is no pub/sub event bus. Modules connect the way RimWorld's do, and the
dependency rules of §2 are what keep that safe:

- **Trackers**: per-pawn subsystems hang off `Pawn` (`health`, `needs`, `story`,
  `mindState`, `skills`, `workSettings`), each owning its own state and save.
- **`Notify_` hooks**: a virtual method on the owner that other systems call —
  `Notify_HediffChanged`, `Notify_StarvationInterval`, `Notify_TraitsChanged`.
  Cheap, ordered, debuggable.
- **Narrow C# events** where a listener is genuinely unknown at compile time:
  `Storyteller.IncidentFired`, `Faction.GoodwillChanged`,
  `ResearchManager.ProjectFinished`.
- **Interfaces for absent layers**: a system needing something unbuilt takes an
  interface instead — `IIncidentTarget`, `IEnvironmentSampler`, `IBillGiver`.
  This is how modules land before their dependencies exist.

_Planned_: the mod API surfaces these as sanctioned extension points.

## 7. Actor Simulation Layer

### 7.1 Needs & Mood

- Needs decay per 150-tick interval; thresholds gate behaviour and thoughts.
- Mood = 50% + grouped thought total, stacking geometrically per RimWorld.
- Thoughts: timed memories (stack limit, renew-oldest) and situational workers.
- Mood below break bands (0.35 / 0.20 / 0.05) → MTB roll → weighted,
  trait-filtered break table → mental state with a recovery and catharsis path.

### 7.2 Health

- Body-part tree per species; coverage weights drive random hit selection.
- Hediffs attach to a part or the whole body: injuries, staged diseases,
  missing parts, prosthetics. Comps add immunity, tending, decay, scarring,
  infection.
- Eleven capacities computed from part efficiency; downed from pain shock,
  unconsciousness or lost legs; dead from a lethal capacity at zero, core part
  destruction, lethal severity, or total damage past threshold.
- Disease is a severity-versus-immunity race, modified by tend quality.

### 7.3 Skills & Work

- Skill 0–20 on an XP curve, passion multiplies gain, daily saturation caps it,
  unused skills above level 10 rust.
- Work tags from traits and backstories disable skills and work types.
- Per-pawn priority grid; work givers order by priority, then natural priority,
  then priority within type, with emergency givers first.

### 7.4 AI (Think Tree / Jobs / Pathing) — _planned_

- Priority tree evaluated top-down per job request; danger, needs and directed
  orders outrank routine work.
- Jobs run as toil state machines; a reservation system prevents two pawns
  claiming the same target.
- Pathing: region graph for reachability plus a per-cell cost grid feeding A*.
  Full-agent populations will need shared paths (hierarchical or flow field).

```mermaid
flowchart TD
  Tree[Priority think tree] --> Override{Danger, need or edict?}
  Override -->|yes| Priority[Priority job giver]
  Override -->|no| Work[Work scan by priority]
  Priority --> Job
  Work --> Job
  Job --> Toils[Toil state machine]
  Toils --> Reserve[Reservation check]
  Toils --> Path[Region graph A*]
```

### 7.5 Demography, Lineage & Lifespan

RimWorld has no equivalent to port: a colony of twelve never needs generations.
A civilization does, so this module is SimWorld's own, built on the ported
`Pawn_AgeTracker` clock (`GenDate.TicksPerYear` = 3,600,000 ticks, life stages
at 3 / 13 / 18) rather than on a compressed one.

- **Household as the unit of lineage.** `Family` records one or two founders,
  a surname drawn from the same `NameBankDef` `Last` pools individual pawn
  names come from, its members, generation depth, and living vs. total counts.
  `Pawn_RelationsTracker` carries the back-references: `familyId`, `spouseId`,
  `motherId`, `fatherId`.
- **Marriage founds a new household; it never merges two.** Each couple
  starts a household of its own. The alternative — attaching the couple to one
  partner's existing house — concentrates a civilization into a single dynasty:
  measured at 90% of the population in one household in Epoch
  (`docs/research/epoch-inspiration.md` §5). Under the founding rule, a
  120-year regression run ends with 1,240 living descendants across 284
  households and the largest holding 0.8% of them.
- **Births** roll once per demographic interval per eligible couple, gated by
  the fertility window, a minimum interval since the last child, and food
  security. Only the _shape_ of Epoch's formula is carried over
  (base × mood × food security × doctrine); every constant is SimWorld's own
  and documented at its declaration in `DemographyTuning`.
- **Death from age is a hidden budget, not a curve.** Each pawn rolls a private
  death age at generation — its race's `lifeExpectancy` ± `LifespanSpreadYears`
  — and dies when it reaches it. The budget is deliberately unreadable: no
  public accessor exists, only an `internal` one for tests. The player is never
  shown how long a citizen has left.
- **The budget moves with the civilization.** Medical research completed
  _before_ a pawn is born lengthens it, scaled by the fraction of the
  `MedicineHealth` track finished; starvation intervals spend it, and eating
  again stops the loss without refunding it. Medicine is read at birth on
  purpose — curing a disease in 1650 cannot retroactively have given someone
  born in 1600 a healthier childhood. That is the same write-time rule the
  chronicle's fidelity model needs (§11.4).
- **The sweep** runs on `FamilyManager.DemographyTick` once per simulated year:
  marriages, then births, then deaths from age, then chronicle entries for each.

```mermaid
flowchart TD
  Tick[DemographyTick · once per simulated year] --> M[Marriages: pair eligible unmarried adults]
  M --> F[FoundHousehold · new family, new surname]
  Tick --> B[Births: per couple, per interval]
  B --> Gate{Fertile age · interval elapsed · food secure?}
  Gate -->|yes| Child[Newborn joins the household]
  Child --> Roll[Roll hidden lifespan budget]
  Med[Completed MedicineHealth research at birth] --> Roll
  Tick --> D[Deaths from age: age >= budget]
  Hunger[Starvation interval] -->|spends budget| D
  D --> Chron[Chronicle: birth, marriage, death]
  Child --> Chron
  F --> Chron
```

## 8. World Simulation Layer

### 8.1 Crafting, Economy & Factions

- Bills: recipe + ingredient filter + repeat mode (count, target with
  hysteresis, forever); ingredients chosen by a value getter over candidate
  stacks; quality rolled from skill, with inspiration reaching Legendary.
- Items sit in a `ThingCategoryDef` tree that `ThingFilter` selects over, by
  category, stuff, quality and hit points.
- Cooking carries a skill-driven food-poisoning chance; eating feeds the
  nutrition need.
- Trade price = market value × price type × relation and negotiator modifiers.
- Faction goodwill crosses thresholds → hostile / neutral / ally.
- Caravans path the world tile graph at a cost from hilliness, biome and roads.

### 8.2 Building / Power / Climate — _planned_

- Blueprint → frame → building, work speed from skill.
- Roof support by flood fill; unsupported roof collapses.
- Power as a producer/storage/conduit graph with a brownout policy.
- Per-room heat diffusion modulated by biome and season.

```mermaid
flowchart LR
  Blueprint --> Frame[Frame: work by skill]
  Frame --> Building
  Wall[Load-bearing grid] -->|flood fill| Support[Roof support]
  Support -->|unsupported| Collapse[Roof collapse]
  Producer[Power producer] --> Storage[Battery]
  Storage --> Grid[Conduit network]
  Grid -->|deficit| Brownout
```

### 8.3 Combat

- Ranged hit chance: shooter accuracy raised to the distance, weapon accuracy
  by range band, target size, cover pass chance, with a floor.
- Melee: hit chance versus dodge, both skill curves; downed targets cannot
  dodge.
- Armor: rating versus penetration rolls deflect, or halve and convert sharp to
  blunt.
- Downed and dead come from the health system, never from combat directly.

### 8.4 Social & Belief

- **Opinion** (`Pawn_RelationsTracker.OpinionOf`): relation type, social memories
  about that specific pawn, personality traits, and a stable compatibility factor
  hashed from the two pawns' ids — RimWorld's own mechanic, so a given pair just
  naturally gets on or doesn't, cheaply and deterministically.
- **Relations** (`PawnRelationDef`): family kinds (spouse, parent, child, sibling)
  are derived on demand from demography's own ids (`spouseId`/`parentIdA`/
  `parentIdB`/`childIds`) rather than stored a second time; friend, rival, lover
  and ex-spouse are stored as a `DirectPawnRelation` on both pawns.
- **Interactions** (`InteractionDef` + `InteractionWorker`): chitchat, deep talk,
  insult and slight, selected by weight per pair on a population-wide sweep every
  2,500 ticks — a rare-tick manager sweep (`SocialInteractionManager`), not
  per-pawn-per-tick work. A sufficiently bad opinion and mood can escalate an
  insult into a social fight, reusing the existing `MentalStateDef` machinery
  rather than a parallel system.
- **Social thoughts** are ordinary memory thoughts with `otherPawn` set (the mood
  system's own stack, not a separate one), feeding both mood and opinion
  (`ThoughtStage.baseOpinionOffset`).

**Belief — _planned_.** Ideology, precepts, memes, rituals and roles are out of
scope for this pass: belief is a separate, much larger design the god layer will
want to own (see `docs/status.json` system 17's `social.ideology` item).

## 9. Director Layer

- Threat points from wealth and per-pawn curves × difficulty × adaptation ×
  days passed, clamped to a band.
- Storyteller personas built from comps (on/off cycle, random main, intro,
  single MTB, disease) choose which incident category fires each interval.
- Incidents gate on earliest day, population, points and refire days.
- **The Chronicle** (SimWorld translation): every fired incident appends a
  narrator record. The persona name is still open — see §15.

```mermaid
flowchart TD
  State[Wealth, population, days] --> Points[Threat points budget]
  Adapt[Quiet-time adaptation] --> Points
  Persona[Storyteller comps] --> Check{Fire incident?}
  Points --> Check
  Check -->|yes| Category[Weighted category]
  Category --> Worker[Incident worker]
  Worker --> Chronicle[Chronicle entry]
```

- Quest layer: `QuestScriptDef` node graphs compile over a `Slate` into quest
  parts wired by signals — delay, letter, reward, end — delivered as letters
  with accept and expire timers.
- Scenarios set the start: forced traits, starting research and things, plus
  the civilization parts (era, rival count, biome). Running them needs a
  game-start orchestrator, which lands with the game loop.

## 10. God Layer & Civilization Translation

The re-focus from RimWorld: the player is not a colony overseer but a god
directing one civilization from sticks and stones into exotic technology.
RimWorld systems are **translated**, not replaced — `docs/status.json` records
the translation and its state per system.

- **Scale**: a colony becomes a civilization of settlements; factions become
  rival civilizations; the storyteller becomes the Chronicle, targeting _your_
  civilization.
- **Control**: the god issues edicts and goals that enter the think tree above
  routine work, instead of drafting individuals. Citizens keep full agency.
- **Eras**: an `EraDef` ladder over the research DAG carries a civilization from
  neolithic to archotech; era completion gates content and scales threats.
- **Aggregation**: per-citizen depth stays, but the god view reads rollups —
  public mood, population health, industry — rather than opening every person.

_Status_: eras and the Chronicle hook exist. Edicts, rollups and the god view
itself are planned and depend on the AI layer.

```mermaid
flowchart LR
  God[God: edicts and goals] --> Think[Think tree priority node]
  Think --> Citizens[Citizens act with full agency]
  Citizens --> Rollup[Aggregated civ state]
  Rollup --> GodView[God view: mood, health, industry]
  Rollup --> Chronicle[Chronicle narrates the era]
```

## 11. Scale: Time, Attention & Level of Detail

**Design, not built.** Nothing in this section has code yet. It records
decisions taken up front, because the modules landing next — settlements, the
game loop, the chronicle — are cheap to build against them and expensive to
retrofit.

### 11.1 Two clocks

- The ported clock stays authoritative: 60 ticks/second, 60,000-tick days,
  3,600,000-tick years (§4). Every pawn, job, hediff and research project runs
  on it. It is the only clock that decides anything.
- Above it sits an **abstracted clock** whose only job is to skip. The player
  hands the sim a span of years; the sim consumes it rather than ticking every
  tick of it.
- Time does not itself cause progression. A century of abstracted time with no
  people, no food and no research advances nothing. Progression comes from
  timers, work and events — whichever clock happens to retire them.
- The rule that keeps the two honest: **the abstract clock may only do what the
  real clock would have done.** Every process it advances needs a closed-form or
  sampled equivalent that agrees with ticking, and — where the span is short
  enough to afford it — a test that runs both ways and asserts the same end
  state. Interval-shaped systems satisfy this naturally: demography's yearly
  sweep (§7.5) is already written as a sweep rather than a per-tick trickle.
- Skipping stays deterministic because the abstract step draws from the same
  seeded streams (§4). A skipped century is reproducible.

### 11.2 Attention: civilization-wide sight, settlement-deep touch

Taken from RimWorld, whose split this codebase already mirrors structurally:

| RimWorld | SimWorld | State |
| --- | --- | --- |
| World map: tiles, factions, settlements, caravans — nothing at colony depth | `World/` (§5) | ported |
| Colony map: cells, things, pawns with jobs and needs at full depth | `Map/` (§5a) | ported |
| A settlement's world tile generates its interior map | `MapGen/` (§5) | ported |
| Entering a settlement (at settlement scope) triggers that generation and persists the result | — | **missing** |

The two halves now have a seam between them: `MapGen.MapGenerator.GenerateMapFor`
takes the world tile a settlement sits on — its biome, elevation, hilliness,
rainfall, rivers and deposits — and generates the interior that tile promised.
What is still missing is the game-loop half: nothing yet calls
`GenerateMapFor` when the player opens a settlement, and a generated map isn't
yet attached to its `WorldObject` and carried across a save. That is the next
piece of work, and it is now a game-loop/scope-switching problem rather than a
map-generation one.

The player's verbs follow the same split. At civilization scope the player sees
everything and acts through edicts, research direction and policy — indirect and
aggregate. At settlement scope the player can open any citizen and act on
particulars. **Sight is global, touch is local.**

### 11.3 Citizens: tiered by significance, not by distance or clock

Every citizen is a real agent record with a real identity. What varies is how
much of that record is _computed per tick_.

- **Full** — ticked exactly as ported: needs, mood, health, skills, jobs. The
  settlement under the player's attention, plus anyone promoted into it.
- **Interval** — the full state exists, but advances only on the rare/long
  buckets and the yearly sweeps, never per tick. Behaviour is drawn from
  aggregates instead of being run job by job.
- **Statistical** — the citizen is a member of a cohort: identity, family, age
  and participation in demography are real; needs and health are sampled from
  the cohort's distribution rather than tracked individually.

Promotion is by **significance, not proximity or elapsed time**: the player
looks at their settlement, they take a role (leader, founder, great worker), the
chronicle names them, or a relationship attaches them to someone already
promoted. Demotion is the reverse and must be lossless in identity — a demoted
citizen is still exactly who they were; only their minute-by-minute existence
stops being computed.

### 11.4 Fidelity: the level of detail _is_ the historical record

The observation this design turns on: **what the simulation did not record is
what history forgot.** The tier a citizen lived at bounds what the chronicle can
ever say about them. A Full-tier life leaves a detailed record; a Statistical
one leaves a name, a family and two dates — which is precisely what the deep
past leaves in reality.

One hard constraint follows: **fidelity is decided at write time, never at read
time.** The chronicle stores what was knowable when the event happened. Resolve
detail lazily against present knowledge instead, and researching Paper in 1600
retroactively remembers what a peasant ate in 1200.

So a single mechanism does two jobs, which is the reason to build it carefully
rather than as a performance hack:

- **Performance** — cheap citizens cost less, which is what makes civilization
  scale affordable at RimWorld depth.
- **Fiction** — the deep past is thin because it _was_ thin, not because a UI
  filter hid it.

The consequences are the interesting part. Writing, record-keeping and archives
become real technologies with a real effect. Losing them — to collapse, fire or
conquest — genuinely loses history. And the player's own attention shapes what
the civilization is able to remember about itself.

```mermaid
flowchart TB
  subgraph Scope[Player attention]
    Civ[Civilization scope · sight everywhere · edicts and policy]
    Set[Settlement scope · touch particulars · any citizen openable]
  end
  Civ -->|open a settlement| Set
  Set -->|step back| Civ

  subgraph Tiers[Citizen simulation tiers]
    Full[Full · per-tick needs, health, jobs]
    Interval[Interval · sweeps only, aggregate behaviour]
    Stat[Statistical · cohort sampling, identity kept]
  end
  Set --> Full
  Full -->|attention leaves, no role| Interval
  Interval -->|role, chronicle mention, relation| Full
  Interval --> Stat
  Stat --> Interval

  Full -->|detailed entries| Rec[Chronicle · written at the tier lived]
  Interval -->|sparse entries| Rec
  Stat -->|name, family, dates| Rec
  Rec --> Past[The deep past is thin because it was thin]
```

### 11.5 What this section deliberately does not decide

- **Tier budgets.** How many citizens each tier can afford comes from
  measurement, not guesswork; the benchmark harness exists to set those numbers.
- Whether Interval and Statistical are two tiers or samples of a continuum.
- How the abstract clock and the director interact — a skipped century still
  needs incidents, and they cannot all fire at the seam.

## 12. Presentation & Host

- `SimWorld.Core` is `netstandard2.1` with no engine reference and an asmdef
  marked `noEngineReferences`; Unity consumes it as a local package.
- The host renders and issues commands; it holds no simulation state.
- A launch path appears only once a playable loop exists — the Blueprint
  tracker shows the launch control disabled with its reason until then.

## 13. Determinism & Testing

- All randomness routes through seeded streams, so tests assert exact outcomes.
- Thread-static `Rand`, `Scribe` and `Find` state keeps xUnit's parallel
  collections isolated; tests touching the global `DefDatabase` share one
  collection.
- The core content loads in CI with zero config or DefOf errors — asserted, not
  assumed.
- Every module ships save/load round-trip tests alongside behaviour tests.
- CI runs build and test, the Blueprint build (which validates `status.json`),
  markdown lint, link check and mermaid validation.

## 14. Roadmap

Per-system state, checklists and translation decisions live in
`docs/status.json` and render in the Blueprint tracker. Phase 1 ships every
system as an isolated tested module; the god layer and a playable loop follow.

## 15. Open Questions

- **Narrator name**: Scribe, Historian, or The Chronicle. Code module stays
  `Director` until chosen.
- Population ceiling: the tiering design (§11.3) fixes the shape; the actual
  per-tier budgets wait on measurement from the benchmark harness, plus a
  pathing pass once AI lands.
- Endless tech beyond the authored era ladder: procedural generation shape.
- Multiplayer determinism, which would constrain RNG stream design.
- Director behaviour across an abstracted-time skip (§11.5): a skipped century
  still needs incidents, and they cannot all fire at the seam.
- Settlement generation and the opening state of a game — how a founding band
  picks a site, and what a settlement _is_ once it has an interior.
