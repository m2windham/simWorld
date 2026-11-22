# The era arc, measured

SimWorld is one settlement rising through eight eras. Nobody had measured how a
real played game moves through them. The 60-day bench ends in Agrarian, Bronze
or SticksAndStones, and `tech-reachability.md` measured the research tree on
its own, with a modelled economy. This document records long runs of the real
simulation: citizens, research at a bench, the construction and works
initiatives, and the storyteller. It measures and diagnoses. It fixes nothing.

In short:

- **Research climbs fast, then slows.** A band of 20 is Medieval in 2.5 to 3.5
  game years and Industrial in 5.4 to 9.6. The best seed reached Information
  at year 8.6, and run D at year 15.9. No run came near Exotic: none finished
  any of its seven spine projects.
- **Nothing else changes with the era.** The settlement builds the same dozen
  kinds of building in Industrial and Information as in its second year. The
  last new kind is Bronze-gated. Of 245 research projects, 14 gate anything at
  all, and Classical and Exotic gate nothing.
- **No player ever sees an era turn.** In played games the engine never raises
  `EraReached`, so there is no era letter, no chronicle line and no "Era
  reached" moment (§5.1).
- **The settlement does not grow into a civilization.** No run went past 51
  people, and no citizen ever left Full tier. Three of five runs went
  extinct: two through disease and hunger (one of them in the Information
  age), and one through a single raid.
- **Long runs are expensive, and they get more expensive as they go.** A game
  year cost 4 to 5 minutes for a small, quiet settlement. It cost 30 to 45
  minutes for one of 35 to 50 people after a few years. Neither surviving seed
  got near 40 years in 3 hours.

## 1. Method

### 1.1 The instrument

`tools/bench --suite arc` (new, `Suites/ArcSuite.cs`) sets up a game the way
`--suite storyteller` does: `TribalStart`, a solo world by default, the
founding band from `--band`, and threats per `--threats`. It opens the
settlement, so citizens live on a real map at Full tier, and ticks for
`--days`. Every `--arc-every` days (default 60, one game year) it prints a
sample at once, so a run stopped partway still leaves its readings:

- era (`ResearchManager.CurrentEra`), progress into the next era, projects
  finished and the current project;
- population, by tier (Full, Interval, Statistical);
- every completed artificial building on the settlement map, by def, plus
  blueprints and frames;
- deaths by cause (`DeathLedger`), and food nutrition on the map and in the
  settlement's store ledger;
- the storyteller's threat points (`DefaultThreatPointsNow`) and the wealth
  they read;
- new moments from `MomentCurator`;
- wall-clock seconds for the interval.

A building's era is the latest era among its `researchPrerequisites`. A
building with none is "ungated". An era turn is read two ways: the engine's
`EraReached` event, and `CurrentEra` checked after every finished project.
For runs that were stopped before printing their summary, the turn day is
rebuilt from each project's logged finish day and the era spines
(`EraDef.IsComplete`). On runs A and E the rebuilt days match the printed
ones exactly.

```sh
dotnet run -c Release --project tools/bench/SimWorld.Bench -- \
  --suite arc --days 2400 --arc-every 60 --band 20 --seed 99 --threats false
```

### 1.2 Runs

| run | band | seed | world | threats | asked | reached | stopped by |
| --- | --- | --- | --- | --- | --- | --- | --- |
| probe | 20 | 99 | solo | off | 1 y, 3 y | 1 y, 3 y | done |
| A | 20 | 99 | solo | off | 40 y | 16 y | extinction |
| B | 20 | 2024 | solo | off | 40 y | 9 y | 3 h wall-clock cap |
| C | 40 | 201 | solo | off | 40 y | 5 y | 3 h 15 min wall-clock cap, in year 6 |
| D | 20 | 99 | solo | on | 40 y | 18 y | extinction |
| E | 20 | 99 | populated | on | 40 y | 4 y | extinction |

"Threats off" is Medium with big and intro threats disallowed, the same
baseline arm `--suite storyteller` uses. Disease, weather, manhunters and
other small incidents still fire. In a solo world no rival exists until
emergence founds one, so `RaidEnemy` has nobody to raid with. Run E was
added for that reason: it uses the same seed in a world that already has
rivals.

The 40-year target was cut by the brief's own rule: a run that would take more
than about 3 hours of wall-clock time is shortened, and this document says so.
The cost section shows why the cut came so early.

The runs are deterministic. Day 30 of the 1-year probe, the 3-year probe and
run A agree on every column. Runs A, D and E share a seed and agree until the
first incident that differs.

## 2. Era timeline

The day each era turned, meaning the day its spine and every earlier spine
were finished. SticksAndStones is current from the start; its row is the day
its spine finished.

