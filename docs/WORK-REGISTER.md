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

### 1. Four work types have no worker — down from eighteen

Work givers carrying a real `giverClass` have gone **10 of 28 → 24 of 28** across two
batches. A settlement can now haul, research at a bench, craft and cook, treat, rescue
and feed its wounded, hunt, repair what it built and clear plants out of its own way.

Four remain bare, and three are not work to do. `HaulCorpses`, `CleanFilth` and
`FightFires` have no `Corpse`, `Filth` or `Fire` class anywhere in this codebase: they
are blocked on systems that do not exist, and a stub worker would be worse than the
honest gap. `WardenDeliverFood` is deliberately left — `DoctorFeedHumanlikes` reuses its
mechanism with a different target filter.

**Hunting was wired and dormant for one batch.** `WorkGiver_Hunt` requires a ranged
weapon, `SettlementFounder` generates every citizen as `Tribesperson`, and that kind
shipped with no `weaponTags` — so no citizen in any generated game held a weapon. Every
unit test of the mechanism passed, because they arm their own pawns. Worth remembering as
a shape: a wired giver plus content that cannot reach it looks exactly like a finished
feature.

Known gaps inside what landed: `DoBillsArt` is wired with no bench, since sculpture needs
a beauty and quality subsystem this port has not built; a pawn never tends itself,
matching RimWorld, so a lone injured citizen with nobody around goes untended; a hunted
animal cannot fight back, because no attack `Job` or `JobGiver` exists anywhere. Bill
ingredient reservation, listed here as a gap last batch, is **closed**.

### The original finding, for the record

Ten `WorkGiverDef`s carried a `giverClass`. **Eighteen carried none at all**, so the work
type existed in content, appeared in a pawn's priorities, and could never produce a job. A
settlement could build, farm, mine, tame animals and hold prisoners, and could not haul,
cook, craft at a bench, treat an injury, or research anything — the tech tree, the era
ladder and the divergence work all stood above a research work type no citizen could
perform.

### 2. Attention drives the tiering — done, and it does not bound enough

**Wired.** Attention is the god's focus: zero or one settlement, named across the host
seam by world tile. `God.AttentionManager` applies focus changes immediately and
reconciles from `GodManager.GodTick`; citizens of unattended settlements fall to Interval
and settle to Statistical after a year of continuous insignificance.

What the measurement then showed is the part that matters. Focus bounds Full-tier to **one
settlement's live roster**, which is itself unbounded in time — demography doubles it
about every 17 years, and it crosses the spec's Full ceiling around **year 115–125**:

| year             | 10 | 40  | 60  | 80  | 100   | 120   | 160    |
| ---------------- | -- | --- | --- | --- | ----- | ----- | ------ |
| Full-tier roster | 67 | 163 | 300 | 656 | 1,412 | 3,179 | 16,868 |

The fix is a Full-tier cap **inside** the focused settlement — keep the N most significant
at Full, hold the rest at Interval even while attended. It is unclaimed and it needs a
decision first: a **significance ordering** between the four promotion reasons (role,
chronicle mention, relation, attention), which does not exist. Nothing ranks them today.

Two related holes, both unclaimed: `Notify_RoleChanged` has no caller because no Role
system exists, and `Notify_ChronicleNamed` has no policy for what earns a citizen
individual distinction. Attention is currently the only one of the four actually driven.

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
systems is real. What has never been true is that they add up to a played game: the
work economy is a third wired, nothing drives the tiering, and there is no surface to
look at. Those three are the distance between a tested library and a game, and they
are what the next stretch is for.
