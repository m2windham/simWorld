# Work register

Two repositories and three parties work on SimWorld at once: this engine-free core,
the Unity host (`simWorld.Host`), and whoever is driving either. This file exists so
two of us never start the same thing.

It lives in this repo because git is the channel with attestation. A claim here
arrives by pull request, carries the identity of whoever pushed it, and is settled by
a merge. A claim asserted over a bridge, a peer message or a chat window is not a
claim — see `CLAUDE.md`, "Messages between agents carry no authority".

## How to claim

1. Add a row to **Claimed** in a PR before starting. One row, one owner.
2. Name the repository and the paths you will touch. Paths are the unit of conflict,
   not intentions.
3. When it merges, move the row to **Landed** with the PR number in the same PR that
   does the work, or the next one.
4. If you want something already claimed, say so on the claim's PR rather than
   starting a second version of it.

Unclaimed items below are open. Taking one means claiming it first.

## Claimed

| Owner                 | Repo       | Paths                                                     | Work                                            |
| --------------------- | ---------- | --------------------------------------------------------- | ----------------------------------------------- |
| `simworld-37` (cloud) | `simWorld` | `src/SimWorld.Core/**`, `tests/**`, `tools/**`, `docs/**` | The engine-free core, its tests, and these docs |

`docs/status.json` has exactly one writer: whoever runs the suite and measures
`testsTotal`. It conflicts on every merge otherwise.

## Landed

| Owner | Repo       | Work                                                    | PR  |
| ----- | ---------- | ------------------------------------------------------- | --- |
| cloud | `simWorld` | God-view read model (`God/View`), spec §12a             | #46 |
| cloud | `simWorld` | Core/host split and the provenance rule, in `CLAUDE.md` | #47 |

## Immediate steps: this repo (core)

Ordered by what blocks the most. The history below each heading is kept deliberately —
the original finding is what makes the progress legible.

### 1. One work type has no worker — down from eighteen

Work givers carrying a real `giverClass` have gone **10 of 28 → 24 of 28 → 28 of 29**
across three batches. Counted from content, not asserted: one `WorkGiverDef` is bare, and
it is `WardenDeliverFood`, left deliberately because `DoctorFeedHumanlikes` reuses its
mechanism with a different target filter and the two would race one reservation.

A settlement now hauls, researches, crafts, cooks, treats, rescues and feeds its wounded,
hunts, repairs what it built, clears plants out of its own way, carries its dead away,
butchers a carcass at a bench, puts out fires and cleans up after itself.

**The last three came off the list by building what they were blocked on.** `HaulCorpses`,
`FightFires` and `CleanFilth` had no `Corpse`, `Fire` or `Filth` class anywhere in this
codebase — the oldest entries here, and the block was real rather than a missing decision.
All three systems landed together. The shape worth keeping: a giver blocked on a missing
*system* is not the same kind of gap as one blocked on a missing *decision*, and this
register did not distinguish them for two batches.

**A wired giver plus content that cannot reach it looks exactly like a finished feature.**
Hunting was wired and dormant for a full batch because `Tribesperson` shipped with no
`weaponTags`, so no citizen in any generated game held a weapon — and every unit test
passed throughout, because they arm their own pawns. The two lanes after it went looking
for the same trap in their own work and both found it: `ThingDef` carried no flammability
field at all and nothing in content was flammable, so fire could not have spread; and
`AreaManager.Home` shipped dormant and has never been populated, so RimWorld's home-area
gate on cleaning would have made every fire of that giver impossible. Both were closed
before shipping rather than after.

Known gaps inside what landed: `DoBillsArt` is wired with no bench; a pawn never tends
itself, matching RimWorld; and rain extinguishment is ported and tested with no weather
module to drive it. ("Nothing interrupts a job in flight" was the fourth; see §1b — it is
closed.)

### The original finding, for the record

Ten `WorkGiverDef`s carried a `giverClass`. **Eighteen carried none at all**, so the work
type existed in content, appeared in a pawn's priorities, and could never produce a job. A
settlement could build, farm, mine, tame animals and hold prisoners, and could not haul,
cook, craft at a bench, treat an injury, or research anything — the tech tree, the era
ladder and the divergence work all stood above a research work type no citizen could
perform.