| era | A: s99 off | B: s2024 off | C: s201 b40 off | D: s99 on | E: s99 on, populated |
| --- | --- | --- | --- | --- | --- |
| SticksAndStones | d7 | d5 | d7 | d7 | d7 |
| Agrarian | d41 (y0.7) | d30 (y0.5) | d38 (y0.6) | d42 (y0.7) | d37 (y0.6) |
| Bronze | d76 (y1.3) | d56 (y0.9) | d70 (y1.2) | d78 (y1.3) | d67 (y1.1) |
| Classical | d135 (y2.2) | d98 (y1.6) | d121 (y2.0) | d133 (y2.2) | d139 (y2.3) |
| Medieval | d213 (y3.5) | d150 (y2.5) | d183 (y3.0) | d206 (y3.4) | extinct y4 at 3/13 spine |
| Industrial | d573 (y9.6) | d322 (y5.4) | not by y5 (12/16 spine) | d549 (y9.2) | |
| Information | extinct y16 at 17/21 spine | d516 (y8.6) | | d954 (y15.9) | |
| Exotic | | not by y9 (0/7 spine) | | extinct y18 at 0/7 spine | |

Days spent in each era:

| era | A | B | C | D |
| --- | --- | --- | --- | --- |
| Agrarian | 35 | 26 | 32 | 36 |
| Bronze | 59 | 42 | 51 | 55 |
| Classical | 78 | 52 | 62 | 73 |
| Medieval | 360 | 172 | > 117 | 343 |
| Industrial | > 387 | 194 | | 405 |

Research finished per game year, run A:

| year | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 | 16 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| projects | 69 | 35 | 27 | 18 | 8 | 6 | 5 | 7 | 7 | 7 | 7 | 8 | 7 | 1 | 0 | 1 |

Projects finished by year, all runs:

| year | A b20 | B b20 | C b40 | D b20 | E b20 |
| --- | --- | --- | --- | --- | --- |
| 1 | 69 | 83 | 73 | 69 | 75 |
| 2 | 105 | 127 | 112 | 106 | 108 |
| 3 | 131 | 152 | 141 | 133 | 122 |
| 4 | 149 | 167 | 157 | 150 | 122 |
| 5 | 157 | 181 | 170 | 158 | |
| 9 | 182 | 225 | | 185 | |

### 2.1 Why the pace has that shape

The first five eras, SticksAndStones to Medieval, cost 136,130 points at
base cost. `ResearchAgenda` picks the cheapest startable project and gets
through all five spines in 2.5 to 3.5 years. After that the price rises
steeply: Industrial alone is 116,610 points and Information 161,370.
`CostFactor` also charges 50% extra for each tech level above the
researcher's, so a Medieval civilization pays 1.5x for Industrial work.

Throughput does not rise to meet the price. `WorksInitiativeTuning.ResearchBenchesWanted`
is a flat 2, so at most two people research at a time, whatever the
population. Run C has twice the band of run A and finished 73, 112, 141, 157
and 170 projects by years 1 to 5. Run A finished 69, 105, 131, 149 and 157.
Doubling the people bought about 8% more research.

Cheapest-first also wanders across eras. By year 8 run B was studying
`Xenobotany`, an Exotic project, while Information was unfinished. At year 9
it had touched none of Exotic's seven spine projects.

## 3. What gets built, era by era

### 3.1 What the content allows

Every building def a settlement could put up, by the era of its research gate:

| gate era | defs | which |
| --- | --- | --- |
| ungated | 10 | Bed, Door, House_Paleo, ResearchBench, Sculpture, SculptureGold, SculptureJade, StorageHut, TableTailor, Wall |
| SticksAndStones | 4 | FueledStove, House_Meso, Smithy, TableButcher |
| Agrarian | 2 | House_Neo, TrapSpike |
| Bronze | 5 | House_Chalco, TableStonecutter, WallGranite, WallLimestone, WallSandstone |
| Classical | 0 | |
| Medieval | 2 | TrapIED, TrapIEDIncendiary |
| Industrial | 4 | Battery, Heater, PowerConduit, WoodFiredGenerator |
| Information | 1 | Turret_Mini |
| Exotic | 0 | |

Across all content, 14 of the 245 projects gate anything: a building, a recipe
or a weapon. Classical (31 projects) and Exotic (25) gate nothing. The few
buildings gated past Bronze (traps, power, heater, turret) are placed by no
initiative. No code in `src/SimWorld.Core` names `Heater`, `Turret_Mini`,
`WoodFiredGenerator`, `PowerConduit`, `Battery`, `TrapIED` or `TrapSpike`.
A settlement left to its own initiative cannot put one up, whatever it has
researched.

### 3.2 What the runs built

Run A, by def:

| def | gate era | y1 | y2 | y4 | y6 | y8 | y10 | y12 | y14 | y16 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| StorageHut | ungated | 253 | 413 | 493 | 539 | 661 | 661 | 661 | 589 | 661 |
| Wall | ungated | 68 | 68 | 51 | 45 | 44 | 42 | 39 | 39 | 39 |
| Bed | ungated | 20 | 20 | 20 | 20 | 20 | 20 | 20 | 20 | 20 |
| Sculpture | ungated | 12 | 12 | 12 | 12 | 12 | 12 | 12 | 12 | 12 |
| House_Paleo | ungated | 11 | 11 | 11 | 11 | 11 | 11 | 11 | 11 | 11 |
| ResearchBench | ungated | 2 | 2 | 2 | 2 | 2 | 2 | 2 | 2 | 2 |
| Door | ungated | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| TableTailor | ungated | - | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| FueledStove | SticksAndStones | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| TableButcher | SticksAndStones | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| Smithy | SticksAndStones | - | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| House_Neo | Agrarian | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |
| House_Chalco | Bronze | - | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

Run B, which got furthest up the ladder:

| def | y1 Bronze | y2 Classical | y3 Medieval | y6 Industrial | y9 Information |
| --- | --- | --- | --- | --- | --- |
| StorageHut | 135 | 203 | 226 | 226 | 227 |
| Wall | 39 | 39 | 39 | 39 | 39 |
| Bed | 20 | 20 | 20 | 20 | 20 |
| Sculpture | 12 | 12 | 12 | 12 | 12 |
| House_Paleo | 10 | 10 | 10 | 10 | 10 |
| House_Chalco | 2 | 2 | 6 | 9 | 14 |
| ResearchBench | 2 | 2 | 2 | 2 | 2 |
| House_Neo, Door, FueledStove | 1 each | 1 each | 1 each | 1 each | 1 each |
| Smithy, TableTailor | - | 1 each | 1 each | 1 each | 1 each |

Kinds of building standing, against the era:

| run | year the last new kind appeared | its era then | kinds standing at the end | era at the end |
| --- | --- | --- | --- | --- |
| A | 2 | Bronze | 13 | Industrial |
| B | 2 | Classical | 12 | Information |
| C | 2 | Bronze | 12 | Medieval |
| D | 2 | Bronze | 13, falling to 12 as the empty settlement decayed | Information |
| E | 2 | Bronze | 12, then 5 after the raid | Classical |

**Does what gets built change with the era? No.** In every run the last new
kind of building appears in year 2, in Bronze or Classical. Nothing changes
after that, through Medieval, Industrial and, in run B, Information, apart
from three things: the number of storage huts, the slow loss of walls, and
`House_Chalco`. When a new household forms, `DwellingUtility` builds it the
latest dwelling the research allows. That is a Bronze-age house, and it is the
only building in any run that responds to the era. Existing households keep
their Paleolithic houses: in run B, 10 of the 25 houses standing in the
Information age are `House_Paleo`.

Walls erode in runs A, C, D and E (68 to 39 in run A) and no initiative
replaces them. Run B's 39 walls stood unchanged for nine years.

### 3.3 Storage huts

`StorageHutNeed` wants one hut per three stacks in the store ledger, and it
has no cap. The huts are the one thing that grows across the eras, and they
grow because stock grows, not because the era changes:

| run | peak huts | share of all buildings | people then |
| --- | --- | --- | --- |
| A | 661 | 87% | 9 |
| B | 227 | 69% | 39 |
| C | 1,459 | 92% | 51 |
| D | 675 | 88% | 15 |

## 4. Population, deaths, food

| run | start | peak | end | died: disease | starvation | injury | age | outcome |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| A | 20 | 25 (y1) | 0 (y16) | 35 | 9 | 2 | 0 | extinct in Industrial |
| B | 20 | 39 (y9) | 39 | 11 | 1 | 5 | 0 | growing slowly at cutoff |
| C | 40 | 51 (y5) | 51 | 5 | 1 | 5 | 0 | growing slowly at cutoff |
| D | 20 | 22 (y1) | 0 (y18) | 24 | 15 | 8 | 0 | extinct in Information |
| E | 20 | 20 | 0 (y4) | 11 | 3 | 14 | 0 | extinct in Classical |

No citizen in any run ever ran at Interval or Statistical tier, because no
population came near `TieringTuning.FullTierBudget`. Nobody died of age: the
oldest lives reached the low to mid forties, and the "Longevity" moments mark
records at 38 to 46 years.

Run A, the collapse in detail:

| year | era | pop | disease | starvation | injury | food on map | food in stores |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Agrarian | 25 | 2 | 0 | 0 | 142 | 2,599 |
| 2 | Bronze | 21 | 8 | 0 | 0 | 117 | 3,964 |
| 4 | Medieval | 16 | 13 | 0 | 1 | 207 | 4,702 |
| 6 | Medieval | 15 | 14 | 4 | 1 | 90 | 5,194 |
| 8 | Medieval | 9 | 22 | 4 | 2 | 54 | 6,222 |
| 10 | Industrial | 7 | 26 | 4 | 2 | 291 | 4,896 |
| 12 | Industrial | 5 | 32 | 4 | 2 | 83 | 3,366 |
| 13 | Industrial | 4 | 34 | 4 | 2 | 24 | 274 |
| 14 | Industrial | 1 | 34 | 8 | 2 | 0 | 0 |
| 16 | Industrial | 0 | 35 | 9 | 2 | 0 | 33 |

