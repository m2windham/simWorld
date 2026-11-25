# Era progression: the ladder, the edicts and the rivals

Branch `era-progression`. This doc covers four things. Section 1 defines the era a civilization is in.
Section 2 gives each era's gates, unlocks and pace. Section 3 lists the edicts the god can issue in each
era. Section 4 sets how fast the rival factions advance.

Section 5 lists the work left, in order. Section 6 gives the runs that will test it.

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