### 1b. The combat module was unreachable from play

Worth its own entry because it was larger than the work-giver gap and nobody had noticed
it. `Combat/` — verbs, armour, cover, downing, death, capture — was complete and tested.
**Nothing in the AI layer ever attacked.** No `JobGiver_*Attack*`, no `JobDriver_*Attack*`
anywhere, and the humanlike think tree ran mental state → food → rest → orders → edicts →
work → wander with no danger tier at all. A raid arrived and everyone kept farming.

Closed. Who fights is SimWorld's own, since there is no draft to port: anyone armed engages
a hostile within acquire radius, anyone at all fights back within melee reach, and a new
`TakeUpArms` edict raises an unarmed citizen to the first rule.

**One of the two things this left open is now closed.** Raids reach a map, but only a map
that exists, and `GodCommands.FocusSettlement` moved attention without generating the
interior while `Game.EnterSettlement` was host-facing and called by nothing inside the core.
The real problem was narrower than "two calls are easy to confuse": the host is told to bind
to `God/View` and never reach into `Game`, and **`God/View` had no way to generate an
interior at all** — so the separation was not a choice a host could make, it was a wall.
`GodCommands.OpenSettlement` now does both in the load-bearing order, and
`GenerateSettlementInterior` is the narrow half for a map nobody is watching.

Still open, and not this repo's to fix alone: `ChooseTargetSettlement` weights across all of
a civilization's settlements, so even with one town open a raid often picks an unopened one
and resolves without a map.

**The other thing it left open is now closed too: something can interrupt a job in
flight.** Both of RimWorld's answers are ported, and kept separate because they are
different strengths of claim on a pawn's attention. A pawn who *sees* a threat goes through
the constant think tree (`ThinkTrees_Constant.xml`, evaluated every
`ConstantThinkTreeTuning.IntervalTicks` whether or not a job is running), gated by
`ThinkNode_ConditionalCanDoConstantThinkTreeJobNow` so a job marked
`casualInterruptible="false"` or one the player forced survives it. A pawn who *is hit*
goes through `JobDef.checkOverrideOnDamage` and `Pawn_JobTracker.Notify_DamageTaken`, which
wakes them on the tick the hit lands and then re-asks the main tree, throttled so a burst
does not buy a think-tree pass per bullet. `LayDown` is the case that proves the split:
asleep is not a state you notice a raider from, but it is very much a state you are woken
out of.

The cadence was measured rather than guessed — `docs/perf/constant-think-tree.md`, and
`tools/bench --suite interrupts` reproduces it. At `TieringTuning.FullTierBudget` on a real
map, evaluating the tree every tick costs +265% of the whole pawn-tick budget; RimWorld's
own 1-in-30 costs +3.7% once two things the constant tree promoted from cold paths to hot
ones were fixed (`BestAttackTarget` tested its expensive predicate first; the combat job
giver built a `Verb` before it had a target). So the 1:1 cadence and the affordable one
turned out to be the same cadence, and nothing here is a translation.

### 2. Attention drives the tiering, and the Full tier is now bounded in time as well as space

**Wired.** Attention is the god's focus: zero or one settlement, named across the host
seam by world tile. `God.AttentionManager` applies focus changes immediately and
reconciles from `GodManager.GodTick`; citizens of unattended settlements fall to Interval
and settle to Statistical after a year of continuous insignificance.

**Capped.** Focus bounded Full-tier to one settlement's live roster, which is a bound in
space and none at all in time — demography doubles that roster about every 17 years, and
it crossed the spec's Full ceiling around year 115–125. `God.AttentionBudget` closes it:
the `TieringTuning.FullTierBudget` most significant citizens of the focused settlement
hold Full and the rest stay where they are **even while attended**.

