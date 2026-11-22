# The era arc, measured

SimWorld is one settlement rising through eight eras. Until now nobody had
measured how a real played game moves through them. The 60-day bench ends in
Agrarian, Bronze or SticksAndStones, and `tech-reachability.md` measured the
research tree on its own with a modelled economy. This document records long
runs of the real simulation: citizens, research at a bench, the construction
and works initiatives, and the storyteller. It measures and diagnoses. It
fixes nothing.

The short version:

- **The research ladder moves fast, then slows sharply.** A band of 20 reaches
  Medieval in 3 to 4 game years and Industrial at about year 10. After that it
  slows to a crawl. Nothing on a seed reached Exotic.
- **Nothing else moves with it.** The settlement builds the same 13 kinds of
  building in Medieval and Industrial as it did in year 2. The last new kind
  appears in Bronze. Of 245 research projects, 14 gate anything, and Classical
  and Exotic gate nothing.
- **Era progression is invisible.** In played games the engine never raises
  `EraReached`, so no era letter, no chronicle line and no "Era reached"
  moment is ever produced (§4.1).
- **The settlement does not grow.** Population never went above the founding
  band plus a handful. Disease removes people faster than births add them, and
  one of the three threat-free seeds went extinct in year 16.
- **What a long run costs** is set by what is on the map, not by how many
  people are on it. A game year costs 3 to 6 minutes with 20 people and about
  14 minutes with 40.

## 1. Method

### 1.1 The instrument

`tools/bench --suite arc` (new: `Suites/ArcSuite.cs`) sets up a game the same
way `--suite storyteller` does. It uses `TribalStart`, a solo world, the
founding band given by `--band`, and `--threats`. It opens the settlement so
its citizens are on a real map at Full tier, and ticks it for `--days`. Every
`--arc-every` days (60, one game year) it prints one sample as soon as it is
taken, so a run that is stopped partway still leaves its readings:

- era (`ResearchManager.CurrentEra`), progress into the next era, projects
  finished, the current project;
- population, split by tier (Full, Interval, Statistical);
- every completed artificial building on the map, counted by def, plus
  blueprints and frames;
- deaths by cause (`DeathLedger`), food nutrition on the map and in the
  settlement's store ledger;
- the storyteller's threat points (`DefaultThreatPointsNow`) and wealth;
- new moments from `MomentCurator`;
- wall-clock seconds for the interval.

The run also records when each era turns, read two ways: the engine's
`EraReached` event, and `CurrentEra` read after every finished project. The
second exists because the first never fires (§4.1). A building def's era is
the latest era among its `researchPrerequisites`, or "ungated".

```sh
dotnet run -c Release --project tools/bench/SimWorld.Bench -- \
  --suite arc --days 2400 --arc-every 60 --band 20 --seed 99 --threats false
```

### 1.2 Runs

| run | band | seed | threats | length asked | length reached | why it stopped |
| --- | --- | --- | --- | --- | --- | --- |
| probe | 20 | 99 | off | 1 year, then 3 | 1, 3 | done |
| A | 20 | 99 | off | 40 years | 16 years | extinct |
| B | 20 | 2024 | off | 40 years | TODO | TODO |
| C | 40 | 201 | off | 40 years | TODO | TODO |
| D | 20 | 99 | on | 40 years | TODO | TODO |

"Threats off" is Medium with big and intro threats disallowed, the baseline
arm `--suite storyteller` uses. Disease, weather and small incidents still
fire. The world is solo: no rival exists until emergence founds one.

The runs are deterministic. Day 30 of the 1-year probe, the 3-year probe and
run A agree to the last digit on every column.

## 2. Era timeline

The day each era became current. `CurrentEra` is the latest era whose
research spine, and every spine before it, is finished. So "became current"
means the settlement had finished that era's spine.

| era | A: s99 b20 off | B: s2024 b20 off | C: s201 b40 off | D: s99 b20 on |
| --- | --- | --- | --- | --- |
| SticksAndStones | start | start | start | start |
| Agrarian | day 41 (y0.7) | TODO | TODO | TODO |
| Bronze | day 76 (y1.3) | TODO | TODO | TODO |
| Classical | day 135 (y2.2) | TODO | TODO | TODO |
| Medieval | day 213 (y3.5) | TODO | TODO | TODO |
| Industrial | day 573 (y9.6) | TODO | TODO | TODO |
| Information | not reached (82% at death, y16) | TODO | TODO | TODO |
| Exotic | not reached | TODO | TODO | TODO |

Research finished per game year in run A:

| year | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 | 16 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| projects | 69 | 35 | 27 | 18 | 8 | 6 | 5 | 7 | 7 | 7 | 7 | 8 | 7 | 1 | 0 | 1 |

### 2.1 Why it is fast, then slow

The first five eras, SticksAndStones to Medieval, cost 136,130 points at
base cost, and cheapest-first (`ResearchAgenda`) finishes their spines in
3.5 years. Then the price climbs
steeply. Industrial alone costs 116,610 and Information 161,370 at base cost.
`CostFactor` adds 50% per tech level above the researcher, so a Medieval
civilization pays 1.5x for Industrial work. Throughput does not rise to meet
it. `WorksInitiativeTuning.ResearchBenchesWanted` is a flat 2, so at most two
people research at any time, whatever the population. Run C has twice the
band and finished 73 projects in its first year against run A's 69 (TODO:
confirm against C's later years).

From Medieval on, an era takes years and the population has to survive those
years with nothing new to build. In run A it did not.

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

