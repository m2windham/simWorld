# Player first

The rule every design decision in this project is held to, and the reasoning
behind it. This is for whoever is briefing the next piece of work — human or
agent — and it outranks any local preference about how a system "should" be
built.

## 1. The premise

This is a game for a human player. The simulation is a closed container that
plays out on its own; the player does not supply the outcome, they supply the
**inputs**. Their logic, right or wrong, is what the machinery takes and runs
with.

That has one immediate consequence, and it is the reason this document exists:

> **The simulation being correct is not the same as the game being playable.**

Every system in this repository can be right — ported faithfully, tested,
measured — while the player has nothing to touch. That is not a hypothetical.
Phase 2 built seven ways for the world to hurt a settlement, and at settlement
scale the player was still a spectator. Every `Blueprint`, every growing zone,
every stockpile in the game came from an AI initiative. The pressure was real
and there was no hand to answer it with.

## 2. The lever test

A player input is legitimate if the simulation can carry it to its consequence
**through its own machinery**.

| Input | Path | Verdict |
| --- | --- | --- |
| "Build a granary here" | Blueprint → a hauler brings materials → a builder raises it | Legitimate |
| "This one leads" | A role assignment the office system already reads | Legitimate |
| "Set her age to 40" | There is no path. This writes a value into state | Not a lever |

The corollary is the part that changes how you work:

> **To give the player a new kind of influence, you build the causal path. You
> never expose a setter.**

When someone says "the player should be able to do X", the question is never
"where do we put the button". It is **"what in the simulation would carry
X to its consequence, and does it exist?"** If it does, the work is a thin
command wrapper. If it does not, the work is building the machinery, and the
button comes last.

This is not a house rule. Every game surveyed enforces exactly this line, and
enforces it structurally: RimWorld's direct state edits — revive, teleport, set
a stat — live in a separate Development mode behind an explicit toggle, which
the community treats as debugging rather than playing. Cities: Skylines is
stricter still: there is no documented way to set a building's fire risk or a
citizen's happiness at all, only the causes. Factorio has no privileged
player-only verb whatsoever — the player is just another entity placing
entities. If we ever want a state editor, it goes in a labelled debug layer,
and it is never reachable from play.

## 3. The two players, and why both get served

Some people play for automation and efficiency. Some play to hand-guide a
civilization and see what happens. These are not opposed and we do not choose
between them. They differ only in **what kind of input they like authoring**:

- **The optimiser authors standing rules.** A bill, a work policy, a storage
  filter, a role. Set it once; it keeps deciding.
- **The roleplayer authors specific acts.** Build this, here, now. Send her.
  Cancel that.

Both are the player's own logic. Both must be applied faithfully. A game that
only takes standing rules is a spreadsheet; a game that only takes single acts
is a chore at any scale above a dozen people.

The strongest examples do not bolt a roleplay layer next to an optimisation
layer. They make the optimiser's own instrumented numbers **be** the story
material — RimWorld's storyteller reads the same colony wealth, population and
mood the player is already managing, and narrates them. Where the two layers
stay structurally separate, players self-segregate into two audiences and each
gets half a game.

## 4. Where a lever can sit

Every lever has a **granularity** and a **persistence**. Both are design
choices, and the grid is a checklist for "have we thought about this one":

| | One-time act | Standing rule |
| --- | --- | --- |
| **Individual** | Order this person to do this thing | This person's role, their medical tier |
| **Group** | Send this band | This guild's work, this office's holder |
| **Place** | Place a blueprint, cancel a designation | A growing zone, a stockpile and its filter |
| **Global** | *(deliberately empty — see below)* | An edict, a research focus, a standing order |

**The "global, one-time" cell is empty in every game surveyed, and it should
stay empty here.** Nothing ships a button that acts once on an entire
population. The reason is not squeamishness: a one-time global act is almost
impossible to distinguish from a state edit. If you find yourself designing
one, you have probably found a setter wearing a hat.

