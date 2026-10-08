# Map render

Draws a real core map with the host's real models, without Unity. Two steps: the
bench writes the settlement's `MapViewSnapshot` as JSON, and `render.js` draws that
JSON in headless Chromium with three.js, the FBX files and the shared `Palette.png`
from a sibling `simWorld.Host` checkout.

It exists because one picture showed what no metric did. On seed 777 every mountain
was mined away by day 6, and by day 20 there were 207 storage huts on the map for 29
people. Nobody was watching a number that moved.

## Run it

From the repository root, with `simWorld.Host` checked out beside this repository:

```sh
export PATH=/root/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet run -c Release --project tools/bench/SimWorld.Bench -- \
  --suite mapdump --seed 777 --solo false --capture-days 0,6,20

tools/maprender/fetch-three.sh            # once: three.js r160 into tools/maprender/vendor
export NODE_PATH=$(npm root -g)           # Playwright, installed globally in the cloud container
node tools/maprender/render.js --map artifacts/maprender/map-777-day6.json --seed 777 --out day6.png
node tools/maprender/render.js --map artifacts/maprender/map-777-day6.json --seed 777 \
  --view zoom --cx 98 --cz 95 --r 20 --out day6-settlement.png
node tools/maprender/render.js --gallery --out models.jpg
```

A `.jpg` output name writes a JPEG, which is far smaller for the full map.

## What it draws, and what it does not

- **Models** are placed the way they are authored: base on the ground, 1 unit = 1
  cell, centred on the thing's footprint, turned by `Rotation`. Plants scale with
  growth. The variant comes from the thing id, which is the host's
  `VisualRegistry.Resolve` rule.
- **Colour** comes from `Palette.png`. Terrain colours come from the host's
  `palette.json` `terrain` table.
- **No model**: a defName without one draws as a box coloured by category, so a gap
  is visible rather than hidden. Animals are boxes. Filth is not drawn.
- **Roofs**: built roofs show as a translucent red overlay. Natural rock roofs are
  not drawn, because the rock under them already is.

It is not the host's renderer. Where the two differ, the host review in
`simWorld.Host/docs/reviews/2026-10-08_MODELS_IN_THE_GAME_VIEW.md` says which one
is right and why.
