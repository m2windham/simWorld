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

Ordered by what blocks the most. The first is much larger than the other two and is
the honest state of the game right now.

### 1. Eighteen work types have no worker

Ten `WorkGiverDef`s in `Data/Core/Defs/WorkGiverDefs/` carry a `giverClass`.
**Eighteen carry none at all**, so the work type exists in content, appears in a
pawn's priorities, and can never produce a job.

Wired: `ConstructFinishFrames`, `ConstructDeliverResourcesToFrames`,
`ConstructDeliverResourcesToBlueprints`, `GrowerSow`, `GrowerHarvest`, `Mine`,
`TameAnimals`, `TrainAnimals`, `WardenFeed`, `WardenAttemptRecruit`.

Unwired: `HaulGeneral`, `HaulCorpses`, `CookMeals`, `Research`, `DoBillsSmith`,
`DoBillsTailor`, `DoBillsArt`, `DoBillsCraft`, `DoctorTend`, `DoctorTendEmergency`,
`DoctorRescue`, `DoctorFeedHumanlikes`, `WardenDeliverFood`, `CleanFilth`, `Repair`,
`PlantsCut`, `Hunt`, `FightFires`.

A settlement can therefore build, farm, mine, tame animals and hold prisoners, and
cannot haul, cook, craft at a bench, treat an injury, or research anything. The tech
tree, the era ladder and the divergence work all exist above a research work type
that no citizen can perform.

Suggested order, by what unlocks the most behind it: `HaulGeneral` (most other work
assumes things can be moved), then `Research` (it gates the whole progression
pillar), then `DoBills*` (the crafting module has recipes and benches and no one to
run them), then `CookMeals` and the `Doctor*` family.

### 2. Attention never changes a citizen's tier

`Pawn_TierTracker.Notify_AttentionChanged` is not called anywhere. The promote and
demote mechanism is built and tested; nothing drives it. In practice every founded
citizen stays Full forever, so the tiering that exists to bound Full-tier population
does not bound anything. This is what the `HealthTick` work (PR #45) bought headroom
for, and the headroom is currently unspent.

Wiring it means deciding what "attention" is — entering a settlement, a camera
scope, an explicit selection — which is a design decision touching both repos, not a
mechanical fix.

### 3. "Endless" is three tracks deep

`EndlessResearch` generates three tracks of three authored titles before it falls
back to appending numerals. "Endless technology" is a stated pillar of the game; a
player who reaches the end of the authored tree currently finds Roman numerals.

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

The host session has a first slice working: a Unity 6000.5.0f1 project at
`A:\dev\simWorld.Host` with its own git repo, referencing `com.simworld.core` as a local
UPM package by relative path, and a `GodViewBootstrap` calling `GodViewSnapshot.Capture()`
on a timer. It verified the result by reading the rendered text back in Play mode rather
than trusting a screenshot, and did not call `Game.NewGame`, so the empty read was honest
rather than staged. Read-only so far; nothing wired to `GodCommands`.

It also reported **zero edicts**, which is a real bug and was this side's. Five edicts
ship and `Capture()` returns every one regardless of availability, so zero is impossible —
the host had never loaded the core's content, and nothing in the contract said it had to.
`GodViewSnapshot.ContentLoaded` now distinguishes "content never loaded" from "a
civilization that has not started", which were indistinguishable before, and `Capture()`'s
doc carries the load call.

Proposed next, in order. As above these are proposals with reasons, not assignments.

1. **Load content before anything else.** `CoreContent.Load` into a `DefDatabase`, that
   database assigned to `DefDatabase.Global`. `ContentLoaded` should flip true and five
   edicts should appear, each with an `Availability` and a `Reason`. If they do not, the
   package is not shipping its `Data/` directory into the Unity build — that is a
   packaging problem on this side and worth reporting immediately rather than working
   around.
2. **Start a real game** (`Game.NewGame`, tribal start, solo) and **report what the
   snapshot actually contains** — population by tier, era and progress, whether the means
   look sane, whether the chronicle fills. The ask here is a report rather than a feature:
   the host can see this and the core cannot. The last number that looked wrong found a
   bug.
3. **Then the write path.** One edict button through `GodCommands.IssueEdict(defName)`,
   and surface `EdictOption.Reason` on the disabled ones — every refusal already carries a
   sentence, and a greyed-out control with no explanation is the exact failure that field
   exists to prevent. Do not reimplement the availability rules to explain them; a
   reimplemented rule drifts from the one the simulation enforces. At a tribal start
   exactly one edict is issuable and four are era-locked, so both states appear on screen
   without contriving anything.

## The honest summary

Phase 1 built every system in isolation and tested it there. The count of ported
systems is real. What has never been true is that they add up to a played game: the
work economy is a third wired, nothing drives the tiering, and there is no surface to
look at. Those three are the distance between a tested library and a game, and they
are what the next stretch is for.