## 5. Rules that fall out

**A bad decision must be allowed to be bad.** Refuse the physically
impossible — off the map, a cell already occupied, an unknown def, no map
open. Never refuse the unwise. A field on poor soil is accepted and grows
slower for it. A wall in a useless place goes up. The player's choices are
the point of the game; a game that protects them from their own judgement has
removed the thing they came for. `MapCommands` pins this with a test rather
than stating it: Sand terrain is forced under a rice zone and the command
returns `Done`.

**Every refusal explains itself, in the simulation's own words.** A greyed-out
control with no reason is a bug report waiting to be filed, and an explanation
re-derived at the seam is an explanation that will drift from the rule it
describes. `EdictOption.Reason` carries the simulation's own wording and the
command surface reuses it verbatim. Do the same for anything new.

**A one-time act interrupts a standing rule; it does not rewrite it.** Three
studios converged on this independently and named it three ways — RimWorld's
"prioritise" jumps the queue without touching the priority grid, Dwarf
Fortress calls the split *active* versus *passive* orders, Oxygen Not
Included's sub-priority breaks ties inside a tier. The standing rule resumes
on its own when the act completes. Build it that way.

**In-flight work is never pre-empted.** A person finishes the job they are on
before reconsidering, even if something more urgent appears. Two unrelated
studios both chose this deliberately: atomicity of the current act beats
freshness of the rule.

**A standing rule that has silently stopped enforcing itself must say so.**
Dwarf Fortress's perpetual work orders validate their conditions once, at
creation, and never again — the UI gives no hint that a standing rule has
quietly become a one-off. That is the cautionary case. Any rule of ours that
can stop re-evaluating needs to be visibly marked.

**Cost is the point.** A cost the player can shrug off is not a decision. This
is already in `CLAUDE.md` and it belongs here too, because the temptation to
soften a consequence always arrives dressed as usability.

## 6. What scale does, and our answer to it

The evidence here is unambiguous and it constrains us.

Per-individual standing rules do not survive scale. RimWorld's work grid is
pawns × 23 work types with no bulk edit; past roughly fifteen people it needs
mods, and two opposite families of mod exist — one to make the grid usable, one
to hand it to an AI so the player never sees it again. Dwarf Fortress's
per-dwarf labour screen has needed an *external application* for over a decade,
and its control failure and its performance failure are the same event: forts
collapse to single-digit FPS at exactly the population where individual control
was already unmanageable.

