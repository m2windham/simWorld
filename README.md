# simWorld

A civilization-scale god-game built on RimWorld-depth simulation: every citizen is a
full agent. Phase 1 ports RimWorld's systems 1:1 as isolated, tested modules; the
god-game layer comes after.

## Layout

| Path | What |
| --- | --- |
| `src/SimWorld.Core/` | Engine-free simulation core (C# 9, `netstandard2.1`). No `UnityEngine` references; Unity consumes it as a local package. |
| `tests/SimWorld.Core.Tests/` | xUnit suite (`dotnet test`). |
| `docs/research/rimworld-mechanics.md` | RimWorld systems reference with diagrams per system. |
| `docs/spec/simworld-spec.md` | SimWorld layered-architecture spec. |
| `docs/status.json` | Single source of truth for the build tracker: systems, translations, checklists, decisions. |
| `tools/blueprint/` | Generates the SimWorld Blueprint page (research, spec, tracker) from the docs above. |

## Build and test

```sh
dotnet build
dotnet test
```

Requires the .NET 8 SDK (`global.json`). All `bin`/`obj` output lands under `artifacts/`.

## Unity

Package Manager → *Add package from disk* → `src/SimWorld.Core/package.json`. The assembly
definition has `noEngineReferences` on, so the core cannot accidentally depend on Unity.

## Blueprint page

```sh
cd tools/blueprint && npm ci && npm run build   # writes dist/blueprint.html
```

Edit `docs/status.json` to move the tracker; the page is regenerated from it.