The decision it needed was a **significance ordering** between the four promotion
reasons, which nothing expressed. It is now `hasRole` → `chronicleNamed` →
`relatedToPromoted` → `attending`, tie-broken by `thingIDNumber` ascending. The first
three are properties of the person and are held by few; the fourth is a property of the
camera and is held by the whole roster at once, so attention is the only one that can
outgrow a budget and the first that must yield. The cap holds back the merely-looked-at,
never the leader. Spec §11.3 carries the full reasoning and the three invariants it does
not touch (demotion constraint not promotion clock, lossless in identity, no thrash).

**The budget is 500**, read off `docs/perf/baseline.md` rather than chosen: it is the
largest measured population at which per-pawn cost is still flat (14.0–14.1 ms/pawn-day
through N=500, degrading to 17.4 at N=1,000), and it sits under the low end of §10's
projected 750–1,500 ceiling for a population actually carrying wounds and illness.

The finding that motivated the lane, unchanged — the Full-tier roster with attention as
the only bound:

| year             | 10 | 40  | 60  | 80  | 100   | 120   | 160    |
| ---------------- | -- | --- | --- | --- | ----- | ----- | ------ |
| Full-tier roster | 67 | 163 | 300 | 656 | 1,412 | 3,179 | 16,868 |

And a century of real demography through the cap
(`AttentionBudgetTests.A_century_of_demography_holds_the_Full_tier_flat_instead_of_doubling_it`
— a different seed and band from the run above, so its roster climbs more slowly; the
load-bearing row is the second one):

| year      | 10 | 40  | 60  | 80  | 100   |
| --------- | -- | --- | --- | --- | ----- |
| roster    | 51 | 127 | 257 | 555 | 1,132 |
| Full tier | 51 | 127 | 257 | 500 | 500   |

The roster more than doubles over the century's second half; the Full tier reaches the
budget at year 80 and stops. Past year 100 it holds by construction rather than by
measurement — the test asserts the budget is never exceeded at any sweep, not only at the
decade samples — and the run was stopped at a century because that is where the finding
was.

Both of the holes this section used to list are now closed. `Notify_RoleChanged` is the
`Offices` module's — a station is seated, held and succeeded — and `MigrationManager` no
longer marks every arrival a founder. `Notify_ChronicleNamed` had a policy for the dead
and none for the living; `Director.ChronicleFame` is the living half: the oldest citizen
alive is named when they outlive every life the civilization has ever known, dead or
living, which is `MomentCurator`'s own record ratchet read one step earlier. It is rare
because a ratchet fires less and less often, and because while the holder lives the record
rises with them, so **at most one living citizen holds it at a time** — one seat of the
500. Measured over a century of real demography, on two seeds: 4 people ever named against
a roster of 1,463, and 3 against 1,404 (`ChronicleFameTests`).

### 3. "Endless" is endless now

**Done.** A generated label is `{age register} {track substrate} {form}` — "holographic
attention first principles" — with the three word lists disjoint per age, per track and
per role, so distinctness inside an age is structural rather than checked. Age to theme is
injective for 20 ages and then compounds with particles (`post-holographic`,
`meta-post-granular`), a base-8 numeral written in words, so the supply has no ceiling.
Verified by reading, not just asserting: ~1,000 generated names across two seeds, which is
what caught the leaves reading like an odometer.

Cost went from geometric to polynomial — depth 20 was 1,796,772 points, **three times the
entire authored tree**, and is now 95,454.

Two real bugs fell out. The generated tree was a pure function of content, so **no two
civilizations ever diverged past the ladder**; and because the `DefDatabase` is
process-wide, a second game in one process read the first game's unfinished generated
projects as startable and so never extended past the authored tree at all. Both fixed by
deriving from the world seed.

One literal bound is recorded rather than fixed: `DefDatabase.Add` narrows `Def.index`
with `checked((ushort)…)`, so a process minting more than 65,535 `ResearchProjectDef`s
throws — about 2,700 ages in, unreachable in play, but it is a ceiling on something the
design calls endless.

### 4. The audit found 131 seams, and the first sixteen were real

