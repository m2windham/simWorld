# Era progression: the ladder, the edicts and the rivals

Branch `era-progression`. This doc covers four things. Section 1 defines the era a civilization is in.
Section 2 gives each era's gates, unlocks and pace. Section 3 lists the edicts the god can issue in each
era. Section 4 sets how fast the rival factions advance.

Section 5 lists the work left, in order. Section 6 gives the runs that will test it. Section 7 is a buildable
spec for the next lane: a town that rebuilds itself as it climbs.

**Core principle.** Progression should be accurate and realistic, which means rebuilding everything over
time, as real towns do. An era is not only what the town can build next. It is also what the town tears
down and puts back in the new form: homes, walls, workshops, storage, roads. Wood gives way to stone and
stone to brick; paths become roads. Sections 1 to 4 decide when an era turns. Section 7 makes the turn show
in the streets.

Sources are named inline:

- **port** means the code in this repository, read for this doc.
- **recalled** means RimWorld behaviour remembered and not checked against a decompile here.

Treat recalled items as claims to verify.

## 1. The era model

### The problem

`ResearchManager.CurrentEra` meant "the last era whose research spine is finished". Edict gating, the god
view, rival comparison and the research tech level all read it. Several readers assumed something else.

- **Edicts.** Each edict's `researchFocus` points at a project in its `requiredEra`: Hunting in Sticks and
  stones, Agriculture in Agrarian, Masonry in Bronze. An edict that focuses research on its own era's
  project only makes sense while that project is unfinished. Under the old meaning the edict unlocked only
  after its project was done.
- **Starts.** An `AgrarianVillage` start seeds every era before Agrarian. The old model therefore put it in
  Sticks and stones, so it could not issue Break the sod. The second playtest hit exactly this, and
  `EdictIdlenessTests` worked around it by researching Agrarian in setup.
- **Other readers.** `EraAvailability.WorkingEra`, `EraPacing.FrontierEra` and `ScenPart_StartingEra`
  ("you start in the Agrarian era") already meant "the era being worked in".

### The decision

`CurrentEra` is the era the civilization is in, and the era it is learning.

| Name | Meaning | Who reads it |
| --- | --- | --- |
| `CurrentEra` | The era the town lives in. Starts at the first era. | Edicts (`requiredEra`, `obsoleteEra`), `EraAvailability`, incident `minEra`/`maxEra`, the god view, site weights, migration, research bench counts (`citizensPerScholar`), the arc report |
| `NextEra` | The era after it: what leaving this one leads to | The god view |
| `MasteredEra` | The era before it, fully learned. Null in the first era. | Research tech level (`AdvanceTechLevelToEra`), the emergence ceiling, rival arms (section 4) |

The rule of thumb: anything about what the town can already do reads the mastered era. Anything about
where the town lives reads `CurrentEra`.

### How the era turns

The town leaves an era when `EraDef.GateOpen` holds:

- its research spine is complete (`IsComplete`, unchanged), and
- its `conditions` are met (new, `Research/EraCondition.cs`).

`ResearchManager.TryAdvanceEra` checks this:

- when a project finishes;
- on every god tick (`GodManager.GodTick`, every 2000 ticks), so an era whose research is done turns as soon
  as the town meets its conditions.

Each step fires `EraReached(from, to)` with a chronicle line and a letter.

### Why the era is state, not derived

Conditions read the settlement, and the settlement can fall back: a house burns, the stores are eaten. So
`currentEra` is saved, and the ladder never runs backward.

- A save without the field derives the era from research on first read. This is the old meaning plus one,
  and is what any old save means.
- Scenario setup (`SetProjectFinishedForSetup`) moves the era silently and ignores conditions. A start says
  where history begins; it does not live through it.

### Conditions

Each condition reads state the settlement already produces, so an unwatched town can clear it, and each
has a player lever:

| Condition | Reads | Player lever |
| --- | --- | --- |
| `EraCondition_Housed{share}` | `DwellingUtility.HousedCitizens` | Raise the roofs, building priorities |
| `EraCondition_FoodStores{days}` | Stored nutrition over citizens × `Need_Food.BaseFoodFallPerTick` per day | Break the sod, Lean rations |
| `EraCondition_Population{count}` | Living citizens | Migration, mood |
| `EraCondition_Building{things,count}` | `Settlement.StructureCount` | Great works, building priorities |

Conditions count as met when no settlement has an interior map. That covers headless research and unit
tests: with nothing to measure, research alone decides.

**Grounding.** RimWorld's royal titles demand a throne room and bedroom of a set quality before a title
pays out (`RoyalTitleDef` room requirements, **recalled**). The epoch charter
(`docs/research/epoch-inspiration.md` §2) has an age turn on what the people have built, not only on what
they know. The bars themselves are this port's and are not sourced.

## 2. The ladder

### Timing

Timing is in days, at 60 days a year. `intendedYears` is now the years a nominal settlement spends in each
era, learning it. That covers its own research, priced at the mastered era's tech level, over the benches
the town keeps (`citizensPerScholar`).

`EraPacingTests.A_nominal_settlement_spends_the_intended_years_in_each_era` recomputes this from content
and pins it within 10%. The whole ladder to Exotic is about 17.75 years, against 30 to 50 years before.

The old intent spent its first two years in Sticks and stones, an era whose research is a few days. The
new intent makes the early eras short, where the trees are small, and the later eras long, where they
are large.

