# Recovering from a raid

Raw evidence for lane/downed, kept beside `../storyteller-populated/` because it
answers the question that folder left open: when a raid downs a founding band,
which link of the recovery chain fails? RimWorld's chain is rescue, tending and
winning the race against infection. The brief expected rescue or tending to be
missing. Neither was.

## What each link does in play

Replays of the populated storyteller world on the branch base `b68eee7`,
following every downing of a citizen. The observer only reads: two replays of
one seed agree line for line.

| Link | Seed 12345 | Seed 777 |
| :--- | :--- | :--- |
| Tend reaches the downed | 39 of 44 downings; median 120 ticks after going down | 31 of 39; median 5,460 ticks, because the 20-raider fight was still on |
| Tending stops bleeding | yes; nobody bled out | yes once reached; one citizen bled out, his first tend job 14,400 ticks after he went down |
| Rescue to a bed | 12 of 44, all to the settlement's one bed | 0 of 39; the map had no bed in 45 days |
| Infections tended | 33 infections, first tended at a median 720 ticks, tended 99% of the time | 29 infections, median 840 ticks, 99% |
| Infections survived | 14 of 33; 7 of 17 infected citizens died | 15 of 29; 5 of 16 died |

Tending and rescue were reached. Infection killed tended citizens.

## Why infection killed tended citizens

Two numbers in this port were not RimWorld's, and neither had a source recorded.

- **The stages summed to a kill below lethal severity.** "Extreme" began at
  0.62 and took 0.2 consciousness each, so two or three infections at once
  drove consciousness to zero. RimWorld's stages take none below 0.78 and cap
  consciousness at 0.1 at the last stage; an infection kills only at 1.0.
- **Wounds got infected 2.7 times as often.** 0.4 per cut, stab, gunshot and
  scratch, against RimWorld's 0.15.

Sources: the wiki's generated def dumps, `Hediffs/Core/Local/Infections` and
`Hediffs/Core/Local/Injuries`, and its `Infection` page for the tend-quality
factor (85% at quality 0 to 5% at quality 1).

The last observation of each citizen who died of infection on seed 12345, 30
ticks before death (`autopsy-12345.txt`):

| Citizen | Consciousness | Infections | Immunity |
| :--- | ---: | :--- | ---: |
| Dashiell | 0.02 | 0.77, 0.62, 0.63 | 0.80 |
| Quill | 0.12 | 0.70, 0.62 | 0.73 |
| Jarrah | 0.14 | 0.66, 0.62 | 0.81 |
| Elian | 0.13 | 0.62, 0.63 | 0.70 |
| Oona | 0.34 | 1.00, 0.06 | 0.99 |
| Halle | 0.02 | 1.00, 0.38 | 0.96 |
| Kira | 0.25 | 1.00, 0.82, 0.84 | 0.98 |

Four of the seven had no infection near 1.0. Under RimWorld's stages an
infection kills only when it reaches 1.0, so those four would still have been
racing their immunity, which stood at 0.70 to 0.81.

## Before and after, paired

Two runs of one seed diverge at the first infection roll, so an unpaired
comparison mostly measures luck; one after-run's first raid came ten days
later than the before-run's. Instead: the branch base plays the world to just
after its first raid, before any wound's infection timer can expire, and saves
it. That one save is then continued by the old code and content and by the new,
from the same seeded random stream, for several continuation seeds. The two
arms share every wound and differ only in the infection model.

| Seed 12345, day-17 raid, 13 casualties | Old | New |
| :--- | ---: | ---: |
| Continuations | 6 | 6 |
| Casualties died of wound infection | 13 | 2 |
| Casualties died of blood loss | 1 | 1 |
| Casualties killed by something later | 1 | 7 |
| Survived to day 28 | 63 of 78 | 68 of 78 |
| New threats the storyteller fired | 5 | 14 |

| Seed 777, day-30 raid, 22 casualties | Old | New |
| :--- | ---: | ---: |
| Continuations | 4 | 4 |
| Casualties died of wound infection | 18 | 7 |
| Casualties died of blood loss in the saved fight | 4 | 4 |
| Casualties died of blood loss after a later raid or pack | 21 | 22 |
| Casualties killed by something else | 3 | 4 |
| Survived to day 42 | 42 of 88 | 51 of 88 |
| New threats the storyteller fired | 7 | 5 |

Seed 777 was saved mid-fight, with 11 raiders still standing, so each
continuation fights the rest of that battle its own way and this pairing is
looser. Its saved-fight blood-loss line is the same citizen in every run,
already beyond help when the save was taken; the deaths after later raids are the band being
downed wholesale with nobody left standing to tend them.

On seed 12345 the rise in later deaths is not recovery failing. Both arms'
storytellers diverge at the first infection roll, and there the arms that kept
their people drew almost three times as many further threats, which killed
some of them. Seed 777 did not show that. Why is the storyteller's question,
not this lane's; the line to read for recovery is the infection line.

## What this does not fix

- **Beds.** Seed 12345 had at most one bed for 25 citizens; seed 777 had none.
  Rescue needs a bed, so most of the downed were tended where they fell. In
  this port a bed does not yet change whether a patient lives, which is why
  this did not show up as deaths.
- **Immunity while resting.** RimWorld multiplies immunity gain by 1.10 for a
  resting pawn and 1.07 more in a bed. This port has neither, so it keeps its
  flat 0.7 a day, which is RimWorld's 0.6441 x 1.10. Porting the multipliers is
  what would make rescue matter for infection; it needs a sourced rule for
  whether a downed pawn on the ground counts as resting.
- **Medicine.** No medicine exists yet, and a tend's quality skips RimWorld's
  medicine potency (0.30 with none). Every tend here is about as good as a
  RimWorld tend with herbal medicine.
- **Tending during a fight.** Every citizen who can fight fights first, so on
  seed 777 nobody tended until the raiders left, and a raid that downs most of
  the band leaves nobody to tend at all. On seed 777 that, not infection, is
  now the larger killer: 21 and 22 of 88 casualties bled out after a later
  raid, in the old and the new arm.

## Files

| File | What it is |
| :--- | :--- |
| `before-12345.txt`, `before-777.txt` | the per-downing and per-infection replays on the branch base |
| `autopsy-12345.txt` | the last observation of every citizen who died with an infection |
| `pairs-12345.txt`, `pairs-777.txt` | every paired continuation, both arms, in full |
| `diag.cs` | the replay; the two `before` files came from it before its autopsy lines were added, and a replay with them agrees with them day for day |
| `pair.cs`, `run-pairs.sh`, `tally.py` | the paired measurement and its tally |

## Reproduce

The harnesses are console programs referencing `SimWorld.Core`, built outside
the repository. The replay takes a seed, a day count, `false` for a populated
world and the content directory. The paired run is two builds of `pair.cs`, one
against the branch base and one against lane/downed:

```sh
Pair save 12345 1010000 s12345.xml <base Data>
run-pairs.sh 12345 s12345.xml 28 1 2 3 4 5 6
python3 tally.py <pairs dir> 12345 1 2 3 4 5 6
```

The measurements were taken before lane/downed merged the raid-credit lane.
That lane changes what a death is credited to, not how wounds bleed or get
infected, so the recovery line is unaffected; the later-threat line may not be.
