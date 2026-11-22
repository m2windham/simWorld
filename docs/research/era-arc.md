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

## 9. Era pacing (2026-11-22)

Added by the `era-pacing` lane. Sections 1 to 8 measured and fixed nothing.
This section records a fix and measures it against them. It serves all eight
eras: each era should last long enough for the settlement to build in it and
live in it, and research should come from the settlement.

### 9.1 Intent and translation

The intent: each era lasts long enough to be built in and lived in, and the
ladder spans a long game. Tech-reachability used a 50-year horizon. The horizon
decision in `docs/status.json` says years are not the currency, because a
quiet century costs nothing to pass on the abstracted clock. That is what makes
a long arc playable, not a reason against one.

Translated: the years a settlement of 25 spends with each era current.

| era current | intended years | intended turn of the next era |
| --- | --- | --- |
| SticksAndStones | 2 | Agrarian, day 120 (y2) |
| Agrarian | 3 | Bronze, day 300 (y5) |
| Bronze | 4 | Classical, day 540 (y9) |
| Classical | 5 | Medieval, day 840 (y14) |
| Medieval | 6 | Industrial, day 1,200 (y20) |
| Industrial | 7 | Information, day 1,620 (y27) |
| Information | 8 | Exotic, day 2,100 (y35) |

Exotic is then lived in from year 35, with the endless tail behind it. The
translation is in the research `translation` field of `docs/status.json`.
Nothing is gated on wall-clock.

### 9.2 What was built

RimWorld paces a colony with three things: project cost, the tech-level cost
factor, and how many researchers work benches. The player picks the project.
Each lever was kept and shaped for the arc:

- **Benches grow with the settlement and the era.** Each era has a
  `citizensPerScholar`: 25 for SticksAndStones and Agrarian, then 20, 18, 12,
  8, 6 and 5. The settlement wants one bench per that many citizens, rounded,
  and at least one (`WorksInitiativeTuning.ResearchBenchesWanted`). This
  replaces the flat 2 of §2.1. The ratios are not sourced, so the tests pin
  only the trend: never fewer benches with more people or a later era, and
  more for a band of 40 than for a band of 20 in every era.
- **Each era carries a cost factor.** `EraDef.researchCostFactor` multiplies
  every project in the era. It sits inside `ResearchProjectDef.CostFactor`,
  beside RimWorld's tech-level factor, so it acts where RimWorld's own
  multiplier acts and `baseCost` stays the authored relative price. Generated
  endless projects take the last era's factor, so the tail does not open
  cheaper than Exotic. `ResearchPerformed` is not touched; it already divides
  by `CostFactor`.
- **The agenda does not reach past the era it is working toward.**
  `ResearchAgenda` is still cheapest-first. It no longer takes a project from
  an era past the earliest incomplete one (`EraPacing.FrontierEra`). Run B's
  drift into `Xenobotany` while Information was unfinished (§2.1) is now a
  test case, found from content rather than by name.

### 9.3 How the factors were derived

While era X is current, the settlement researches the era after it. The
frontier agenda sweeps nearly all of that era, leaves included. So:

```text
factor(X+1) = years(X) x 60 x points per day at benches(X)
              / (authored cost of X+1 x tech factor)
```

Points per day were measured, not modelled, and the first calibration was
wrong. It assumed 350 points a scholar-day, read from runs A to D (147,000
effective points to the Medieval turn, by day 150 to 213, on two benches). A
probe on this branch showed one bench earning 380 to 700 points a day,
rising as its researcher learned. Research is the lowest-priority work type
(`naturalPriority` 300, as in RimWorld), so a bench is staffed by spare hands.
One bench is nearly always staffed; a second is staffed less. The 350 was a
two-bench average.

The rates used, measured with `era-survival`'s 4,000-tick research sittings
merged in, since both land in integration:

| benches | points a day | source |
| --- | --- | --- |
| 1 | about 600 | band 20 seed 99 probe: 358, 520, then 660 to 680 a day by month |
| 2 | about 390 a bench | runs A to D; band 40 seed 201 probe: 350 to 380 a bench |
| 3 or more | 390 a bench, assumed | not measured: no run has had three benches |