The wiring audit landed because the defect this project keeps shipping is a module that
is complete, tested and called by nothing — and a suite that tests each module in
isolation is structurally blind to it. Isolation *is* the defect. The audit asks six
questions over reflection, a token index of `src/` and the loaded content graph: an
interface with no implementation the game can hold, a `Notify_` hook nothing raises, a Def
field naming a placeholder worker, a field code reads that no Def sets, a field content
sets that no code reads, and state read but never written.

It shipped green against a reviewed baseline of 131 dormant seams, each carrying a
hand-written reason. The file can only shrink: a stale line fails the audit too, so a lane
that claims a fix it did not make is caught at merge rather than believed.

**Sixteen lines came off in one batch, and behind them were defects nobody had a symptom
for.**

- **Peaceful never suppressed raids.** `DefaultThreatPointsNow` clamps up to 35 and 35 is
  exactly `RaidEnemy`'s own floor, so a `threatScale` of 0 did not turn threats off — it
  shrank every raid to the smallest legal war band and fired it on schedule. Six of seven
  dormant `DifficultyDef` fields are consumed now.
- **Mining destroyed the rock and spawned air.** `mineableThing`/`mineableYield` were set
  by content and read by nothing, while `JobDriver_Mine`'s own doc claimed it read them.
- **A completed quest enriched nobody.** `QuestPart_Reward.Enable` handed its rewards to an
  interface no type implemented. Measured on a real game: the quest reported "200 silver, 5
  goodwill", and the seat's stores, the civilization's wealth and the faction's goodwill
  were unchanged.
- **Three needs never moved**, and tracing why turned up two arithmetic bugs worse than the
  seam: recreation lost 50 ticks of decay every coarse tick to an integer division, and a
  memory an unwatched citizen held aged at **a thirteenth of real speed** — so most of the
  civilization stayed anchored to events a fortnight after a watched citizen had forgotten
  them.
- **A backstory, a kind and a germline shaped no traits.** Eight fields read by
  `PawnGenerator` and set by nothing, so every pawn in every game rolled from one
  unfiltered table.

**Three lessons worth carrying, none of them about the specific bugs.**

*An audit's recorded reason expires.* Two of the baseline's own explanations were wrong by
the time a lane reached them — one blocker had been cleared a batch earlier, another
misidentified which end of the pipeline a field belonged at. A recorded reason is evidence,
not a verdict; check it against the code as it stands.

*A clean textual merge is not a correct one.* Two lanes wired
`DifficultyDef.questRewardValueFactor` in the same batch, one at reward generation and one
at payout. Different files, different methods, no conflict — and the result would have
squared the factor, paying 0.64 on a 0.8 difficulty. Nothing but reading both sites would
have caught it.

*The audit has a blind spot, and it is recorded rather than fixed.* Its two field checks
ask "content sets it, does code read it?" and "code reads it, does content set it?". A
field that **neither** side touches is reported by neither. `researchSpeedFactor` was
exactly that: no preset set it, no line read it, and it appears nowhere in the baseline. A
third check is the open follow-up.

### 5. A module can be wired and still unable to act

Batch six went back to the audit's list and two lanes came back with something the
audit had not asked about. Both are the same shape, and it is one level past what §4
describes.

**Raiders and citizens could not see each other as hostile.** Measured on a settlement
founded the ordinary way, with a real `RaidEnemy` on its interior: **0 of 14 raiders saw
a citizen as hostile, 0 of 30 citizens saw a raider**, and neither side ever took an
attack job. Hostility required both sides to carry a `Pawn.faction`, and
`SettlementFounder.GenerateFoundingBand` builds its `PawnGenerationRequest` naming none —
`PawnGroupMaker`, the raider path, is the only caller in `src/` that passes one. A
settlement has a faction; not one of its citizens does.

§1b of this register says the combat module was complete, tested and unreachable from
play, and records that being closed. It was reachable after that and **still inert**. Every
combat test passed throughout, because each hands its own defenders a faction by hand.

**Nothing in `src/` ever created a bill.** Every `Bill_Production` in the repository was
made by a test, so `WorkGiver_DoBill` — wired, given a real giver, listed in content, and
counted in §1's "29 of 29" — never found work on any bench in any game. Cooking, smithing
and tailoring are still in that state; stonecutting was fixed because a lane happened to
trace the whole chain from rock to wall rather than stopping at the missing recipe.