Births and wanderers added about 26 people in 16 years, and deaths took 46.
Disease is the main cause in every threat-free run.

**People starve beside full stores.** In run A, four people starved in years 5
and 6 while the store ledger held about 5,000 nutrition and the map held 90 to
100. In run E the last three died, of starvation and injury, with 6,040
nutrition in the ledger. Run D shows the same thing: three people starved by
year 5 with 6,106 nutrition banked. In runs A and D the ledger then emptied
within about a year (D: 1,184 at year 12, 6 at year 13), and the rest starved
outright. These runs do not show
why ledger food did not reach the people who needed it. The ledger-to-map
crossing exists (`SettlementStockInitiative`), so this is a measured symptom
whose cause is not yet known.

## 5. What the player would see

### 5.1 No era is ever announced

`ResearchManager.ResearchPerformed` writes the new progress before it calls
`FinishProject`:

```csharp
progress[currentProj] = newProgress;
if (newProgress >= currentProj.baseCost)
{
    FinishProject(currentProj, false, researcher);
}
```

`FinishProject` reads `eraBefore = CurrentEra` to bracket the change. By then
the project already counts as finished, so `eraBefore` is already the new era
and `CheckEraTransition` sees no crossing. Research work
(`JobDriver_Research`) is the only source of research points. So in played
games `EraReached` never fires, `EraTransitionUtility.Notify_EraReached` never
runs, and no chronicle line, "A new era" letter or "Era reached" moment is
ever written. `MomentCurator` treats that moment as always worth keeping, and
it is the one moment meant to mark the arc.

Measured across every run: `CurrentEra` moved through four to seven eras, the
`EraReached` column stayed empty, and the moments contain no "Era reached"
entry. The era tests call `FinishProject` directly (`EraTransitionTests`),
which is the one path where the order is right.

### 5.2 The chronicle goes quiet, even through disaster

`MomentCurator` keeps the first of each kind, records, and era turns. Era
turns never arrive (§5.1), so a run uses up its kinds in the first year and
then falls almost silent:

| run | moments in year 1 | after year 1 | years after year 1 |
| --- | --- | --- | --- |
| A | 17 | 12 | 15 |
| B | 17 | 8 | 8 |
| C | 18 | 5 | 4 |
| D | 18 | 14 | 17 |
| E | 23 | 7 | 3 |

The worst days in these runs left almost nothing behind:

- In run A's year 7, six people died and no moment was recorded. A death from
  disease had already been seen.
- In run E, one raid between days 120 and 180 killed 10 of 15 people. The only moment
  from it is "Raiders from Steel reach have given up and are leaving."
- None of the three extinctions is a moment. The last moment in run E is a
  death on day 154, and in run D a death on day 888, about three years before
  the last person died in the Information age. Each settlement died out,
  recorded as nothing.

### 5.3 Threats

With threats off, run A's points still rose from 147 to a peak of 1,357 in
year 6, and fell as the population did. They are driven by wealth, and wealth
here is mostly the store ledger, which is what the storage huts hold.

With threats on in a solo world (run D), points rose from 147 to 614 by year
1 and about 3,000 by year 10. Wealth reached 99,000. In 18 years no raid
came, because no rival existed. The threats that did fire were manhunters (day 4),
disease, weather and toxic fallout. Run D's injury deaths (8 by year 8) are
about four times run A's (2), yet its population ran 13 to 15 against run
A's 7 to 9 at the same years. The two runs diverge from day 4, so the gap
cannot be put down to threats alone. Run D ended the way run A did: the bank
emptied in year 13, and starvation (15 deaths in all) and disease (24) took
the last eight people by year 18, two and a half years after Information
turned.

With threats on in a populated world (run E), the first raid came on day 11
and fled. Between days 120 and 180, in Classical, with the points reading about 750,
a raid killed
10 of the 15 people left. The settlement had nothing to defend with: walls had
eroded from 118 to 67, and no trap or turret is ever placed (§3.1). It was
extinct by year 4.

So the storyteller's number climbs with stored goods, while the settlement's
ability to meet a threat does not change with the era at all. The era factor
(`threatPointsFactor`, 1.0 to 2.8) scales the threat up across the ladder.
Nothing on the settlement's side scales with it.

## 6. Cost

Wall-clock minutes per game year, measured per interval. Every run is single-
threaded, and the machine was shared with other lanes' bench and test
processes throughout. The "mine" column counts my own concurrent runs.