Across all content, 14 of the 245 research projects gate anything: a building,
a recipe or a weapon. Classical (31 projects) and Exotic (25) gate nothing.
The Medieval, Industrial and Information buildings that do exist (traps,
power, heater, turret) are placed by no initiative. No code in
`src/SimWorld.Core` names `Heater`, `Turret_Mini`, `WoodFiredGenerator`,
`PowerConduit`, `Battery`, `TrapIED` or `TrapSpike`. A settlement left to its
own initiative can never put one up, whatever it has researched.

### 3.2 What run A actually built

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

By era, against the era the settlement was in:

| year | era | distinct defs | ungated | SticksAndStones-gated | Agrarian-gated | Bronze-gated | later |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Agrarian | 10 | 367 | 2 | 1 | 0 | 0 |
| 2 | Bronze | 13 | 528 | 3 | 1 | 1 | 0 |
| 4 | Medieval | 13 | 591 | 3 | 1 | 1 | 0 |
| 10 | Industrial | 13 | 750 | 3 | 1 | 1 | 0 |
| 16 | Industrial | 13 | 747 | 3 | 1 | 1 | 0 |

**The answer to "does what gets built change with the era" is no.** The
thirteenth kind of building appears in year 2. For the fourteen years that
follow, through Classical, Medieval and Industrial, the only things that
change are the number of storage huts and the slow loss of walls. 99% of
everything standing is ungated. The one Bronze-gated building is a single
`House_Chalco`. The household dwelling stays a Paleolithic house for 11 of 12
households, because nothing replaces a house that already stands.

TODO: B, C, D building tables.

### 3.3 Storage huts

`StorageHutNeed` wants one hut per three stacks in the store ledger, with no
cap. In run A the ledger reached 6,000 nutrition and the map held 661 huts at
the peak: 89% of every building, for 7 to 25 people. It is the one thing that
grows across the eras, and it grows because stock grows, not because the era
changes.

## 4. What the player would see

### 4.1 No era is ever announced

`ResearchManager.ResearchPerformed` writes the new progress before it calls
`FinishProject`:

```csharp
progress[currentProj] = newProgress;
if (newProgress >= currentProj.baseCost)
{
    FinishProject(currentProj, false, researcher);
}
```

`FinishProject` then reads `eraBefore = CurrentEra` to bracket the change.
By then the project already counts as finished, so `eraBefore` is already the
new era and `CheckEraTransition` sees no crossing. Research work is the only
place research points come from (`JobDriver_Research`), so in played games
`EraReached` never fires. `EraTransitionUtility.Notify_EraReached` never runs,
and nothing writes the chronicle line, sends the "A new era" letter or records
the "Era reached" moment. `MomentCurator` lists that moment as always worth
remembering, and it is the one moment that marks the arc.

Measured: across the probes and runs A to D (TODO: confirm for B to D), the
`EraReached` column is empty and the moments contain no "Era reached" entry,
while `CurrentEra` moved through five eras in run A. The era tests drive
`FinishProject` directly (`EraTransitionTests`), which is the one path where
the ordering is right.

### 4.2 Moments go quiet

`MomentCurator` marks the first of each kind, records and era turns. With
era turns gone, a run's moments stop arriving once the first year has used up
the kinds. Run A logged 17 moments in year 1 and 12 in the 15 years after. In
year 7 six people died and no moment was recorded, because a death from
disease had already been seen. TODO: B to D.

### 4.3 Threat points

TODO: from run D. In the threat-free arm the points still rise from 147 to a
peak of 1,357 in year 6, driven by wealth (mostly stores) and the era factor,
and then fall with the population.

## 5. Population and deaths

Run A:

| year | era | pop | died disease | died starvation | died injury | food on map | food in stores | wealth |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | SticksAndStones | 20 | 0 | 0 | 0 | 0 | 0 | 260 |
| 1 | Agrarian | 25 | 2 | 0 | 0 | 142 | 2,599 | 19,185 |
| 2 | Bronze | 21 | 8 | 0 | 0 | 117 | 3,964 | 29,852 |
| 4 | Medieval | 16 | 13 | 0 | 1 | 207 | 4,702 | 38,424 |
| 6 | Medieval | 15 | 14 | 4 | 1 | 90 | 5,194 | 46,473 |
| 8 | Medieval | 9 | 22 | 4 | 2 | 54 | 6,222 | 58,121 |
| 10 | Industrial | 7 | 26 | 4 | 2 | 291 | 4,896 | 54,654 |
| 12 | Industrial | 5 | 32 | 4 | 2 | 83 | 3,366 | 50,536 |
| 13 | Industrial | 4 | 34 | 4 | 2 | 24 | 274 | 35,265 |
| 14 | Industrial | 1 | 34 | 8 | 2 | 0 | 0 | 33,893 |
| 16 | Industrial | 0 | 35 | 9 | 2 | 0 | 33 | 34,093 |

Every citizen ran at Full tier for the whole run; no Interval or Statistical
pawn ever appeared, because the population never came near
`TieringTuning.FullTierBudget`.

Disease killed 35 people, starvation 9 and injury 2. Births and wanderers
added about 26 people over 16 years and never got ahead of deaths. Four
people starved in years 5 and 6 while the store ledger held about 5,000
nutrition. The map held 90 to 100. The stores then fell from 3,366 to 274 in
one year as the last few people lived on them, and the last starvations
follow. This run does not show why ledger food did not reach the people who
starved in years 5 and 6. It is recorded here as an observation.

TODO: B, C, D.

## 6. Cost

TODO: table with concurrency, all runs.

## 7. Diagnosis

TODO.
