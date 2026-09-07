# Working in this repository

Conventions for anyone — human or agent — adding to SimWorld. Architecture lives
in `docs/spec/simworld-spec.md`; per-system progress in `docs/status.json`.

## Build and test

```sh
export PATH=/root/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet test tests/SimWorld.Core.Tests/SimWorld.Core.Tests.csproj
dotnet build -c Release          # must report 0 warnings, 0 errors
```

`TreatWarningsAsErrors` is on and nullable is enabled: a warning fails the build.
Build output goes to `artifacts/`, not `bin/` and `obj/`.

## Ground rules

- **The core is engine-free.** No `UnityEngine` reference in `SimWorld.Core`,
  ever. Unity consumes it as a local package.
- **Port RimWorld 1:1 first, translate second.** Keep RimWorld's names, call
  shapes and constants. Where a number could not be sourced, say so in a comment
  and pin the behaviour with a test rather than the literal value.
- **Translations are recorded, not improvised.** A civilization-scale change to a
  system goes in that system's `translation` field in `docs/status.json`.
- **Determinism is a feature.** All randomness goes through a seeded
  `RandomStream`. Never `System.Random`, never `DateTime.Now`.

## Adding a module

1. Code under `src/SimWorld.Core/<System>/`, namespace `SimWorld.<System>`.
2. Content XML under `src/SimWorld.Core/Data/Core/Defs/<DefTypeName>s/`.
3. Tests under `tests/SimWorld.Core.Tests/<System>/`, namespace
   `SimWorld.Tests.<System>`.
4. Update the system's entry in `docs/status.json`: `status`, `module`, `tests`,
   and the `items` checklist. Set `testsTotal` to the new suite total.
5. Regenerate the tracker: `cd tools/blueprint && npm run build`.

A system is `ported` when its mechanics, its content and its tests are in. Items
that belong to a system but depend on an unbuilt one stay `done: false` with a
label saying which module they land with.

## Content XML

- Element name is the Def class name; `<defName>` is required and unique.
- Def references are bare `defName` strings and resolve after all files load, so
  files may reference each other in any order.
- `Name` / `ParentName` / `Abstract="True"` for inheritance; `Inherit="False"`
  to break a field.
- `Class="Full.Type.Name"` on a list item selects a concrete type; `Type`-valued
  fields take full type names.
- Lists use `<li>`; `IntRange` and `FloatRange` parse as `"min~max"`; enums by
  name; curves as `(x,y)` points.
- Every `[DefOf]` field must exist in content — the content test asserts the load
  produces zero config and zero DefOf errors.

## Namespaces that shadow their own types

`SimWorld.Map` is both a namespace and, inside it, the class `Map`. Same for
`SimWorld.World`. From any other namespace a bare `Map` or `World` resolves to
the **namespace**, not the type — even with `using SimWorld.Map;` present — and
fails with CS0118. Write `Map.Map` and `World.World`, as `Thing.cs` and
`GenSpawn.cs` already do. It costs a confusing build error every time someone
touches these types from outside, so reach for the qualified form first.

## Tests

- Derive from `ContentTestBase` (`tests/…/Content/CoreContentFixture.cs`). It
  loads the shipped content once, points `DefDatabase.Global` at it, and gives
  each test a fresh `TickManager`, a seeded `Rand` and a reset thing-id counter.
- Helpers: `NewHuman()`, `Human`, `Husky`, `Trait(name)`,
  `RunTicks(ticks, pawns)`.
- Tests reading `DefDatabase.Global` share the `"GlobalDefs"` collection so they
  never run in parallel with each other.
- Inside `SimWorld.Tests.X`, a reference to `SimWorld.X` can resolve to the test
  namespace — qualify with `global::` when it bites.
- Assert behaviour over literals for anything tuned: a band, a trend or a
  round-trip, not a magic number you cannot source.
- Every module needs a Scribe round-trip test.

## Git

- Work on the designated feature branch; never push to `main`.
- PRs squash-merge once CI is green (build and test, blueprint, markdown lint,
  link check, mermaid validation).
- Parallel work happens in git worktrees, one per module, merged into the
  feature branch one at a time so CI stays serial.