| run | year 1 | year 3 | year 5 | year 8 | year 9+ | total | mine running |
| --- | --- | --- | --- | --- | --- | --- | --- |
| probe 1 y | 3.9 avg | | | | | 3.9 min | 1 |
| probe 3 y | 4.4 | 4.5 | | | | 13.5 min | 1 |
| A | 3.2 | 3.8 | 4.6 | 3.9 | 2.1 to 5.6 | 66 min for 16 y | 2 to 4 |
| B | 6.7 | 10.2 | 16.6 | 42.6 | 35.4 | 179 min for 9 y | 2 to 4 |
| C (band 40) | 14.0 | 24.0 | 44.8 | | over 70 in y6 | 195 min for 5 y, y6 unfinished | 3 to 4 |
| D | 4.5 | 13.5 | 9.2 | 8.5 | 2.6 to 11.5 | 151 min for 18 y | 2 to 4 |
| E | 10.1 | 15.8 | | | | 36 min for 4 y | 4 |

What the table supports:

- A small settlement costs about 4 minutes a game year on a quiet machine
  (the probes). That is 13,000 to 15,000 ticks a second.
- Cost rises with population. Between 23:30 and 01:05, with four of my runs
  going at once, run D at 13 to 15 people cost 8 to 11 minutes a year, run B
  at 33 to 39 cost 18 to 43, and run C at 48 to 51 cost about 45.
- Cost also rises with time at a fixed population. Run B went from 6.7 to
  42.6 minutes a year while its population grew by half. More concurrent load
  during that window explains part of the rise but not all of it.
- A settlement does not get cheap just because it empties. Run A cost 5.5
  minutes a year with five people, against 3.2 with 25 in year 1, while 661
  storage huts stood on its map. One more of my runs was going by then, so
  part of that is load. Run D did get cheaper at the very end, 2.6 to 4.6
  minutes a year with two people or none, as its huts decayed from 675 to
  306. Per-year cost tracks what has piled up on the map as well as who
  lives there.

At these rates a settlement that collapses, like run A, fits 16 years into
about an hour. One that survives, like run B at 35 to 45 minutes a year by
year 8, would need more than a day for 40 years, and a band of 40 longer
still. The arc cannot be watched end to end on this bench as it stands.

## 7. Diagnosis

### 7.1 What era progression feels like

It feels **too fast, then too slow, and invisible throughout.**

Agrarian, Bronze and Classical each last one to two months of game time, 26
to 78 days. A player would see four eras turn in the first two and a half to three
and a half years, and nothing in the settlement would change when they did. Medieval
then lasts six months to a year and Industrial a year or more. Through those
years the settlement builds nothing new, its people slowly die, and the
chronicle goes quiet. Across all of it the player is never told an era has
turned (§5.1).

### 7.2 What never changes across the eras, and should

- **What is built.** The same dozen kinds of building, settled by year 2.
- **How people are housed.** A house built in year 1 is never replaced. In
  run B, 10 of 25 houses are still Paleolithic in the Information age, and
  no dwelling past Bronze exists in content.
- **Defence.** Walls erode and nothing replaces them. Traps, turrets and
  stone walls exist in content but are never placed.
- **Research capacity.** Two benches and two researchers, from the first
  hearth to computing.
- **The population tier.** Every citizen is a Full pawn in every run. The
  Interval and Statistical machinery never engages.
- **What the chronicle records.** The kinds are used up in year 1, and the
  era turns that should keep it going never arrive.

### 7.3 The five gaps that matter most

1. **Era turns are silent.** `EraReached` cannot fire from research work
   because of the order of operations in `ResearchPerformed` (§5.1). It is
   the cheapest gap and it touches every other one: no letter, no chronicle
   line, no moment, and nothing for any system to hang an era change on. The
   evidence is every run here: four to seven era turns each, zero events.

2. **Research unlocks nothing the settlement uses.** 14 of 245 projects gate
   anything. Classical and Exotic gate nothing. The few later buildings that
   are gated (power, heater, traps, turret) have no initiative that places
   them. Every run's building table is flat from year 2 (§3.2). The ladder is
   a counter, not a cause.

3. **Research pace is cut off from the settlement.** Two benches, whatever
   the population, so a band of 40 researches 8% faster than a band of 20
   (§2.1). The cheapest-first agenda clears five eras in under four years and
   then grinds, and it drifts into Exotic leaves while Information's spine
   waits. Nothing the settlement becomes changes how fast it climbs.

4. **The settlement does not grow, and it dies of things the era should
   answer.** Population peaks at 25 to 51, and disease is the main killer in
   every threat-free run. Herbal remedies, germ theory, antiseptics,
   vaccination and hospitals were all researched in run A before its people
   died of infection. Three of five runs went extinct: run A in Industrial,
   run D in Information, run E in Classical. People starve beside thousands
   of units of banked food (§4).

