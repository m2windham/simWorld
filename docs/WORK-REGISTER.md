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

| Owner                   | Repo            | Paths                                                     | Work                                                         |
| ------------------------ | --------------- | ---------------------------------------------------------- | -------------------------------------------------------------- |
| `simworld-37` (cloud)   | `simWorld`      | `src/SimWorld.Core/**`, `tests/**`, `tools/**`, `docs/**` | The engine-free core, its tests, and these docs               |
| `mcp-bridge-fc` (local) | `simWorld.Host` | `Assets/**`, `Packages/**`, `ProjectSettings/**`          | The Unity host: the god view, read and write, and its tests  |

`docs/status.json` has exactly one writer: whoever runs the suite and measures
`testsTotal`. It conflicts on every merge otherwise.

## Landed

| Owner | Repo            | Work                                                                                    | PR  |
| ----- | --------------- | ---------------------------------------------------------------------------------------- | --- |
| cloud | `simWorld`      | God-view read model (`God/View`), spec §12a                                             | #46 |
| cloud | `simWorld`      | Core/host split and the provenance rule, in `CLAUDE.md`                                 | #47 |
| local | `simWorld.Host` | Project scaffold, `com.simworld.core` as a local package, Pipeline+MCP Editor reachability | (this PR) |
| local | `simWorld.Host` | God view read (`GodViewBootstrap`) and write (`EdictPanel` → `GodCommands.IssueEdict`), 3 EditMode tests, `ContentLoaded` adopted | (this PR) |

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

**Updated.** The host caught up past the proposed order below — items 1 and 3 landed
before item 2 did, since content-loading turned out to block the write path too, not
just the read side.

`A:\dev\simWorld.Host`, own git repo (not pushed to a remote yet), references
`com.simworld.core` as a local package by the relative path this file asked for.
`CoreContentBootstrap.EnsureLoaded()` loads the shipped content into
`DefDatabase.Global` — pointing `SIMWORLD_DATA` at this repo explicitly, since
`CoreContent`'s own upward directory walk never finds a sibling repo. First attempt used
`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` and fired too early: `CoreContent.Load`
returned success with zero defs, no error — exactly the trap `ContentLoaded` now exists
to catch, hit from the other direction. Moved to run from each consumer's `Awake()`
instead, adopted `ContentLoaded` once it landed here, and made a zero-edict load a hard
failure so that failure mode cannot go quiet again on either side.

Both halves of spec §12a are wired and verified live, not just from source: a
`GodViewBootstrap` reads `GodViewSnapshot.Capture()` on a timer (never calls
`Game.NewGame`, so the pre-world read stays honest); an `EdictPanel` writes through
`GodCommands.IssueEdict`, one row per `EdictOption`, button interactable only when
`Availability == Available`, `Reason` surfaced verbatim rather than re-derived. Clicked
the one real available edict (`HuntersMandate`) through code, got `Done: Hunter's
mandate issued.` back, and the next read showed it flip to `Active` with `Reason` now
`In force.` Three EditMode tests cover this (content loads, `Capture` safe pre-world,
issue round-trips through a fresh snapshot) and pass, run via the official Unity
Pipeline CLI's `run_tests` against the live Editor instance rather than a fresh
batchmode process (which conflicts with an Editor that already has the project open).

Still open on this side:

1. **Start a real game** (`Game.NewGame`, tribal start, solo) and report what the
   snapshot actually contains — not done yet, still the proposal below.
2. Visual polish. Everything so far is legacy `UnityEngine.UI.Text`, default anchoring
   just barely made sane. Functionally proven, not close to a real UI yet.
3. No remote for `simWorld.Host` yet, so nothing here is a link anyone else can pull.

Proposed next, unchanged from before since it was not reached:

1. **Start a real game** (`Game.NewGame`, tribal start, solo) and **report what the
   snapshot actually contains** — population by tier, era and progress, whether the means
   look sane, whether the chronicle fills. The ask here is a report rather than a feature:
   the host can see this and the core cannot. The last number that looked wrong found a
   bug.

## The honest summary

Phase 1 built every system in isolation and tested it there. The count of ported
systems is real. What has never been true is that they add up to a played game: the
work economy is a third wired, nothing drives the tiering, and there is no surface to
look at. Those three are the distance between a tested library and a game, and they
are what the next stretch is for.