| era | authored cost | tech factor | benches paying for it | factor |
| --- | --- | --- | --- | --- |
| SticksAndStones and Agrarian | 16,600 | 1.0 | 1 | 4.3 |
| Bronze | 21,110 | 1.5 | 1 | 3.4 |
| Classical | 38,060 | 1.0 | 1 | 3.8 |
| Medieval | 60,930 | 1.0 | 1 | 3.0 |
| Industrial | 120,210 | 1.5 | 2 | 1.55 |
| Information | 161,370 | 1.5 | 3 | 2.0 |
| Exotic | 163,680 | 2.0 | 4 | 2.3 |

`EraPacingTests` recomputes each nominal era length from the loaded content
and pins it within 10% of the intent, so a project added to an era, or a
ratio changed, fails a test rather than a ten-hour run.

### 9.4 Before and after

`--suite arc`, threats off, solo world, sampled every 30 days, capped at 10
game years or 2.5 hours. "After" is this branch merged with `era-survival`
(`b5db21a`), the build integration will have. "Before" is §2. Turn days are
rebuilt from each project's finish day and the era spines, as in §1.1. For run
20/99 they match the suite's own table and `EraReached` days exactly.

| era turned | intended | A before (20/99) | after 20/99 | B before (20/2024) | after 20/2024 | C before (40/201) | after 40/201 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Agrarian | d120 | d41 | d125 | d30 | d154 | d38 | d111 |
| Bronze | d300 | d76 | d266 | d56 | d313 | d70 | d241 |
| Classical | d540 | d135 | d462 | d98 | not by d360 (3/14 spine) | d121 | not by d300 (6/14 spine) |
| Medieval | d840 | d213 | not by d600 (6/13 spine) | d150 | | d183 | |
| stopped | | | 10 y, 95 min | | 6 y, 2.5 h cap | | 5 y, 2.5 h cap |

Days with each era current:

| era | intended | before (A, B, C) | after 20/99 | after 20/2024 | after 40/201 |
| --- | --- | --- | --- | --- | --- |
| SticksAndStones | 120 | 30 to 41 | 125 | 154 | 111 |
| Agrarian | 180 | 26 to 35 | 141 | 159 | 130 |
| Bronze | 240 | 42 to 59 | 196 | | |

What this shows:

- **The opening is on intent.** Agrarian turned on day 111 to 154 against 120,
  where it had turned on day 30 to 41. Agrarian and Bronze now last 130 to
  196 days, where they had lasted 26 to 59. They run 12 to 28% short of
  intent: the single bench earns a little more than 600 once its researcher
  matures. That is a calibration point to move, not a shape problem.
- **Each era is researched whole before the next turns.** In run 20/99 every
  Agrarian, Bronze and Classical project was finished (27, 31 and 31), and
  the next era's first project waited until its own era was the frontier.
- **The turns are announced.** With `era-survival`'s fix merged, each turn
  wrote an "Era reached" moment, three in 20/99 and two in 20/2024.
- **A band of 40 researches faster, though not by much.** It turned Agrarian
  and Bronze 14 to 72 days earlier than the band-20 seeds, on two benches
  against one. A second bench adds about 25%, not 100%, because it is staffed
  by spare hands (§9.3).

### 9.5 What this did not determine

- **Medieval onward, measured.** No run reached Medieval: the intended turn is
  year 14, and the bench affords 5 to 10 years in 2.5 hours. The later eras
  are paced by the derivation and the content test, not by a played run.
- **Three or more benches.** The late factors assume 390 points a bench. If a
  third bench is staffed as thinly as a second, the late eras run longer than
  intended. That is the next thing to measure, and the lever is
  `citizensPerScholar` or the rate in the derivation, not a per-seed tune.
- **A bigger settlement researches more, but weakly.** Two benches earn about
  1.3 times what one does. A settlement that wants research to scale with
  people would need researchers who do not lose their bench to every other
  job. That is a work-priority question this lane did not take on.
- **The rate depends on `era-survival`.** The calibration was measured on the
  merged build. On this branch alone the single-bench rate was about the
  same (383 to 705 a day against 358 to 683).