5. **Threat scales with the ladder and with hoarded stock. Defence scales with
   neither.** Threat points come mostly from the store ledger, which the
   uncapped storage huts keep growing, and the era factor multiplies them.
   The settlement's defences do not change and in fact erode. In a populated
   world, one Classical-era raid at about 750 points ended a 15-person
   settlement (§5.3). In a solo world the points climb to 3,000 with nobody to
   spend them.

Two smaller findings belong beside these. The chronicle does not record its
own worst days: a raid that killed two thirds of the people, and three
extinctions, left almost nothing (§5.2). And the bench cannot afford the arc:
cost grows with population and with what piles up on the map, so 30 to 50
years of a surviving settlement takes more than a day (§6).

## 8. What this could not determine

- **Why banked food does not reach starving people.** It is measured, not
  explained.
- **Whether the Exotic era is reachable in play.** No run got there.
  Run B was the closest and had finished none of Exotic's spine by year 9.
- **Raid frequency over time.** The instrument records moments, not every
  incident, so after the first raid later ones show only as injury deaths.
  `--suite storyteller` has the per-incident log, and over decades it would
  be the right tool.
- **Cost on a quiet machine.** Runs B to E overlapped with each other and with
  other lanes' work. The probes are the clean figure.

## 9. After era-survival (2026-11-22)

The `era-survival` lane took five of the defects above. Each is listed with the
cause found, what changed, and the test that pins it. Runs A and D were then
repeated with the fixes in place. Every era is affected: the era-turn fix and
the research sitting touch every era from Agrarian on, and the store ceiling
and ring walls hold from the first year.

### 9.1 Era turns are announced (§5.1, §7.3 gap 1)

The cause was as §5.1 described. `ResearchPerformed` now leaves the finishing
amount to `FinishProject`: the `progress[currentProj] = newProgress;` line
moved below the finish check, and the finishing branch returns. That is the
only change to `ResearchManager`.
`EraTransitionTests.Research_work_that_finishes_an_era_announces_it` turns an
era through `ResearchPerformed`, the path research work uses. It asserts the
event, the chronicle line, one letter and the `MomentCurator` moment.

### 9.2 People starved at the research bench (§4)

A diagnostic rerun of run A was stopped at about day 100. Of the people who were
starving, seven were on a `Research` job with food at 0.00. The map held about
128 nutrition, which was its full reserve, and the ledger held about 4,300.
The food was in reach. Nobody left the bench to get it.

`JobDriver_Research`'s toil was `ToilCompleteMode.Never`, so a research job
ran until the project finished. A project costs thousands of points and takes
months, and nothing else interrupts a running work job. RimWorld's toil is a
`Delay` of 4000 ticks (`defaultDuration = 4000`). This value comes from memory
of the decompile and was not checked against the source in this lane. After
the sitting the think tree is asked again and hunger can win. This is a
defect, not faithful behaviour: RimWorld researchers stop to eat.
`ResearchWorkTests.A_hungry_researcher_on_a_long_project_leaves_the_bench_to_eat`
pins it.

The ledger draining to zero in years 12 and 13 was not reproduced. Neither
rerun lasted that long, and the bank was still full at the end of both
(§9.6).

### 9.3 Storage huts: the food store had no ceiling (§3.3)

The huts tracked the ledger exactly. By day 60 of the diagnostic run the
ledger held 857 stacks (RawRice 40,181 units, RawBerries 19,637, and others),
so the need was 286 huts and 287 stood. The need never saturated because the
food kept coming in. `FarmingInitiative` sizes the field at three times
break-even (`FieldSizeMargin`), foraging and hunting add to that, and banked
food never spoils (`CompRottable` is only on corpses). Stock grew by about
1,500 to 3,000 nutrition a year, and at 3.75 nutrition per raw-food stack
that is many huts.

The field now lies fallow (`Zone_Growing.allowSow = false`) while map plus
ledger hold a year of what the settlement eats
(`FarmingTuning.DaysOfFoodStored` = `GenDate.DaysPerYear`). It is sown again
once the store falls below that. This is the RimWorld player's sowing toggle,
and `SettlementSubsistence` already describes the store as "a working larder,
not an ever-growing pile". `StorageHutNeed` is unchanged: its job is to house
what is stored.
`FarmingInitiativeTests.A_store_holding_a_years_food_stops_sowing_and_an_emptied_one_starts_again`
pins the fallow rule.

### 9.4 Walls (§3.2, §7.2)

The settlement's own ring walls were not eroding. In the diagnostic run they
stayed at 39. Earthquakes on days 58 and 92 knocked walls down, and the
quake's rebuild blueprints and `SettlementCompound.CountFree` put them back.
The walls that fell, 48 to 38, were the map's factionless ruin walls. Nothing
repairs or rebuilds those, and RimWorld does not either. Buildings never
deteriorate in RimWorld or here (`CanEverDeteriorate` covers items only).

