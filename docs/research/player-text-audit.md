# Player-text audit: no RimWorld names on screen

Doug, 2026-11-24: "nothing 'rim world' is player facing. ensure that." Internal
defNames, class names, enum members and ported code keep RimWorld's names; anything a
player can read is simWorld's own. This note records what the audit found, what was
renamed, and what was left alone on purpose.

## The guard

- The banned list is one file:
  `tests/SimWorld.Core.Tests/Content/player-text-banned.txt`. Terms match
  case-insensitively as whole words, so the defName `ManhunterPack` is not a hit and
  "manhunter pack" is.
- `PlayerTextTests` (core) walks every string a shipped def holds, however deeply
  nested (thought stages, scenario parts, name banks, faction prefixes), skipping
  `defName` and strings that are exactly a defName; checks the four calendar labels
  and a year of date readouts; and scans every string literal in
  `src/SimWorld.Core/**/*.cs`, comments excluded.
- The host's `PlayerTextTests` reads the same file beside the core checkout it builds
  against and scans `Assets/Scripts` literals and the text assets under `Assets`.

## Renames

Defs keep their defNames, so saves (which store defNames) load unchanged. The
`Quadrum` enum keeps its members; only `GenDate.QuadrumLabel` changed, and no save
stores a quadrum.

| Where | Was | Now |
| --- | --- | --- |
| Storyteller `Cassandra_Classic` | Cassandra Classic | The Annalist |
| Storyteller `Phoebe_Chillax` | Phoebe Chillax | The Hearthkeeper |
| Storyteller `Randy_Random` | Randy Random | The Trickster |
| Calendar | Aprimay, Jugust, Septober, Decembary | Seedtide, Suntide, Reaptide, Frosttide |
| Animal `Muffalo` (race and kind) | muffalo | woolly ox |
| Incident `ManhunterPack` | manhunter pack; "Manhunter pack: X" letter | maddened beasts; "Maddened beasts: X" |
| Incident and condition `Flashstorm` | flashstorm | dry thunderstorm |
| Incident category `ShipChunkDrop` | ship chunk drop | falling wreckage |
| Faction `OutlanderCivilization` | outlander civilization, outlander(s) | borderland civilization, borderer(s) |
| Faction `RoughOutlanders` | rough outlanders | reaver band |
| Trader `Caravan_Outlander_BulkGoods` | outlander bulk goods caravan | borderland bulk goods caravan |
| Chemical `Yayo`, item `Flake` | yayo, flake | brightdust |
| Plant `Plant_Healroot`, crop demand | healroot | comfrey |
| Item `Plasteel`, `MineablePlasteel` | plasteel, compacted plasteel | starmetal, compacted starmetal |
| Research `ArchotechSeeds` | archotech seeds | demiurge seeds |
| Scenario part `StartingIdeo` | starting ideoligion; "N ideoligions" | starting faith; "N faiths" |
| Xenotype `Baseliner` | baseliner | commonborn |
| Pawn kind `Colonist` | colonist | settler |
| Alerts, trainable text | colonist(s) | citizen(s) |

## Left alone

- `TechLevel.Spacer` / `TechLevel.Archotech` on research projects and eras: enum
  values, never shown by the view or the host.
- Generic words RimWorld also uses: trait names (too smart, iron-willed, gourmand),
  mental breaks (sadistic rage, catharsis), interactions (chitchat, deep talk),
  biomes, "orbital" traders and visitors, IED trap, ancient asphalt, steel, silver,
  jade, uranium, pemmican.
- "pawn" in debug output (`Lord.ToString`, the host renderer's status line) and in
  test messages; the player-visible "pawns" in scenario summaries and the rescue
  work-giver label were reworded anyway.
- `EraDefs` and `EdictDefs` belong to the `era-progression` lane. The only hits
  there are the `Spacer` / `Archotech` tech levels above.