The rival clock uses the same timing (`FactionAdvancement.DayReached`).

### Per era

| Era | What it is | To leave it (research spine plus) | Unlocks on entry (letter, view) | Threats | Years in it | Entered about day |
| --- | --- | --- | --- | --- | --- | --- |
| Sticks and stones | A band with fire, stone tools and the hunt | Half the people housed | Edicts: Hunter's mandate, Raise the roofs. Houses: windbreak hut, pit house | Stone-age raiders (rivals have mastered nothing) | 0.25 | 0 |
| Agrarian | Farming, storage, permanence | Food in store for 10 days | Edicts: Break the sod, Lean rations, Harvest feast. House: longhouse | Raiders still stone-armed; the world clock is in Agrarian by day 15 | 1 | 15 |
| Bronze age | Smelting; the smith's fire | A smithy stands | Edict: Great works (needs a Steward). House: town house. Hunter's mandate and Raise the roofs retired | Rivals have mastered Agrarian, which has no raider kinds of its own, so still the stone-age band | 1.5 | 75 |
| Classical | Law, scholars, planned streets | 15 days of food and three in four housed | Edict: Patronage of learning. House: courtyard house | Bronze-age kinds (bowmen, legionaries) | 2 | 165 |
| Medieval | Iron, castles | 24 people | Edict: Iron discipline. House: stone town house | Classical kinds (longbowmen, men-at-arms) | 2.5 | 285 |
| Industrial | Steam and the factory | 28 people and 20 days of food | Edict: Industrial mobilization. House: terrace house | Medieval kinds (crossbows, early rifles) | 3 | 435 |
| Information | Electronics and orbit | 32 people | House: suburban house. No new edict yet (section 5) | Industrial kinds | 3.5 | 615 |
| Exotic | The procedural tail | (last era) | House: arcology apartment | Information-era kinds | 4 | 825 |

Take Hunter's mandate as a lever for an era, not a whole era of play. Take Agrarian and Bronze as the first
real stretch: a year and a half at most before the first metal raids.

### Pricing

`researchCostFactor` was re-derived from the new intent:

| Era | Factor |
| --- | --- |
| Sticks and stones | 1.0 |
| Agrarian | 3.0 |
| Bronze | 1.7 |
| Classical | 1.9 |
| Medieval | 1.5 |
| Industrial | 0.78 |
| Information | 1.0 |
| Exotic | 1.15 |

Sticks and stones stays at RimWorld's price: priced up, the band waited two months for a roof
(`docs/research/era-arc.md`). Industrial comes in under 1 because it is the largest authored tree and the
first era with two benches.

`citizensPerScholar` moved one era later (25, 25, 25, 20, 18, 12, 8, 6). This keeps the bench count of the
era being learned where it was.

### What reaching an era changes

Reaching an era changes four things.

- **The letter and the god view say what the age opened.** `EraUnlocks.Describe` lists, from content:
  - the new edicts and the edicts left behind;
  - houses and buildings to learn;
  - the project count;
  - "To leave it, besides its research: …".
- **The god view's `EraOutlook` shows the climb.** It carries the current and next era, the gates
  finished, each condition (`EraConditionLine`: label, current, target, met) and a progress bar over gates
  plus conditions. It also lists the era's edicts, buildings and dwellings, and the rival era with its lead.
- **Edicts open and retire.** Hunter's mandate is outgrown on entering Bronze, with a chronicle line.
- **What the town and its rivals use moves up.** The research tech level rises to the mastered era, and so
  do the rival arms ceiling (section 4) and the emergence ceiling.

## 3. The edicts

The god sets direction. An edict is the strongest lever and should cost something. Each edict now states
what it does in the god view's `EdictOption.Effects`, generated from the def. A line can never describe a
mechanism the edict does not carry.

Edicts with a span lapse on their own: "Edict ran its course". Every edict with a rest
(`cooldownDays`) cannot be issued again until the rest runs out, however it ended. That includes an early
rescind and one rescinded by a contradicting edict.

**Grounding for timers.** RimWorld's royal permits have a `cooldownDays` before reuse (**recalled**). Its
rituals and parties are one-off events with a lasting memory (**recalled**). The port's existing `durationDays`
on memory thoughts carries the memory.

