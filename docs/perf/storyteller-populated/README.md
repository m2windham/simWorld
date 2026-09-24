# The storyteller in a populated world

Raw output from `tools/bench --suite storyteller`, kept because the finding it
supports changes what the game needs next, and a scratchpad is one interruption
from gone.

Every earlier storyteller reading was taken in a world with **no enemies**: the
suite hardcoded `soloStart`, so `RaidEnemy` could not fire. These are the first
runs in the world a player actually plays.

> **These numbers belong to commit `959f1f2`, not to whatever is current.** A
> seed names a world only for a given tree: any change to how many random draws
> something takes shifts every roll after it. The construction carry tracker,
> merged right after these runs, did exactly that — on the tree that followed,
> seed 12345's first raid comes on day 17 at 623 points, not day 12. Seed 777
> happened to reproduce. Re-run rather than compare against these tables.

| File | World | Seed | Days |
| :--- | :--- | ---: | ---: |
| `pop-12345.txt` | populated | 12345 | 60 |
| `pop-777.txt` | populated | 777 | 60 |
| `solo-12345.txt` | solo, same tree, for comparison | 12345 | 60 |
| `diag-pop-12345.txt`, `diag-pop-777.txt` | populated — a replay of what each raid did, matched to the bench on 92 sampled days with 0 mismatches | | |
| `decompose.py` | the method behind the factor-share figures below | | |

Reproduce any run with:

```sh
dotnet run -c Release --project tools/bench/SimWorld.Bench -- \
  --suite storyteller --solo false --days 60 --seed 12345
```

## What it found

**`RaidEnemy` fires, becomes the main threat, and ends the game.** It took 16 of
19 big-threat slots across the two populated runs. On seed 12345 the first raid
came on day 12 at 436.6 points with 11 raiders; it downed 14 of 21 citizens and
the band was wiped out by day 27. Seed 777's first raid came on day 30 with 20
raiders; the settlement was down to 3 of 26 by day 60.

**Almost every death was a downed citizen dying afterwards** — bleeding out, or
of wound infection days later. Being downed is survivable in RimWorld, and that
is what turns a raid into a near-miss rather than a wipe. Here it was not.

Three things compound it, each a defect rather than a tuning choice:

1. **Raid deaths are credited to nothing.** `IncidentWorker_RaidEnemy` never tags
   its squad with the incident that spawned it, so the death ledger and
   `LossSummary` report raids as having killed nobody.
2. **Dying of wounds does not reach adaptation.** Blood-loss and infection deaths
   are not charged as violent, so the storyteller does not ease off after a
   massacre. Adaptation *rose* on the day six people bled out.
3. **Capture never happens.** `CaptureUtility.Capture` is called only from tests,
   so no raider is ever taken prisoner.

**The raid should not be retuned until those are fixed.** Whether 11 raiders on
day 12 is too many cannot be read off a settlement that cannot rescue its
wounded and whose storyteller does not notice its dead.

## The threat ceiling

`ManhunterPack`'s `MaxPack = 12` still binds in the solo run, from day 28. In the
populated runs it never did — raids took the big-threat slots and killed enough
people to keep the curve below the ~720 points where the cap starts to bite. It
no longer caps what the player faces: raid squads are uncapped, and 817.7 points
bought 20 raiders.

## Decomposition of the rise in threat points

| Window | Points | Days passed | Adaptation | Wealth | Population |
| :--- | ---: | ---: | ---: | ---: | ---: |
| solo 12345, day 0 → 60 | ×7.94 | 53.0% | 23.0% | 26.2% | −2.2% |
| solo 12345, day 0 → 29 | ×3.35 | 65.9% | 26.7% | 7.0% | +0.1% |
| populated 777, day 0 → 29, the day before its first raid | ×3.11 | 70.3% | 30.7% | 2.3% | −3.6% |

Before the first raid, the calendar is most of the curve. Trade did not raise
wealth's share: the curve does not read wealth at all below 10,000, and the
populated world only passed that on day 29.

## Caveats

- Solo and populated runs of one seed are different worlds from day 1, so the
  comparisons above are not paired.
- Nobody is playing. The citizens run at full detail on a real map, but no
  player drafts them, takes cover or retreats. A human would lose fewer — which
  is why the three defects above matter more than the raid's size.
