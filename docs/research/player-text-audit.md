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
- Generic words RimWorld also uses: biomes, "orbital" traders and visitors, IED
  trap, ancient asphalt, steel, silver, jade, uranium. (Round 2 below revisited the
  trait, mental-break, interaction and pemmican names this line used to list.)
- "pawn" in debug output (`Lord.ToString`, the host renderer's status line) and in
  test messages; the player-visible "pawns" in scenario summaries and the rescue
  work-giver label were reworded anyway.
- `EraDefs` and `EdictDefs` belong to the `era-progression` lane. The only hits
  there are the `Spacer` / `Archotech` tech levels above.

## Round 2 (2026-11-24): system vocabulary

A playtester who knows the source game still recognised its vocabulary: not proper
names but its coined terms and exact phrasings. Plain words used plainly (Cooking,
Hunting, Mining, Hauling, Cleaning, Doctor, Research) stay. Area labels are not
saved, so renaming them needs no save migration; no other renamed text is saved.

| Where | Old | New |
| --- | --- | --- |
| Alerts | Minor / Major / Extreme break risk | Low spirits / Near breaking / At breaking point |
| Alerts, need and stat text | mental break(s), mental break threshold | breakdown(s), breakdown threshold |
| Alert | Fire in home area | Fire within the town bounds |
| Areas | Home area / Snow clear / Build roof | Town bounds / Snow shovelling / Roofing |
| Work columns | Firefight / Patient / Bed rest / Basic | Fire watch / Seek care / Convalesce / Chores |
| Work columns | Warden / Handling / Constr. / Growing / Plants | Captives / Husbandry / Building / Farming / Felling |
| Work columns | Smith / Tailor / Art / Craft | Smithing / Tailoring / Artistry / Crafting |
| Work refusal | X is incapable of firefighting because of being pyromaniac. | X will never take on fire watch, owing to a pyromaniac nature. |
| Mental breaks | sad wander, hide in room, food binge, insulting spree | aimless grief, shut away, gorging, lashing out |
| Mental breaks | tantrum, sadistic rage, fire starting spree, berserk | smashing fit, cruel streak, fire-setting frenzy, blood frenzy |
| Mental breaks | murderous rage, catatonic breakdown, give up and leave | killing grudge, stupor, walking away |
| Mental states | Mental state: X; social fighting; hiding in room | State of mind: X; brawling; shut away |
| Thought | catharsis | weight lifted |
| Needs | recreation, beauty, comfort, outdoors, space | leisure, surroundings, ease, open air, elbow room |
| Thoughts | recreation-starved etc.; hideous/ugly/pretty/beautiful environment | leisure-starved etc.; ... surroundings |
| Traits | too smart; industriousness (very industrious, industrious) | overclever; work ethic (tireless, diligent) |
| Traits | natural mood (sanguine, optimist, pessimist, depressive) | temperament (sunny, hopeful, gloomy, melancholic) |
| Traits | nerves (iron-willed, steadfast, volatile) | composure (unshakeable, level-headed, brittle) |
| Interactions | chitchat, deep talk, slight | small talk, heart-to-heart, snub |
| Jobs | beat fire, put out own fire, tend patient, feed patient | smother fire, put out own flames, treat the sick, feed the sick |
| Jobs | clear snow, skygaze, social fight, "doing a bill." | shovel snow, watch the sky, brawl, "working an order." |
| Work givers | tend patients (emergency), X bills, refuel fuelable buildings | treat the sick (urgent), X orders, refuel buildings |
| Bench commands | bill(s) | work order(s) |
| Food | simple / fine / lavish meal, pemmican | plain meal / hearty meal / feast platter, trail cake |
| Items | herbal medicine | herbal salve |
| Buildings | stonecutter's table, passive cooler | masons' bench, clay cooler |
| Plants | potato / rice / cotton plant | potato / rice / cotton crop |
| People | tribesperson, tribespeople | clan member, clansfolk |
| Stat | bed rest effectiveness | convalescence effectiveness |
| Storytellers | descriptions | rewritten in each teller's own voice |
| Host | Break risk; Home; Snow clearing; Bills | Breaking point; Town bounds; Snow shovelling; Work orders |

The terms are banned as whole phrases in `player-text-banned.txt`, so "break",
"home" and "meal" stay free on their own. Left alone: trait names that are plain
English (pyromaniac, gourmand, brawler), the "incapable" skill marker in the
citizen panel's skill list, and job defs with no label, which the host shows as
their defName spaced out.