**What this changes about the method.** §1 counts givers with a `giverClass`, and §4 counts
seams nothing reaches. Both counts were honest and both missed these, because the question
they ask is "is this thing connected?" and the question that finds these is **"put a real
game in front of it and watch what happens."** Every one of these was found by measuring a
generated settlement, not by reading a call graph. The audit narrowed the search; it did
not answer it.

The corollary is uncomfortable and worth writing down: a passing suite of 1,855 tests, a
green audit, and 29 of 29 work givers wired were all simultaneously true while no bench had
work on it and no citizen could see a raider.

### The next item, and it is a World-module job

**Give a settlement's citizens their settlement's faction.** The batch-six fix wired
`FactionDef.hostileToFactionlessHumanlikes`, which is the flag for the factionless pairing
and was genuinely unread — but Tribal and Outlander content declares it `false`, so those
raids still find nobody. Turning the flag on for content that did not ask for it would
paper over the real gap.

It is not a one-line change and should not be taken as one. Citizens carrying a faction
changes hostility resolution, `AttackTargetsCache`'s faction buckets, `MapPawns` filing,
and anything that reads `Pawn.faction` for a social or trade decision. It wants its own
lane, its own measurement of a generated settlement before and after, and a check that the
Statistical and Interval tiers do not pay for it.

## Immediate steps: the host repo — unclaimed, proposed

These are proposed rather than assigned. The host session claims, amends or rejects
them; this side does not assign work across the boundary.