The games that scale cleanly either never expose the individual as a control
unit (Cities: Skylines' floor is the district) or their "individuals" are
stateless automatons (Factorio's belts and trains).

We cannot take either escape. We have individuals with needs, moods, health and
relationships, and losing them would cost us the thing the project is for. So:

- **Roles, not per-pawn grids.** `Pawn_WorkSettings.SetPriority` mechanically
  passes the lever test — the grid is real and `WorkGiversInOrderNormal` reads
  it honestly. It stays unexposed anyway, because a per-pawn priority table is
  the exact colony-management granularity the civilization reframing was
  written to avoid. `RoleDef` is the lever; `manualPriorities` already exists to
  let a specific override survive a role being re-applied, so a targeted
  exception remains possible later without re-opening the grid.
- **Template the rules; do not coarsen the dial.** Factorio sidesteps the whole
  individual-versus-aggregate tension because one placement instantiates an
  arbitrarily large structure of standing rules. That is a better answer than
  Songs of Syx's — which coarsened control to race × job-category and still
  draws scale complaints. Prefer a lever that lets the player author a pattern
  once and apply it widely.

## 7. Keep the drill-down

Frostpunk 2 moved from named citizens to faction statistics and was reviewed on
exactly what it lost: *"when a handful of them die from exposure to the cold, it
doesn't sting the way it did in the first game."* That is the nearest published
failure to where this project sits.

Our depth is not the risk. Burying it is. So:

> **There must be an unbroken path from the civilization aggregate down to one
> named person's actual state and history.**

A dashboard that only ever reports totals has made the Frostpunk 2 mistake
whatever is running underneath it. This is a hard requirement on any view we
build, and it is the one item on this list a competitor has already proven by
getting it wrong in public.

## 8. The reason to play comes from the player — but occasions do not

The player brings the reason. The game's job is to keep producing **occasions**
worth having a reason about.

"Ran out of things to do" is the failure mode in every game surveyed — Banished,
Cities: Skylines once traffic is solved, Factorio after the rocket, RimWorld's
late game, and Nova Roma within months of release. In no case was the answer
more content; Factorio's actual fix was to bolt a second game on top. The single
mechanism that never fully runs out is the one that does not pre-author goals at
all, but reads live state and selects from it.

We already have that component. `MomentCurator` decides what was worth
remembering. Structurally it is the same machine as a storyteller, pointed
backwards. The design position this document takes is that it should also point
forwards: **what surfaces to the player and when**, not only what is recorded
afterwards.

And a note on measurement, because it cuts the same way: `tools/bench --suite
probe` reports population, food, mood and deaths, and none of those is fun.
Tuned against them alone the optimum is a settlement that never starves, never
loses anybody and never has a bad day. Flat is the failure mode. Near-misses and
recoveries are what make a run worth telling somebody about.

## 9. How to brief work under this doctrine

For any proposed player-facing feature, answer these in the brief:

1. **What decision is the player making?** If you cannot name it, cut the
   feature. This applies to readouts as much as controls — a number nobody acts
   on is decoration with a frame cost.
2. **What carries it to its consequence?** Name the existing machinery, by
   type and file. If nothing does, the work is building that path, and say so.
3. **Which cell of the grid is it?** Granularity and persistence, both stated.
4. **What does it refuse, and what does it deliberately allow?** The allowed-
   but-unwise cases need tests, not intentions.
5. **Does it survive scale?** If it is per-individual and per-attribute, say
   how it behaves at five hundred citizens.

And one verification rule, inherited from everything this project has found the
hard way: **a mechanism with no caller is not a feature.** The audit in §10
exists because that failure has now happened at least nine times in this
codebase. A test calling something is not a caller. Search `src/`.

## 10. Where we actually stand

As of this document, verified by searching every call site in `src/` rather than
assuming.

**Reachable by a player:** the six `GodCommands` (six edicts, attention and
focus), the four `MapCommands` (`PlaceBlueprint`, `CancelDesignation`,
`MarkGrowingZone`, `MarkStockpile`), and the starting site at game creation.

**Built, tested, and never called from `src/`:**

| Mechanism | Note |
| --- | --- |
| `Pawn_JobTracker.QueueJob` | `Job.playerForced` and `JobGiver_DirectedOrder` exist and are wired into the think tree above `JobGiver_Work`. The whole "order this person to do this thing" path is complete and has never been called. The single highest-leverage gap. |
| `Faction.DeclareWar` / `SignTreaty` | Full goodwill, refusal and treaty machinery. No caller, not even AI. |
| `WorkPolicyUtility.ApplyRoleToPopulation` | The aggregate policy lever spec §10 describes in prose. Never wired. |
| `SurgeryUtility.PerformNextSurgery` | Its own doc admits the job driver is missing. |
| `SettlementTradeUtility.OpenSession` | A trader arrives with real stock and no session ever opens. |
| `Ideo` | A step further back: never instantiated. `new Ideo(` appears nowhere in `src/`, so precepts, rituals and ideological roles are dead weight regardless of any command surface. |
| `AreaManager.Home` | `Filth/CleaningBounds.cs:49` genuinely reads it, so its own comment claiming otherwise is stale — but nothing populates it, so it is a live read of a permanently empty area. |

The shape is worth naming, because it is the shape of nearly every defect this
project has found: **not a missing feature, but a finished one that nothing ever
picks up.** The player's hands are largely already built. They have never been
connected.