| Edict | Era (from–until) | What it does | Cost | Span / rest | Trade-off | RimWorld mechanism |
| --- | --- | --- | --- | --- | --- | --- |
| Hunter's mandate | Sticks and stones – Bronze | Every able hand hunts. Every animal is marked. Research turns to Hunting. | Mood −3 | 3 days / 7 days | Meat now, nothing else done | Hunt designation drives `WorkGiver_HunterHunt` (port) |
| **Raise the roofs** (new) | Sticks and stones – Bronze | Construction before all other work. Research turns to Pit dwellings. | Mood −2 | 5 days / 10 days | Houses (the era's condition) at the hunt's expense: measured, the hunt halved | Work-type priority order in `JobGiver_Work` (port) |
| Break the sod | Agrarian – | Every able hand to the fields. Research turns to Agriculture. | Mood −4 | 10 days / 20 days | Surplus (the era's condition) while everything else waits | Same as Hunter's mandate, Growing |
| **Lean rations** (new) | Agrarian – | Adults eat only when urgently hungry. Children are exempt. | Mood −3, plus the hunger thoughts it causes | Stands / none | Stores last longer (the food condition) while the band goes hungry | `Need_Food` falls at 0.5× when hungry and 0.25× when urgently hungry (port, `FoodFallPerTickAssumingCategory`) |
| **Harvest feast** (new) | Agrarian – | No routine work for a day. Recreation is opened up to 90% joy. Emergencies are still answered. | A day's labour | 1 day / 20 days | +4 mood during it, then a +8 memory for 6 days if it runs its course | Party and recreation block (recalled). Memory thoughts (port) |
| Great works | Bronze – | Construction and Mining first; walls and storehouses first. Research turns to Masonry. Needs a Steward. | Mood −6 | Stands | Building over everything; suspended if the seat falls vacant | Royal title / office (port `requiredOffice`) |
| **Patronage of learning** (new) | Classical – | Research before all other work, so every bench stays staffed | None in mood; the hands it takes | Stands | Faster through the long eras, slower everywhere else | Work-type priority (port) |
| Iron discipline | Medieval – | Smithing first. Research turns to Steel. | Mood −5 | Stands | Arms and tools over food and comfort | Work-type priority (port) |
| Industrial mobilization | Industrial – | Crafting and hauling first. Research turns to Steam engine. | Mood −7 | Stands | Output over mood | Work-type priority (port) |
| Take up arms | Any | Every citizen who can fight goes to meet the enemy, armed or not | Mood −6 | Stands | Defence over everything | RimWorld's draft, as a standing posture (port `CombatPostureUtility`) |

### Mechanisms added

All of these are in the think tree, none is a setter:

- `rationFood` gates `ThinkNode_ConditionalHungry`.
- `restDay` gates the routine half of `JobGiver_Work` and the edict tier, and raises
  `ThinkNode_ConditionalLowJoy` to 0.9 for the day.
- `durationDays` and `cooldownDays` are handled in `GodManager` and saved.
- `completedThought` is granted on lapse to every living citizen the edict applies to.

## 4. The rival clock

Three options were considered:

- **Fixed clock.** The previous behaviour: rivals advance on `DayReached`. It punishes a slow player with
  rivals two or three eras ahead, which is a stomp, not a decision.
- **Follow the player.** Rivals are always the player's era. This removes the pressure to advance.
- **Blend (chosen).** The world clock still runs on `DayReached`. A rival is in the clock's era, capped at
  one era past the player's `CurrentEra`. It fights with the era before that one (`ArmsEra`), the era it
  has mastered, as the player does.

What the blend means in play:

- A town that keeps up meets raiders armed with the era it has itself mastered.
- A town that falls behind meets raiders one age ahead of the age it is in, and no further.
- A town that runs ahead keeps its lead, because the clock does not chase it.

`EraOutlook.RivalLead` shows the gap.

**Grounding.** RimWorld scales raid points with the colony's wealth and population, so threat follows the
player's growth (`StorytellerUtility.DefaultThreatPointsNow`; the port's `Director/IncidentWorker.cs`
carries the points side). `docs/design/the-loop.md` asks for pressure scaled to the player's own growth.

The cap is that rule applied to arms. The clock keeps time real, so idling is never free.

## 5. Next, in order

First, in its own lane: **renewal (section 7)**. It is the largest item and the one the player sees.
The rest, for this lane or the next:

1. **Tune the conditions from CI.** Read the arc runs in section 6, look at how long each era holds on its
   conditions after its research is done, and move the bars. Tune the Housed share first: it decides the
   whole opening.
2. **Era-turn raid grace.** For a few days after a turn, hold off raids armed with the new era's kinds. The
   letter promises a new age; a stronger raid should not be the first thing it brings.
3. **Edicts for Information and Exotic.** Neither era has an edict of its own yet. Both inherit Industrial
   mobilization and Patronage of learning.
4. **Long days.** An edict that moves the timetable: fewer sleep hours, more work, and rest debt. This
   needs a timetable in the port first.
5. **Edict slots scaled by era.** A fixed `GodTuning.MaxActiveEdicts` (three) crowds the later eras' roster.
   One more slot at Classical and at Industrial is the obvious shape.
6. **Ingredient delivery to benches.** A Patronage-of-learning town still stalls on any bench whose project
   needs materials.
7. **`ScenPart_StartingEra` sets the research tech level to the start era's.** Under this model it should
   be the mastered era's. Left alone because the Industrial start represents a crashed industrial crew
   whose know-how is a floor. Revisit when that scenario is reworked.

## 6. Measurement (GitHub Actions, not the dev PC)

Push the branch and point `ci/lane-runs` (worktree `H:/dev/_wt/core-ci`, `.github/workflows/lane-runs.yml`)
at its head SHA. Use the same matrix shape as the raid-fix runs.

### Full suite

```sh
dotnet test tests/SimWorld.Core.Tests -c Release
```

### Arcs, 10 years

These four answer two questions: does each era hold for about its intended years, and do the conditions
ever stall a town for good? Read the sample line ("era X, next Y N% learned, conditions met/not met") and
the era table.

```sh
dotnet run -c Release --project tools/bench/SimWorld.Bench -- --suite arc --days 600 --arc-every 30 --solo false --threats true  --band 20 --seed 99
dotnet run -c Release --project tools/bench/SimWorld.Bench -- --suite arc --days 600 --arc-every 30 --solo false --threats true  --band 20 --seed 2024
dotnet run -c Release --project tools/bench/SimWorld.Bench -- --suite arc --days 600 --arc-every 30 --solo false --threats true  --band 40 --seed 201
dotnet run -c Release --project tools/bench/SimWorld.Bench -- --suite arc --days 600 --arc-every 30 --solo false --threats false --band 40 --seed 201
dotnet run -c Release --project tools/bench/SimWorld.Bench -- --suite arc --days 600 --arc-every 30 --solo false --threats true  --band 40 --seed 202
```

### The 21-seed, 60-day storyteller bench, threats on and off

This checks that the opening (Sticks and stones into Agrarian, Raise the roofs available) has not cost
lives.

```sh
for r in 20:105 20:99 20:777 20:2024 20:12345 20:31337 20:101 20:102 20:103 20:104 20:106 20:107 20:108 20:109 20:110 40:777 40:12345 40:201 40:202 40:203 40:204; do
  dotnet run -c Release --no-build --project tools/bench/SimWorld.Bench -- --suite storyteller --solo false --days 60 --threats true --band "${r%%:*}" --seed "${r##*:}"
done
```

Repeat with `--threats false`.

### The raid probe

This checks raider arms against the capped clock.

```sh
dotnet run -c Release --project tools/bench/SimWorld.Bench -- --suite probe --days 60 --threats true --band 20 --seed 99
```

### Local check

The one local check allowed is the 60-day probe above, run once.

## 7. Renewal: the town rebuilds itself

Status: a spec for a dedicated redevelopment lane. Nothing here is implemented on `era-progression`.

### 7.1 Principle and the question it answers

A real town is never finished. Over a few generations most of its houses, walls, stores and streets are
pulled down and put back in the materials and forms of the day. Some old things are kept on purpose. A
settlement here should do the same: across eight eras almost nothing from the founding should still stand,
except what the town chose to keep.

A UX audit asked whether settlements rebuild their homes as the era's `House_<Era>`. Today they do not.
`DwellingPlanner` builds each new house in the newest rung the civilization knows, and its own doc says
"nothing standing is rebuilt" (port, `Building/DwellingPlanner.cs`). Under this spec the answer is yes, and
the same holds for walls, workshops, storage and roads.

### 7.2 What the port already has

| Piece | What it does today | Where |
| --- | --- | --- |
| New houses in the newest rung | A household without a house gets the newest `DwellingDef` the civilization knows | `DwellingPlanner.Plan`, `DwellingUtility.PreferredDwellingDef` |
| Repair | A missing wall, door or bed of a planned house is planned again, in whatever the town builds with by then | `DwellingPlanner.Repair` |
| Material upgrade of plans | Wall and door blueprints not yet started switch from wood to a spare metal | `MaterialLadder.UpgradeBlueprints` |
| Wear | Things left under open sky deteriorate. Buildings do not age. | `Weather/SteadyEnvironmentEffects.cs` |
| Streets | Three local road terrains, `StreetDirtPath`, `StreetDirtRoad` and `StreetStoneRoad`, painted at map generation from the world road and never upgraded | `TerrainDefs/Terrain_Roads.xml`, `MapGen.GenStep_Roads` |
| Era of a thing | The era of a buildable def, from its research | `EraAvailability.EraOf`, `EraUnlocks.Buildings` |
| Dwelling view | Def, rung, footprint, completeness, beds, household, roof | `Map/View/DwellingView.cs` |

The pieces to build on exist. What is missing is the decision to replace something that still stands, and
a way to do it without putting the household out of doors.

### 7.3 RimWorld grounding

There is little to port.

- **Damage only, no age** (recalled). RimWorld buildings lose hit points only to damage. They do not age,
  and a player replaces an outdated wall by hand.
- **Replace in place** (recalled; added around version 1.3). A blueprint of a different stuff can be placed
  over an existing wall or floor. Builders raise the new frame while the old building stands, and the old
  one is swapped out when the new one is finished. This is the one mechanism worth porting as it is
  (step 2 in 7.9).
- **Item deterioration** (port). Things outdoors wear down, faster when unroofed
  (`SteadyEnvironmentEffects.UnroofedDeteriorationFactor`). Building wear (7.4) reuses that shape.
- **Wealth and beauty** (recalled). Room impressiveness and colony wealth reward better materials, but
  nothing makes a colony renew on its own.

Everything else in this section is **original design**: the drivers, the scores, the pace, heritage, and
the town acting on its own.

### 7.4 What drives renewal

Each structure the settlement owns gets a renewal score once a day. The highest scores are renewed first,
within the budget in 7.5.

| Driver | Reads | Effect | Grounding |
| --- | --- | --- | --- |
| **Obsolescence** | The form's era (`EraAvailability.EraOf(def)`, or `DwellingDef.era` for a house) against `ResearchManager.CurrentEra`; its stuff against the best the town can afford (`GenStuff.AffordableStuffFor`, `MaterialLadder.BestBuildingMetal`) | The main term. Zero in the current era's form. Rises with each era behind and each step down the material ladder | Original |
| **Condition** | Hit-point fraction | Below half its hit points, a structure with a newer form available is renewed in the new form, not repaired in the old one. With no newer form, repair stays as it is today | Original; the damage itself is RimWorld's |
| **Age** | Ticks since built (new `CompBuilt.builtTick`, saved). Past `serviceYears`, the structure wears: it slowly loses hit points, faster unroofed | A small term, so an old form is renewed eventually even in a quiet town | Original; the curve borrows `FinalDeteriorationRate`'s shape |
| **Crowding** | Household size against the house's beds; storage fill against capacity | Raises homes and stores that are too small. Crowding alone can renew a house within an era, as a bigger house of the same rung | Original |
| **Wealth and resources** | Stock of the new form's material, after what blueprints and frames already claim (the accounting `MaterialLadder` already does) | A gate, not a term. Nothing is renewed unless its full cost is in store above a reserve | Original |
| **Damage events** | A structure destroyed by a raid, fire or quake | Rebuilt at once in the current form, through the existing repair path, instead of the old form | Extends `DwellingPlanner.Repair` |

Renewal never outranks the things that keep people alive. The initiative waits while any of these hold:

- someone has no bed;
- food in store is under 5 days, or under the current era's food condition if that is lower;
- a raid is on the map.

### 7.5 How fast it happens

The aim is a town that is mostly in its current era's forms by the time it leaves that era, and not much
sooner. The measure is the **share of structures in the current or previous era's form**:

- about 50% by the middle of an era;
- at least 80% when the era is left;
- never 100%, because of heritage (7.6).

The budget that should deliver it, in a new `RenewalTuning`:

| Setting | Value | Why |
| --- | --- | --- |
| `PassIntervalTicks` | One day | Renewal is slow, and a daily pass is cheap |
| `MaxUnderway` | One per 15 citizens, at least one | The shape of `DwellingTuning.MaxUnderway`: a band renews one thing at a time, a city several |
| `MaxHomesUnderwayShare` | A quarter of homes | No more than a quarter of households live in a house being replaced |
| `MaterialReserve` | 1.5 × the structure's cost | A renewal never takes the last of a material |
| `ObsoleteAfterEras` | 1 | A form is due once it is one era behind; two eras behind doubles its score |

A rough check against section 2: a 20-person Bronze town has about 6 houses, 40 wall cells and a few
workshops. With one structure underway at a time and a house taking 2 to 4 days, its homes turn over in
about a month of the 90-day Bronze age. The arc runs (section 6) decide whether that is too fast. If it is,
lower `MaxUnderway`, not the scores.

**Order.** The score decides. Ties break in this order: homes, storage, workshops, walls, roads. People
notice homes first, and roads are the cheapest thing to leave as they are.

### 7.6 What is kept as heritage

Some old structures are kept on purpose. A heritage structure is repaired in its own form and material,
never renewed, and wears at half the rate.

- **The founding house.** The first complete dwelling the settlement built.
- **One exemplar per obsolete rung.** When the last-but-one house of a rung would be renewed, the oldest
  remaining one is marked heritage instead. The town keeps an old quarter.
- **Monuments and graves.** Sculptures, graves where the port has them, and anything with a new
  `building.isMonument` flag.
- **Places of record.** A structure that a chronicle moment names as its location (`EventLocationView`):
  the house a ruler was born in, the wall that held a siege.
- **The god's choice.** A new command marks or unmarks any structure: `GodCommands.SetHeritage(thingId,
  bool)`.

Heritage is capped at about one structure in ten, so the town is not frozen. Losing a heritage structure to
damage writes a chronicle line.

### 7.7 Mechanisms and defs touched

| Item | Kind | Notes |
| --- | --- | --- |
| `Building/RenewalInitiative.cs` | New | The daily pass: score, budget, plan. A settlement initiative beside `SettlementConstructionInitiative` and `SettlementWorksInitiative` |
| `Building/RenewalTuning.cs` | New | The constants in 7.5 |
| `Building/CompBuilt.cs` | New comp | `builtTick` and `isHeritage`, saved. Attached when a frame completes, so no def needs editing |
| Replace in place | New, in `GenConstruct` and `Frame` | The port of RimWorld's replace. A blueprint may stand on a building of a different def or stuff. When the frame completes, the old building is despawned and the new one spawned, with a fresh `builtTick` |
| `DwellingPlanner.Renew` | New method | Build the new rung on a free plot along the street, move the household when it is complete (`DwellingUtility.AssignHomes`), then deconstruct the old house. With no free plot, replace in place part by part, walls before beds |
| `MaterialLadder.UpgradeStanding` | New, beside `UpgradeBlueprints` | Plans replace-in-place blueprints for standing walls and doors whose stuff is below the best the town can afford |
| `DwellingDef.serviceYears`, `ThingDef.building.serviceYears` | New fields | Default: twice the form era's `intendedYears` |
| `ThingDef.building.supersededBy` | New optional field | The next form where the era ladder alone does not say it: `StorageHut` to a granary, `WallEarth` to `Wall`. When empty, the next form is the newest def of the same designation category and role |
| `TerrainDef.renewsTo`, `TerrainDef.era` | New fields on road terrains | `StreetDirtPath` (Sticks and stones) to `StreetDirtRoad` (Agrarian) to `StreetStoneRoad` (Classical). A paved Industrial street needs a new terrain def |
| `Weather/SteadyEnvironmentEffects` | Extend | Building wear past `serviceYears` |
| `GodCommands.SetHeritage` | New command | 7.6 |

### 7.8 View data the host needs

Every handle is a defName or a thing id, as the view rule requires.

On `DwellingView`, and on a new `StructureView` for walls, workshops and storage carried in
`MapViewChunk` and `MapViewDelta`:

- `EraDefName`: the era of the form;
- `BuiltTick` and `AgeYears`;
- `Condition`: the hit-point fraction;
- `IsObsolete`: the form is behind the current era;
- `IsHeritage`;
- `Renewal`: `None`, `Planned`, `Underway` or `Replaced`;
- `ReplacedByThingId` and `ReplacesThingId`, so the host can cross-fade one into the other.

On `EraOutlook`, a `RenewalSummary` for the town:

- per category (homes, storage, workshops, walls, roads): the count, the count in the current era's form,
  and the count underway;
- the share in the current or previous form, the 7.5 measure;
- the heritage count.

In the chronicle:

- a moment when the last non-heritage structure of a rung goes ("The last pit house is pulled down");
- one letter per era, on its first renewal ("The town begins to rebuild in stone").

With these the host can show an old quarter beside new streets, scaffolding on the house being replaced,
and the share climbing in the era panel.

### 7.9 Ordered steps and their tests

Each step can ship on its own. Every test runs on a generated map, as `EdictProgressionMapTests` does, and
every new guard gets a mutation proof.

1. **`CompBuilt` and its Scribe round trip.**
   - A building finished on tick N reports `builtTick` N after a save and load.
   - A scenario's starting buildings carry the start tick.
2. **Replace in place.**
   - A stone wall blueprint over a wood wall leaves the wood wall standing, and pathing unchanged, until the
     frame completes. Then exactly one wall stands, in the new stuff.
   - A cancelled replacement leaves the old wall untouched.
3. **`MaterialLadder.UpgradeStanding` within the budget.**
   - With spare stone, wood walls are planned for replacement, at most `MaxUnderway` at once, never taking
     stock below `MaterialReserve`.
   - With no spare stone, nothing is planned.
4. **`DwellingPlanner.Renew`.**
   - In Bronze, with a free plot, a pit-house household gets a town house and moves in when it is complete.
     The pit house is deconstructed after the move, never before.
   - Nobody is without a bed at any sampled tick.
5. **`RenewalInitiative`: scores and waits.**
   - Nothing is planned while someone lacks a bed, food is short or a raid is on the map.
   - Of two obsolete houses, the more crowded is renewed first.
6. **Heritage.**
   - The founding house survives a full renewal of its rung.
   - One exemplar per rung is kept.
   - A god-marked structure is repaired in its own stuff.
7. **Wear.**
   - A structure past `serviceYears` loses hit points, faster unroofed.
   - A heritage structure loses them at half the rate.
8. **Roads.**
   - Entering Agrarian schedules path cells for upgrade to dirt road, through the same budget.
9. **View fields and chronicle.**
   - `DwellingView` and `StructureView` carry every field in 7.8.
   - `RenewalSummary` counts match the map.
   - The "last pit house" moment is written once.
10. **Measurement.**
    - Run the section 6 arcs with renewal on, and report the 7.5 share at mid-era and at each turn.
    - Tune `MaxUnderway`, not the scores, to land it.

### 7.10 How it meets this lane

- The `Housed` condition counts houses of any rung, so renewal never blocks an era. A later condition could
  ask for "half the homes in this era's form", but only once step 10 shows renewal gets there unaided.
- `EraUnlocks.Describe` already lists each era's houses and buildings in the era letter. Renewal makes that
  list a promise the town keeps.

### 7.11 Decisions taken building it (lane `renewal`)

Choices the spec left open, recorded as they were made, in build order.

- **Step 1: the built tick lives on `Building`, not in a `CompBuilt`.** Comps are rebuilt from `def.comps` on
  every load (`ThingWithComps.InitializeComps`), so a comp attached at run time would vanish after a save, and
  listing it would mean editing every building def. `Building.builtTick` and `Building.isHeritage` are saved
  fields every building carries. A building's tick is set when it first spawns: a finished frame, or map
  generation and a scenario's start. A save from before the field loads as `UnknownBuiltTick` (-1).
- **Step 2: replace in place covers walls only.** `GenConstruct.ReplaceTag` reads the family off what the port
  already knows (`StoneWallMaterials.IsWall`, the earth bank) instead of adding RimWorld's `replaceTags` to
  content. Doors are left out: a door's frame is impassable here, so replacing a house's door would shut its
  household out until it was finished. The replaced building gives back what deconstructing it would, dropped
  beside the cell rather than inside the new wall. Both buildings are destroyed with `WillReplace`, so the cell
  never stops holding the roof. A site's target is read off the map (`GenConstruct.ThingToReplace`: a site on an
  edifice is always a replacement), so a save carries nothing new and either spawn order on load leaves the old
  building in the edifice slot.
- **Step 3 was built after the homes.** Steps 4 to 6 (homes) were built before step 3 (`UpgradeStanding`), since a
  house renewed is what the player sees first.
- **Step 4: new before old, and only on a free plot.** `DwellingPlanner.Renew` lays the new rung on the first free
  plot (`TryFindPlot`), linked to the old house both ways by a new `Dwelling.id`. The household keeps the old house,
  repaired as before, until every part of the new one stands; `DwellingUtility.AssignHomes` (still the one writer of
  `householdId`) then moves it in one step and marks the old house `beingDemolished`. With no free plot, renewal
  waits: the spec's fallback, replacing in place part by part, is not built in this lane.
- **Demolition is RimWorld's deconstruct designation, placed by the settlement.** The player's deconstruct giver
  works only the player's marks, so a new `WorkGiver_DeconstructRenewal` (`WorkGivers_Renewal.xml`, Construction,
  `priorityInType` 80) works the settlement's own, below finishing frames (95) and the player's marks (110). The
  player switching the settlement's deconstruct initiative off stops it.
- **The old roof is lifted when demolition starts.** The port has no no-roof area or roof-removal job (RimWorld's
  way, recalled), and a roof whose last wall goes falls on whoever is under it. So the roof and its build-roof
  marks over the old footprint are cleared at once. Once nothing built stands on the footprint, the house is struck
  off the plan, which frees its plot.
- **Ids are given lazily.** Planners and tests add dwellings to the compound directly; every pass that reads ids
  calls `SettlementCompound.EnsureDwellingIds` first, in plan order, so ids are the same on every replay and load.
- **Step 5: the waits, the budget and the score.** `RenewalInitiative` runs inside the construction pass, once a
  day. It waits while built beds are fewer than living citizens, while stored food (the
  `EraCondition_FoodStores` reading) is under 5 days or the era's own food bar if lower, and while a pawn of a
  faction hostile to the settlement is up on the map. Its budget is the spec's (`MaxUnderway` one per 15 citizens,
  a quarter of homes), and it also waits while `DwellingTuning.MaxUnderway` houses are already going up, so new
  houses for households without one come first. The score is eras behind (once due), crowding into a rung with
  more beds, walls under half their hit points, and a little age; ties go to the older house. One renewal is
  started per pass.
- **The material gate skips wood.** The reserve rule (1.5 times the new house's cost, over what sites already
  claim, `MaterialLadder.Spare`) holds for every material except logs: builders fell a tree for a blueprint that
  wants wood (`WorkGiver_ConstructChopWood`), and no one keeps a woodpile, so gating on logs would stall every
  wooden rung. Doors and beds are priced in their default stuff: their stuff is picked at placement from what lies
  on hand and falls back to the default, so pricing them in what was on hand let every part claim the same few
  steel bars and no house looked affordable.
- **Step 6: the founding house is the first house finished.** `DwellingPlanner` stamps each dwelling's
  `completedTick` the pass it is first whole; the first one stamped becomes `SettlementCompound.foundingDwellingId`
  and is marked heritage on the spot. A save from before the field marks the first finished house it sees.
- **The exemplar is taken when a rung is down to two.** Before renewal starts on a house whose rung has at most two
  unreplaced houses left and none kept, the oldest of them (lowest id) is marked heritage instead; if that is the
  house picked, the next candidate goes. The cap (one structure in ten, counted over every faction building on the
  map, at least one) is checked before marking; at the cap a rung is renewed out entirely.
- **A kept house remembers its wall.** Marking a house copies the def and stuff of its first standing wall onto the
  dwelling (`heritageWall`, `heritageWallStuff`), so a lost wall is put back as it was rather than in the era's
  wall. Wood is always used as remembered (a tree can be felled for it); any other stuff only while a wall's worth
  is on hand, else the era's affordable stuff, so a kept house never waits forever on a stone the map has lost.
- **The god's mark is per house.** `GodCommands.SetHeritage(thingId, bool)` takes any building's id; on a house it
  marks the whole dwelling. It refuses a house already being replaced and a mark past the cap. Unmarking frees the
  house for renewal and for the era's repairs. Chronicle lines are free text: "The town begins to rebuild in the
  forms of ..." once per era, and "The last ... is pulled down" when a rung's last unkept house goes.
- **Step 9: renewal is shown on the house, not the Thing.** `DwellingView` gains `DwellingId` (the dwelling's own
  id: a house is a plan of many Things, so it has no thing id), `EraDefName`, `BuiltTick`, `AgeYears`, `Condition`,
  `IsObsolete`, `IsHeritage`, `Renewal` (`None`, `Planned`, `Underway` or `Replaced`, as a string like the
  snapshot's other stages) and `ReplacedByDwellingId` / `ReplacesDwellingId`, the pair a host cross-fades between.
  The per-building fields of 7.8 (`StructureView` in chunks and deltas) are not built in this lane.
- **The era panel's measure is `EraOutlook.Renewal`.** `RenewalSummary` counts homes (by their rung's era) and,
  outside houses, storage huts, benches that take bills and walls (by `EraAvailability.EraOf`, the era of the
  research that unlocks them; none means the first era). `Share` is current-or-previous-era structures over all,
  7.5's measure. It also carries the heritage count and the reason renewal is waiting. Roads are not counted.
- **Step 3: standing walls are renewed a stretch at a time, after the homes.** `MaterialLadder.UpgradeStanding` runs
  in the same daily pass, under the same waits, after the house pass. It plans a replace-in-place blueprint over each
  of the settlement's own walls outside any house whose hit points are below the wall the town would raise today
  (the era's stone, else a spare bulk metal), heritage walls excepted, in map order. Its budget is
  `WallCellsPerStructure` (8, the port's own figure) wall cells per unit of `RenewalTuning.MaxUnderway`, counted
  over replacements already standing, separate from the homes' budget. Each wall needs the reserve (1.5 times its
  cost, spare over other sites' claims) before it is planned. A cell whose ground cannot bear the new wall is
  skipped. House walls are left to the house's renewal.

### 7.12 Renewal past the early eras (lane `renewal2`)

The ten-year arcs renewed homes through the Agrarian and Bronze ages and then stopped. Each cause below was found in
a probe and is pinned by a test on a generated map (`RenewalLaterErasTests`, `BabyInteractionTests`).

- **The ledger is stock.** `SettlementStockInitiative` banks stockpiled chunks, blocks, logs and metal into
  `Settlement.Stores`, and every material gate read the map alone: `MaterialLadder.Spare`,
  `StoneWallMaterials.CanAffordOne` and through them `RenewalInitiative.Affordable` and the wall pass. A probe town
  held 38 chunks and 600 logs in its ledger and none on the ground, so no stonecutter's table was ever raised, no
  block was ever cut and no stone wall ever built. `Spare` and `CanAffordOne` now count the ledger;
  `SettlementBuildStock` issues what blueprints and frames still need from the ledger next to the settlement's
  stockpile, as `SettlementBillStock` already did for bills; and `StonecutterInitiative` raises its table, and
  takes its bill, for stone held only in the ledger.
- **A full street rebuilds in place.** Renewal built only on a free plot, and a town of forty filled its street in
  its first month and never renewed a house again. With no free plot, `DwellingPlanner.RenewInPlace` plans the new
  rung on the old house's own plot. The household's claim moves to the new house at once, the old one is pulled
  down, and nothing of the new one is planned until its ground is clear. It goes ahead only while the dormitory
  has a bed for every member beyond the citizens already without a house (`RenewalInitiative.CanLodge`), and never
  while beds are short. This is 7.7's fallback done a whole house at a time, since the rungs' plans do not share
  their walls. RimWorld has none (recalled: its player rebuilds by hand).
- **A bed shortage no longer stops every renewal.** In a growing town, births and newcomers kept beds short for good,
  and renewal waited for ever. While beds are short, a house is still renewed if its household has more members
  than beds and the new rung has more beds (`RenewalInitiative.AddsBeds`). It is built on a free plot only, new
  before old, so no bed is lost on the way. Stores, benches and walls still wait.
- **A house coming down is not one going up.** `DwellingUtility.CountUnderway` counted houses being demolished,
  which held `DwellingTuning.MaxUnderway` and stopped new houses and renewals until the last wall was gone.
- **Stores have an era form.** Only the storage hut existed, so storage could never count as renewed.
  `Buildings_StorageForms.xml` adds a store for each era from the Agrarian age on: granary (Granaries),
  storehouse (Bureaucracy), horreum (Markets), tithe barn (Feudalism), warehouse (Railways), distribution centre
  (GlobalMarkets) and automated depot (AutonomousDrones). Each is one cell and made from stuff, and its hit points
  and work rise with the era. These figures are this port's own. `GenConstruct.ReplaceTag` puts all stores in one
  family (`Storage`). `StoneWallMaterials.EquivalentsOf` counts any store toward the storage need, so a renewed
  hut is not built again.
- **Benches are rebuilt in a better material.** A one-cell bench that is made from stuff and takes bills replaces
  itself in place (`Bench:<defName>`) in the best material held to spare: a building metal, then cut stone, then
  wood. It keeps its bills across the swap (`Frame.CompleteConstruction`), because nobody here would set them
  again. `StructureRenewal` runs after the homes in the daily pass, under the same waits. Its budget is
  `RenewalTuning.MaxUnderway`, separate from the homes' and walls' budgets, with the same 1.5 times reserve and
  wood exempt. Stores go first, then the bench furthest behind.
- **A bench's or wall's form is its material's era too.** `RenewalSummary` dates a store by its def, and a bench or
  wall by the later of its def's era and its material's (`StructureRenewal.FormEraOrder`). A material's era is that
  of the earliest recipe that makes it (`StructureRenewal.MaterialEraOrder`), not `EraAvailability.EraOf`. `EraOf`
  gives no era to anything that can also be dug up, and RimWorld maps carry compacted steel, so steel read as a
  first-era material.
- **A stalled renewal is called off, not downgraded.** `ReplanStalledSites` re-plans a site that is short of its
  stuff in whatever is affordable, which is usually wood. It now counts the ledger as within reach. A site over a
  standing building is cancelled instead of re-planned, because a wooden bench rebuilt in wood is no renewal and
  would be planned again the next day.
- **Production gap, not closed here.** Steel (Medieval) is the latest building material any chain makes. Nothing
  later is produced: the electric smelter needs power, and no settlement builds power. So from the Industrial age
  on, benches and walls can renew no further, and only homes and stores can be in the current era's form.
- **House research comes late in each era.** The agenda studies the cheapest projects in the frontier era first,
  and each era's house project is one of its dearest. In one probe, `UrbanSanitation` finished 160 days into the
  Industrial age. Homes still count toward the measure in the previous era's form, so this is reported rather than
  changed.
- **Babies take no part in the social sweep.** `SocialInteractionManager` now skips a pawn whose life stage is
  `Baby`, as an initiator or a recipient. This is recalled from Biotech, not read from its source:
  `LifeStageDef.canInitiateSocialInteraction` is false for a baby, and `InteractionUtility` keeps a baby from being
  picked for random interactions. Before this, a newborn collected "Insulted" memories (about -9) from adults.
