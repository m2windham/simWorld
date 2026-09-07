# Tech tree generator

`gen_techtree.py` owns every `ResearchProjectDef` in `src/SimWorld.Core/Data/Core/Defs/ResearchProjectDefs/`.
Don't hand-edit those XML files -- edit the data table in the script and regenerate instead.

To edit the tree: open `gen_techtree.py` and find the `ERA_<NAME>` list for the era you want (e.g.
`ERA_BRONZE`). Each entry is one project: `defName` (unique, never rename an existing one -- code
and tests reference several by name), `label`, `track` (must be one of the `TRACKS` tuples), a
`prereqs` list of other defNames (same era or earlier only), and a one-sentence `desc`. Cost,
tech level, tags, and the research-view grid position are all derived automatically from era and
track, so you never set them by hand.

To regenerate: `python3 tools/content/gen_techtree.py` (stdlib only, no dependencies). It
validates the whole table (unique names/labels, no missing or cyclic or later-era prerequisites,
costs in band, per-era/per-track minimums) before writing anything, prints a summary, and rewrites
every `Research_<order>_<EraDefName>.xml` file in that directory from scratch. Run it, then run
the test suite (`dotnet test tests/SimWorld.Core.Tests`) to confirm nothing broke.
