# Tending after a raid

Evidence for the change that puts RimWorld's emergency work above sleep and
hunger. Kept in the repository because a scratchpad is one interruption from
gone.

> **These numbers belong to commit `a685640` and the tend lane on top of it.**
> A seed names a world only for one tree. Beds (trees on the map) landed in the
> same pull request and shift every roll after map generation, so re-run rather
> than compare a later tree against these tables.

## What was wrong

After a raid, the median first tend was 5,340 ticks and 29 of 84 bleeding
downings never got a tend job at all. It was not the fighting: once the map was
clear, the median from "clear" to first tend was 330 ticks. It was the hours
before that. Raiders here never retreat, so the map stayed hostile for 25,070
ticks after raid 1, and while someone lay bleeding and untended the able
citizens were **asleep** in 580 samples, fighting in 453 and tending in 78.

This port ran all work, emergency included, after hunger, sleep, recreation,
orders and edicts. RimWorld runs "starving → eat" and then emergency work
above all of those, and a sleeping pawn looks for emergency work every 211
ticks.

## Files

| File | What it is |
| :--- | :--- |
| `before-777.txt` | `a685640` replaying the populated storyteller world, seed 777, 60 days, following every downing of a citizen |
| `after-777.txt` | The same harness on the tend lane's code; the two agree line for line up to raid 1 |
| `save-777.txt` | Shows that saving mid-run does not perturb the replay |
| `pairs-t1537060.txt`, `pairs-t2613060.txt` | Paired continuations from saves taken just after raid 1 and raid 3, ten seeds, both arms |
| `pairs-summary.txt` | The pooled summary of the pairs |
| `diag.cs`, `Diag.csproj.txt` | The harness (renamed so no build picks it up) |
| `tally.py`, `pairs_summary.py` | The tallies behind the tables |
| `run-pairs.sh`, `mutate.py` | The paired-continuation runner and the mutation proof, as run in the lane |

## Paired continuations, ten seeds each

| | Raid 1, before | Raid 1, after | Raid 3, before | Raid 3, after |
| :--- | ---: | ---: | ---: | ---: |
| Downings | 300 | 161 | 318 | 280 |
| Bleeding, never a tend job | 105 | 31 | 85 | 44 |
| First tend p50 / p90 (ticks) | 6,750 / 21,180 | 390 / 5,010 | 1,980 / 16,290 | 5,820 / 14,490 |
| Bled out | 70 | 1 | 63 | 20 |

Part of the gain is fighting, not faster tending: a doctor woken for a casualty
is awake and armed when the raiders come, so raids end sooner with fewer downed.
Raid 3's median wait got longer, because citizens fight before they doctor, and
9 of its 20 remaining bleed-outs came while the whole band was down.

## Not fixed here

- **Most bleed-outs die at 45% blood loss, not 100%**: 42 of 70 and 36 of 63
  before this change. This port's consciousness formula multiplies breathing,
  filtration and pain penalties outright, far harsher than RimWorld's.
- **Raiders never retreat**, so the map stays hostile long after a raid is lost.
- **Fires now get sleepers out of bed too**, and firefighting here has no
  home-area limit, so a map-wide fire could wake everyone. None fired in these
  runs.
