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

## Round 3 (2026-11-25): presentation

A fresh tester (playtest 4, `H:/dev/_builds/playtest4/report.md`) still read whole
sets as borrowed even where each word was plain: the twelve-skill list with its
passions, the trait list, the mood reasons, the break line, the mark verbs and the
numbered work grid. This round reshapes the presentation as well as the words. No
renamed text is saved: saves store defNames, and the letters and zone labels a save
does keep are free text that loads either way.

| Where | Old | New |
| --- | --- | --- |
| Skills `Shooting` / `Melee` | shooting / melee | marksmanship / close fighting |
| Skills `Construction` / `Plants` / `Animals` | construction / plants / animals | building / farming / husbandry (the work columns' own names) |
| Skills `Crafting` / `Artistic` / `Medicine` | crafting / artistic / medicine | handcraft / artistry / healing |
| Skills `Social` / `Intellectual` | social / intellectual | speech / learning |
| Skills `Mining` / `Cooking` | mining / cooking | kept: the honest names |
| Skill `pawnLabel`s | Shooter, Grower, Handler, Crafter, Doctor, Socialite, Professor | Marksman, Farmer, Herder, Maker, Healer, Speaker, Scholar |
| Host citizen panel | "Skills", twelve rows | "Know-how", four grouped lines: Arms; Land and beasts; Handiwork; Care and wits |
| Host citizen panel | interested / passionate / incapable | keen / devoted / unable |
| Traits | pyromaniac, bloodlust, psychopath, kind, ascetic | fire-touched, battle-hungry, cold-hearted, gentle, plain-living |
| Traits | brawler, tough, jealous, greedy, abrasive, nimble | scrapper, hardy, envious, grasping, sharp-tongued, light-footed |
| Traits | fast learner, slow learner, gourmand, nervous | quick study, slow study, food-lover, jumpy |
| Trait `Squeamishness` | squeamishness: squeamish, faint-hearted | weak stomach: weak-stomached, blood-shy |
| Genes | fast learner, slow learner | quick to learn, slow to learn |
| Precept, thoughts | bloodlust celebrated / honored, kindness honored | battle-hunger celebrated / honored, gentleness honored |
| Thoughts (temperament) | sanguine, optimist, pessimist, depressive | sunny / hopeful / gloomy / melancholic nature |
| Thought `Expectations` | extremely low ... sky-high expectations | asks nothing of the world, asks little of the world, used to plain living, wants a settled life, wants to live well, wants the best of everything |
| Thoughts | ate without table, ate raw food | no table to eat at, ate it raw |
| Thoughts | slept on ground, slept outside, slept in barracks | slept on bare earth, slept under the sky, slept in a crowded hall |
| Thoughts | insulted, witnessed ally's death, ravenously hungry | stung by harsh words, saw a friend fall, famished |
| Thoughts | had a nice chat, was snubbed, rebuffed | passed the time of day, given the cold shoulder, turned down |
| Thoughts | cathartic fight, angering fight | cleared the air with fists, a fight that settled nothing |
| Thoughts | soaking wet, immodest exposure | drenched to the skin, bare before the forge |
| Host citizen panel | Breaking point: "none: mood 62%, 27 points above the minor break line (35%)" | Spirits: "comfortably above breaking" / "steady, a fair way above breaking" / "holding, but close to low spirits" |
| Host citizen panel | "low spirits: mood 30% is under 35%; near breaking under 20%" | "low spirits: an outburst could come" (and "a bad breakdown could come", "anything could set them off") |
| Marks (designation, job, work giver) `CutPlant` | cut / cut plant / cut plants; cutting plant | clear growth; clearing growth |
| Marks `Deconstruct` | deconstruct; deconstructing | take down; taking down |
| Marks `HaulUrgently` | haul urgently | carry first |
| Marks: mine, harvest, hunt, tame, slaughter | | kept: plain verbs |
| Mark answers | Designated 3 CutPlant target(s) over 12 cell(s); already designated; N designation(s) | Marked 3 to clear growth across 12 cells; already marked; N mark(s) |
| Work grid (core words, host cells) | 1-4, "1 is done first, blank is never" | First / Soon / Later / Last; an empty cell is work they leave alone |
| Work priority answers | "now does Mining at priority 2"; "use 0 (never)" | "now does Mining soon"; "0 leaves the work alone" |
| Roofs | constructed roof, thin rock roof, thick rock roof | roof, rock overhang, cave ceiling |
| Host inspector | Roof: X; Category: Building | Overhead: X; Kind: a structure (or something growing, loose goods, ...) |
| Host stock | Nutrition; "61519 units in all" | Food; "61519 things stored" |
| Host research | Study Fire (next era) | Study Fire, toward the next era |
| Host era panel | (research 3 of 7, conditions 0 of 1); (31 of 25) | (3 of 7 studies done, its one goal still to meet); (31 now, 25 needed) |
| Host era panel | Their raiders carry Sticks and stones arms. | Their raiders are armed as in the Sticks and stones era. |
| Host speed | Speed 5 (at 47%) | Speed 5 (behind): the bar has no room to explain a share |
| Drought letter and condition | watched or not | in every settlement, near or far |

The retired phrases are banned in `player-text-banned.txt` as whole phrases. Common
words stay off the list even where one was retired here, because banning them would
catch ordinary text: the plain skill names (shooting, construction, plants, animals,
crafting, medicine, social, melee), the plain trait names (kind, tough, greedy,
nimble, nervous), "insulted", and the passion words "interested" and "passionate"
(a thought says "I wasn't interested"). Those are held instead by the host's tests
on the know-how lines and by this table. "minor break" is not listed either:
"break line" already catches the old phrasing, and the longer term would hide the
round-2 "break risk" match inside "minor break risk".

Left alone in round 3, and why:

- Stat and job labels that name a skill's use ("melee hit chance", "shooting accuracy",
  "melee attack"): plain descriptions of a mechanic. The job label can show as what a
  fighter is doing; the tester did not flag it, and it is worth a look next round.
- "lazy", "sick", "tired", "exhausted", "hungry", "starving", "in pain", "has a
  friend", "has a rival", "broke up", "divorced", "new lover", "unwanted advance": plain
  English that any game would use.
- "earthen roof": simWorld's own (the pit house's sod roof).
- The Mine, Hunt, Tame, Slaughter and Harvest marks: the honest verbs.
- Work type columns: renamed in round 2; this round only changed the numbers in
  their cells.
