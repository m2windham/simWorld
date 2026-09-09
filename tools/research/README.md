# Tech-tree reachability harness

A console harness that answers "how much of the 232-project tech tree does a played
game actually reach?" by simulating research progress over a long horizon under
scripted player archetypes across a fixed seed panel.

Findings live in `docs/research/tech-reachability.md`. This file is about running and
extending the tool.

It is deliberately **not** in `SimWorld.sln` and **not** in CI: it is a research
instrument, not shipped code. It takes no NuGet dependencies, so it restores offline.

## Running it

```sh
export PATH=/root/.dotnet:$PATH
dotnet run -c Release --project tools/research/ReachHarness.csproj all
```

Commands:

| Command | What it prints |
| --- | --- |
| `tree` | Static shape of the shipped tree: size, cost, depth, connectivity, unlock consequence |
| `baseline` | The headline panel, as shipped and with the tech level tracking the era, plus the shipped scenarios' starting eras |
| `eras` | Era shape: how much of an era a civilization has seen when it leaves it, and whether archetypes diverge (see the report's §10) |
| `sweep` | Sensitivity to throughput, horizon, starting era, researcher growth, attention and skill |
| `modifiers` | Every candidate fix, measured against the same panel |
| `all` | All of the above (about eight minutes) |
| `run <spec> [k=v ...]` | One panel with an arbitrary modifier stack and configuration |

`run` accepts `horizon`, `researchers`, `start`, `attention`, `skill`, `growth`,
`cap`, `jitter`, `ticks` and `skillgrowth`:

```sh
dotnet run -c Release --project tools/research/ReachHarness.csproj \
  run costscale2,techtrack horizon=25 researchers=4 start=Industrial
```

## What it actually simulates

The tech tree, the completion gate and the cost curve are the real shipped ones: the
harness loads `Data/Core/Defs` through `CoreContent`, drives a real
`ResearchManager`, asks `ResearchProjectDef.CanStartNow` what may be started, and
pays `ResearchProjectDef.CostFactor` against `ResearchManager.ResearcherTechLevel`.
Starting eras replicate `ScenPart_StartingEra`. Researcher skill runs on a real
`SkillRecord`, so the XP curve, the passion factor, the daily learning saturation and
the level-10-and-up decay table are the shipped ones too.

The research *economy* is modelled, because SimWorld does not ship one yet — there is
no `ResearchSpeed` StatDef, no research `WorkGiver`, and no research building:

```text
points/day = researchers x WorkTicksPerDay x ResearchPointsPerWorkTick x speed(skill)
speed(skill) = 0.08 + 0.115 x IntellectualLevel        (1.0 at level 8)
```

Every constant behind that, and every unsourced assumption, is listed with its
sensitivity in `docs/research/tech-reachability.md`. Read that before trusting a
number out of this tool.

## Adding a policy

A policy is a scripted player. Subclass `ResearchPolicy` in `Policies.cs` and
register it in `PolicyRegistry.All()`; it then joins every report automatically.

```csharp
public sealed class MyPolicy : ResearchPolicy
{
    public override string Name => "my-policy";

    // Optional: run once per pick, for anything expensive the ranking needs.
    protected override void Prepare(PolicyContext ctx) { }

    // Lower is better: pick the lowest tier, then the lowest key after jitter.
    protected override (int, float) Rank(ResearchProjectDef p, PolicyContext ctx) =>
        (0, ctx.RemainingCost(p));
}
```

Notes:

- `Rank` returns `(tier, key)`. Tiers are compared first and exactly; keys are
  compared after multiplicative noise (`RunConfig.ChoiceJitter`), so seeds separate
  near-ties the way a real player's attention would. Keys must be non-negative.
- `RequiredFor(ctx, targets)` gives the union of the prerequisite closures of a set of
  targets — the building block for any beeline archetype.
- Override `AttentionOverride` to model a player who mostly is not researching, and
  `SwitchChancePerDay` to model one who changes target mid-project.
- The archetype named `idle` is the control and is excluded from every "engaged"
  aggregate. If you rename it, update `PanelResult.ControlPolicy`.

## Adding a modifier

A modifier is a candidate design change, applied to the tree **in memory only**. The
shipped XML is never written; `TreeModel.Restore` puts every cost and prerequisite
list back between configurations.

Subclass `Modifier` in `Modifiers.cs` and add a token to `ModifierRegistry.Parse`:

```csharp
public sealed class MyModifier : Modifier
{
    public override string Name => "mine";

    // Edit costs or prerequisites, or add to `excluded` to cut authored surface.
    public override void Apply(TreeModel tree, HashSet<ResearchProjectDef> excluded) { }

    // Change the run's economy rather than its content.
    public override void Configure(RunConfig config) { }

    // Replace the shipped era-completion rule.
    public override EraRule? EraRuleOverride => null;
}
```

Notes:

- Call `tree.Recompute()` after changing costs or prerequisites; depth, out-degree and
  closure costs are cached.
- Only exclude projects nothing else depends on, or the surviving DAG dangles.
  `TrimModifier` does this iteratively and stops when it cannot cut safely.
- Stacks are comma-separated and applied in order: `"techtrack,costscale2,spine"`.

The modifiers that ship:

| Token | Effect |
| --- | --- |
| `techtrack` | `ResearcherTechLevel` follows `CurrentEra` instead of being set once at game start |
| `throughput<x>` | Multiply the day's research points by `x` |
| `costscale<x>` | Multiply every project's cost by `x` |
| `costfreeze<n>` | Rescale every era after order `n` to era `n`'s mean cost |
| `trim<n>` | Keep at most `n` projects per era, dropping dead-end leaves first |
| `flatten<d>` | Cap prerequisite depth at `d` by rebasing too-deep edges |
| `eragate` | A project is only researchable once its era is open |
| `spine` | An era turns on its spine — the projects something else depends on |
| `era<pct>` | An era turns at `pct` percent of its projects |

## Determinism

Every run is seeded through `RandomStream`, one stream per run, per `CLAUDE.md`'s
determinism rule. The same command prints the same numbers. The panel is 24 fixed
seeds (`RunConfig.DefaultSeeds`); the harness is single-threaded because it mutates
the process-wide `DefDatabase.Global` and the thread-static `Find.ResearchManager`.