There was no product fix. The arc suite had counted ruins and own walls under
one name. It now lists ruins as `Wall (ruin)`.
`SettlementCompoundTests.A_ring_wall_destroyed_by_anything_is_laid_out_again_where_it_stood`
pins replacement.

### 9.5 Medical research unlocks nothing: a gap for the era-unlocks lane (§7.3 gap 4)

Tending works. The diagnostic run saw tend quality between 0.2 and 0.7. But no
medical project changes it:

- HerbalRemedies, BoneSetting, Surgery, Anatomy, Hospitals, Antiseptics,
  GermTheory, Vaccination and Microscope gate no medicine, recipe, bed or
  stat. Surgery appears only in `RecipeWorker` doc comments.
- The only code that reads the `MedicineHealth` tag is a newborn's lifespan
  budget in `Pawn_AgeTracker`.
- `MedicineIndustrial` has no recipe.
- No hospital bed ships. The one `Bed` has `MedicalTendQualityOffset` 0.
- Healroot is ungated.

So an Industrial settlement tends with herbal medicine (potency 0.6, quality
cap 0.7), as a Paleolithic one does. This is recorded in `docs/status.json`
(`health.medicalresearch`, not done) for the era-unlocks lane. This lane did
not build it.

### 9.6 Runs A and D again

Same settings as §1.2: band 20, seed 99, solo, threats off (A) and on (D), 600
days asked, built from `b5db21a`. Run A finished all 10 years. Run D was
stopped by its process ID at the 2-hour cap, after the year-9 sample.

Era turns, from the `EraReached` event, against the day `CurrentEra` changed:

| era | A before (§2) | A after | D before (§2) | D after |
| --- | --- | --- | --- | --- |
| Agrarian | d41, no event | d43, event d43 | d42, no event | d43, event d43 |
| Bronze | d76, no event | d75, event d75 | d78, no event | d85, event d85 |
| Classical | d135, no event | d148, event d148 | d133, no event | d150, event d150 |
| Medieval | d213, no event | d229, event d229 | d206, no event | d221, event d221 |
| Industrial | d573, no event | d525, event d525 | d549, no event | d516, event d516 |

Every turn now writes an "Era reached: X, after the Y." moment. Run A's
moments list holds five of them, plus "New edicts available" lines on the
turns that open edicts. Run A kept 34 moments in 10 years, 21 of them
after year 1. Before the fixes it kept 12 after year 1, over 15 years. Run D
kept 32 in 9 years, 15 of them after year 1.

Run A, threats off, year by year:

| year | era | pop | starvation | disease | injury | exposure | food in stores | storage huts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Agrarian | 22 | 0 | 0 | 0 | 0 | 2,105 | 208 |
| 2 | Bronze | 19 | 0 | 2 | 0 | 2 | 2,028 | 219 |
| 4 | Medieval | 22 | 0 | 4 | 2 | 3 | 2,665 | 277 |
| 6 | Medieval | 21 | 0 | 6 | 2 | 6 | 3,033 | 323 |
| 8 | Medieval | 21 | 0 | 7 | 3 | 8 | 3,474 | 382 |
| 10 | Industrial | 25 | 0 | 8 | 4 | 8 | 3,603 | 404 |

Run D, threats on, solo:

| year | era | pop | starvation | disease | injury | exposure | food in stores | storage huts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Agrarian | 26 | 0 | 0 | 0 | 0 | 2,301 | 223 |
| 3 | Classical | 27 | 0 | 2 | 0 | 4 | 2,666 | 301 |
| 5 | Medieval | 26 | 0 | 5 | 0 | 7 | 2,531 | 319 |
| 7 | Medieval | 26 | 0 | 6 | 1 | 9 | 2,688 | 363 |
| 9 | Industrial | 23 | 0 | 9 | 3 | 13 | 2,128 | 363 |

Against the measurements before the fixes (§3.3, §4):

- **Starvation: none in either run.** Before: run A had 4 deaths by year 6
  with about 5,000 nutrition banked, and run D had 3 by year 5 with 6,106.
- **Storage huts are bounded.** In run A they grow with the people, at 12.6
  huts per person in year 4 and 16.2 in year 10. Before, run A reached 661
  huts for 9 people in year 8. In run D the count sat at 363 from year 7 to
  year 9. The food store levels off at about 145 to 165 nutrition per person
  in run A from year 6, and drops back in run D. These counts are still not
  small. Raw food fills a stack with only 3.75 nutrition, so a year of food is
  hundreds of stacks. The store also runs above the one-year line, because
  hunting and foraging continue while the field lies fallow.
  `HuntingInitiative.WantsMeat` reads only the map. Its doc comment already
  says it should read the ledger once food can be issued from it, which is
  now the case. That change was left for the owner of the food economy.
