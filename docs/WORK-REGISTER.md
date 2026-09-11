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

### 1. Seven work types have no worker — down from eighteen

**Updated.** Four lanes wired eleven of the eighteen: hauling, research, the four
crafting-bill givers plus cooking, and four of the doctor family. Work givers carrying a
real `giverClass` went from 10 of 28 to **21 of 28**.

A settlement can now haul, research at a bench, craft and cook at benches, and treat,
rescue and feed its wounded — none of which it could do before.

Seven remain bare, and three of those are not work to do. `HaulCorpses`, `CleanFilth` and
`FightFires` have no `Corpse`, `Filth` or `Fire` class anywhere in this codebase: they are
blocked on systems that do not exist, and a stub worker would be worse than the honest
gap. `WardenDeliverFood` is deliberately left — `DoctorFeedHumanlikes` reuses its
mechanism with a different target filter, and the two would race one reservation.
`Hunt`, `Repair` and `PlantsCut` are simply next.

Known gaps inside what did land, recorded rather than discovered later: crafting reserves
the bench but not individual ingredient stacks, so a second consumer can take the pile
between a job being offered and finishing; `DoBillsArt` is wired but has no bench, since a
sculpture needs a beauty and quality subsystem this port has not built; a pawn never tends
itself, matching RimWorld, so a lone injured citizen with nobody else around goes
untended.

### The original finding, for the record

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

1. **Start a real game and report what the snapshot contains.** `Game.NewGame`, tribal start,
   run long enough that something happens. Population by tier, era and progress, whether the
   means look sane, whether the chronicle fills, whether settlements come with interior maps.
   This is a report rather than a feature: the host can see it and the core cannot, and two
   core bugs have now been found this way.
2. **Expect every citizen to read as `Full`.** Nothing drives the tiering yet. A core lane is
   fixing that and will land a focus concept — the god focuses zero or one settlement, and that
   is what attention means. **The host should not build its own notion of a selected
   settlement**; two disagreeing ideas of what the player is looking at is a bug that will take
   a week to find.
3. **Then the civilization view proper.** Settlements, population, era, chronicle.
   `GodViewSnapshot` carries all of it, and `RecentHistory` versus `Moments` deserves different
   treatment on screen — running news against the civilization's landmarks.

## The honest summary

Phase 1 built every system in isolation and tested it there. The count of ported
systems is real. What has never been true is that they add up to a played game: the
work economy is a third wired, nothing drives the tiering, and there is no surface to
look at. Those three are the distance between a tested library and a game, and they
are what the next stretch is for.
