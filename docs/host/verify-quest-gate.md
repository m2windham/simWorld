# Note to the host lane: the verify run's quest gate is a coin toss

**From:** core, lane `req007-quests` · **Date:** 2026-11-22

## What was seen

The host's headless `-verify` run (Release player, solo scenario, seed `simworld-host`, 61 days) offered 3 quests
on the core before the shelter-rooms merge and 0 after it (`0d77578`). On the same run, incidents went from 24 to
20 and finished research from 76 to 72, and the `quests` group failed its gate `questLettersOffered >= 1`. The
populated scenario still offered 2.

## Finding: a different random path, not a defect

Nothing the shelter merge changed is read on the way to a quest offer. A quest is offered only through
`GiveQuest_Random` (`Incidents_Quests.xml`), and the storyteller reaches it in one way: on each `RandomMain` firing
(Cassandra: mean time between firings 3 days), it picks the `GiveQuest` category with weight 5 of 80. The incident
then fires whenever some `QuestScriptDef` covers the firing's points and is off its refire spacing. A non-threat
firing carries 0 points, which every shipped script accepts, and nothing has fired yet to start a spacing. No
gate on that path reads dwellings, the housed count, wealth, population or the era (`IncidentWorker.CanFireNow`,
`IncidentWorker_GiveQuest.Candidates`, `QuestManager.CanFire`).

What the shelter merge did change is everything citizens do: houses are now real walls, doors and beds raised by
builders, a cooler and a hearth are fuelled and flicked, and rooms have their own air. All of that draws from the
same seeded `Rand.Current` the storyteller draws from. So the storyteller's rolls land differently from the first
few hours, and a 1-in-16 category pick in about 20 rolls simply comes up a different number of times.

Over 61 days the expected count is about 20 x 5/80 = 1.25 `GiveQuest` picks. The auto-accepting `Quest_Omen`
(weight 0.2 of 2.2) sends no letter, so about 1.1 letters are expected. With a mean that low, a run offering no
quest letter at all happens about one time in three. That is the shipped tuning working, not a broken path.

## Evidence

A core-side reproduction of the verify solo scenario, built in this lane's scratch area and not shipped: it does
what `SimHarness.StartGame` does (TribalStart, the seed string, 3 subdivisions, solo, band 20) at the host's
batch-mode cadence (8 ticks, then 60 per frame, deltas pumped each frame). It drives the same levers
`VerifyRunner.Drive` does each game hour: research pick, one edict, accept every quest, one trade per caravan, and
one wall and potato field. It was run 61 days on the core just before the merge (`5d6ca77`) and at the merge
(`0d77578`). The harness does not repeat every host read, so its numbers on `simworld-host` differ from the
player's. It reproduces the same direction (1 -> 0) and the same mechanism.

| seed | quest letters, `5d6ca77` | quest letters, `0d77578` | incidents | research finished |
| --- | --- | --- | --- | --- |
| simworld-host | 1 | **0** | 19 -> 16 | 70 -> 72 |
| s1 | 1 | 1 | 18 -> 21 | 67 -> 62 |
| s2 | **0** | 3 | 20 -> 14 | 72 -> 74 |
| s3 | **0** | 1 | 16 -> 20 | 65 -> 65 |
| s4 | 1 | **0** | 15 -> 13 | 80 -> 77 |
| s5 | **0** | 4 | 21 -> 21 | 71 -> 73 |
| total | 3 (3 of 6 seeds at zero) | 9 (2 of 6 seeds at zero) | 109 -> 105 | |

Before the merge, half these seeds would already have failed the gate, and after it the total offered went up, not
down. Incidents and research move in both directions seed by seed, which is what a reshuffled random path looks
like. A broken quest path would show zeros on every seed after the merge.

## Recommendation for the verify check

Judge quests by whether the path works, not by whether one seed's dice came up:

1. **Best, and deterministic:** keep the solo run as it is, but judge the `quests` group on the command path the
   host owns. When at least one quest letter was offered, every one must be acceptable through
   `GodCommands.ResolveLetterChoice`. When none was offered, report the group as *not exercised* (information,
   not a failure), as `economy` is reported in solo. Whether the storyteller can offer a quest at all is the
   core's to prove, and the core suite does (`DirectorTests`, `QuestTests`, `LetterChoiceTests`).
2. **If the gate must stay a count:** count across scenarios and seeds, not per run, for example "at least one
   quest letter across the solo and populated scenarios", or "quests offered on at least 2 of 3 seeds". On the
   numbers above, a single solo seed misses about a third of the time and three seeds all miss about 4% of the
   time.
3. Do not raise `GiveQuest`'s storyteller weight to make the gate pass: that is tuning against the instrument
   (`CLAUDE.md`, "The player is the point, and the instruments are not").