- **Disease still kills.** Run A had 8 disease deaths by year 10, against 26
  before. Run D had 9 by year 9. The drop comes from people being fed, not
  from medicine (§9.5).
- **The population holds.** Run A had 25 people at year 10, against 7 before.
  Run D had 23 at year 9, against 13 to 15 before. Neither run went extinct
  within its window. The window is shorter than the original runs (16 and 18
  years), so this says nothing yet about their later collapse.
- **Exposure is now the largest cause in run D**, with 13 deaths by year 9.
  Exposure is the shelter lane's, and the pre-fix doc had no column for it.

### 9.7 21-seed 60-day bench, threats off

These are survival numbers from a 60-day bench. They show the fixes did not
break the colony scale. They are not evidence about the arc (§9.6 is). The
columns are houses, deaths and end population, compared against the shelter
lane's result on the same runner.

| seed | shelter h/d/pop | era-survival h/d/pop | era-survival deaths by cause |
| --- | --- | --- | --- |
| 20/99 | 11/2/19 | 12/3/19 | Disease 2, Injury 1; incidents: Disease_Plague 1 |
| 20/101 | 12/1/24 | 12/5/19 | Disease 3, Injury 2; incidents: Disease_Plague 1, Earthquake 2 |
| 20/102 | 11/1/22 | 12/4/19 | Disease 1, Injury 3; incidents: Earthquake 3 |
| 20/103 | 11/2/20 | 11/2/22 | Disease 2 |
| 20/104 | 13/4/21 | 12/0/24 | none |
| 20/105 | 11/1/22 | 10/4/18 | Disease 4 |
| 20/106 | 12/4/18 | 11/1/21 | Disease 1 |
| 20/107 | 11/3/20 | 10/0/20 | none |
| 20/108 | 11/2/21 | 11/1/22 | Disease 1 |
| 20/109 | 11/2/22 | 10/3/18 | Disease 3 |
| 20/110 | 0/13/9 | 2/2/18 | Disease 2 |
| 20/777 | 11/4/17 | 10/2/19 | Disease 2 |
| 20/2024 | 5/0/26 | 2/3/20 | Disease 2, Injury 1; incidents: Disease_Plague 1 |
| 20/12345 | 10/1/19 | 12/3/21 | Disease 3; incidents: Disease_Plague 2 |
| 20/31337 | 0/2/19 | 12/1/23 | Injury 1 |
| 40/201 | 21/2/43 | 21/10/34 | Starvation 8, Disease 1, Injury 1 |
| 40/202 | 19/3/40 | 20/2/42 | Disease 2 |
| 40/203 | 20/2/46 | 20/3/41 | Disease 3; incidents: Disease_Plague 2 |
| 40/204 | 21/1/42 | 21/4/42 | Disease 4 |
| 40/777 | 20/2/42 | 20/0/47 | none |
| 40/12345 | 20/3/40 | 20/6/38 | Disease 6 |
| total, 21 seeds | **261/55/552** | **271/59/547** | |

Across the 21 seeds there are 10 more houses, 4 more deaths and 5 fewer
people at the end. One seed, band 40 seed 201, carries more than the whole
difference: 8 deaths from starvation, against none in the shelter run.
Without that seed the totals are 250/49/513 against 240/53/509.

**Seed 40/201 is a famine with nothing left to eat, not hunger beside a full
store.** Two lane-private copies of `b5db21a` were run on this seed, each
with one fix turned off. With the store ceiling off, the result was unchanged:
the same 8 starvation deaths. With the research sitting off, nobody starved
(2 disease deaths). So the sitting change triggers it. A daily dump from both
runs shows the mechanism:

| | sitting on (this lane) | sitting off |
| --- | --- | --- |
| ledger food, day 30 | 358 | 554 |
| ledger empty | day 36 | day 40 |
| map food gone | day 40 | day 44 |
| harvest (wealth jumps) | about day 54 | |
| deaths | 8 starved, days 49 to 50 | none by day 51 |

In both runs the founding provisions ran out about two weeks before the
first harvest. After that, 39 people sat at food 0.00 with nothing on the map
or in the ledger, and spent the time on `Clean` and `LayDown`. Nobody hunted
or foraged. With the sitting on, the ledger empties four days sooner, and malnutrition
kills before the crop comes in. The most likely reason is that researchers
who leave the bench also eat. With the sitting off they stayed at the bench
and ate nothing. The runs also differ by one person at day 30 (40 people
against 39). Neither cause was traced pawn by pawn.

The underlying gap is in the food economy, not in this lane's change. The
founding provision (`SettlementLarderTuning.ProvisionNutritionPerMouth`, first
harvest plus 4 days) did not cover the time to the first harvest on this map,
and a settlement with no food at all sends nobody out to find any. Both
belong to whoever owns the food economy. This lane did not change either.