1. **Scaffold** `simWorld.Host` as its own repository under `A:\dev\`. A separate
   repo rather than a directory here means the two never share a file path, so no
   merge between them can collide.
2. **Reference the core relatively** in `Packages/manifest.json`:
   `"com.simworld.core": "file:../../simWorld/src/SimWorld.Core"`. Package Manager
   writes an absolute path by default, which works on exactly one machine.
3. **Get the Editor reachable** — pipeline package and MCP plugin — so the Editor
   answers `unity_list_instances`.
4. **A god view against the read model.** `GodViewSnapshot.Capture()` to read,
   `GodCommands` to act, per spec §12a. Never reach into `GodManager`: the snapshot
   is values and every handle is a `defName` precisely so the host cannot hold a
   `Def` and through it a worker. Surface `EdictOption.Reason` rather than
   reimplementing the rules to explain a disabled control.
5. **Do not touch** `src/SimWorld.Core/**`. If the host needs something the core does
   not expose, that is a request to this side, not an edit.

## Host: landed, and what is next

The host session has the read/write loop of spec §12a proven live. A Unity 6000.5.0f1 project
at `A:\dev\simWorld.Host` with its own git repo references `com.simworld.core` as a local UPM
package by relative path. It loads content, reads `GodViewSnapshot.Capture()`, and drives
`GodCommands.IssueEdict` from an `EdictPanel` whose buttons are interactable only when
`Availability == Available` and which surfaces `Reason` verbatim. It verified the write end to
end — issued the one edict a tribal start allows, got `Done` back, and saw the next read flip it
to active and non-interactable — and added three EditMode tests mirroring `GodViewTests.cs`.

Five commits, all local: `simWorld.Host` has no remote yet. That is the user's call to make, but
it means a proven loop currently lives on exactly one disk.

### The content-loading bug was ours, and it is fixed at the root

The host reported zero edicts for content that ships five. The cause was never on their side:
`CoreContent`'s class doc claimed "The Unity host sees it inside the package folder" and
`Locate()` never implemented it. It tried `SIMWORLD_DATA`, a `Data/` beside the assembly, and a
walk up for `src/SimWorld.Core/Data` — and from a Unity project's `Library/ScriptAssemblies`
every one of those stays inside the host project. A package resolved by relative path to a
**sibling repository** was unreachable by construction, and the load then reported success with
zero defs, which reads exactly like a civilization that has not started.

Four changes close it:

- `CoreContent.TryResolveFromUnityPackageManifest` walks up for `Packages/manifest.json` and
  resolves each `file:` dependency against the `Packages` folder — Unity's own rule, not the
  project root — accepting whichever one actually carries `Data/Core`. Not keyed on the package
  being named `com.simworld.core`, so renaming it cannot break this.
- `CoreContent.Load(db, types, options, dataDirectory)` and `AddCoreDefs(loader, dataDirectory)`
  take an explicit directory. The host wanted this and had only an environment variable to reach
  for; a process-global that must be set before anything reads it is what produced their
  `RuntimeInitializeOnLoadMethod` misfire.
- A Defs directory that exists and holds **no XML** now throws instead of loading zero defs.
- `CoreContent.DescribeSearch()` returns every place it looked and what was there, for printing
  in the failure.

The host's own guard — treating a zero-edict load as a hard failure — is worth keeping anyway,
because it checks the outcome rather than the mechanism.

Two corrections to how the host is reading this file. The claim-by-PR protocol governs **this**
repo; `simWorld.Host` is theirs and needs no PR here. And pushing `simWorld.Host` to its own
remote is not pushing to the shared repo — a narrower thing is being held than they think.

### Proposed next, in order

1. **Load content — the core now finds it by itself.** `CoreContent` could never locate
   content inside a Unity package: it probed `SIMWORLD_DATA`, a `Data/` beside the
   assembly, and a walk up for `src/SimWorld.Core/Data`, and from
   `Library/ScriptAssemblies` every one of those stays inside the host project. A package
   resolved by relative path to a sibling repo was unreachable by construction. Fixed:
   `TryResolveFromUnityPackageManifest` reads `Packages/manifest.json` and resolves `file:`
   dependencies against the `Packages` folder. Also new: an explicit `dataDirectory`
   argument on `Load`/`AddCoreDefs` (better than the environment variable), a hard throw on
   a Defs directory with no XML, and `DescribeSearch()` for the failure message. The host's
   own zero-edict guard is worth keeping — it checks the outcome, not the mechanism.
2. **Start a real game and report what the snapshot contains.** `Game.NewGame`, tribal
   start, run long enough that something happens. Population by tier, era and progress,
   whether the chronicle fills, whether settlements come with interior maps. A report
   rather than a feature: the host can see this and the core cannot, and two core bugs
   have now been found this way.
3. **Focus has landed — drive it, do not reinvent it.** The earlier note said to expect
   every citizen at `Full` because nothing drove the tiering. That is no longer true.
   `GodCommands.FocusSettlement(int tile)` and `ClearSettlementFocus()` are the write path,
   `GodViewSnapshot.FocusedSettlementTile` reads it back, and a settlement is named by its
   **world tile** rather than its name, which is not unique. The host should not carry its
   own idea of a selected settlement: two disagreeing notions of what the player is looking
   at is a bug that takes a week to find. Expect a focused settlement's citizens at `Full`
   and everyone else falling to `Interval`, then `Statistical` after a year.
4. **Then the civilization view proper.** Settlements, population by tier, era, chronicle.
   `GodViewSnapshot` carries all of it, and `RecentHistory` versus `Moments` deserves
   different treatment on screen — running news against the civilization's landmarks.

## The honest summary

Phase 1 built every system in isolation and tested it there. The count of ported
systems is real. What has never been true is that they add up to a played game — and
the three things that were furthest from true have moved unevenly.

The work economy is wired: 29 of 29 work givers carry a real `giverClass`, up from 10
of 28. The tiering is driven, and bounded in time as well as space. There is still no
surface to look at, and that is now the largest single gap — the host repo's section
above is where it gets closed.

What replaced "a third wired" as the recurring defect is subtler and is what §4 is
about: a system can be complete, tested, wired *and still unreachable*, because the
content that would exercise it sets nothing, or the one caller that would reach it was
never written. Sixteen of those came off the list in one batch. A hundred and fifteen
reviewed seams remain, most of them honestly blocked — but the batch before proved that
"blocked" is a claim with a shelf life.
