using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using SimWorld.Building;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Thoughts;

namespace SimWorld.Bench.Suites
{
    /// <summary>
    /// What a settlement's mood is made of at one sample: every thought group its living citizens hold, by
    /// def, averaged over all of them (a citizen without the thought counts as zero), with the share who hold
    /// it. Reads <see cref="ThoughtHandler.MoodThoughtGroupsAsComputed"/>, so taking it never moves the
    /// simulation. The top six on each side are printed; the sum of every average is the mean offset.
    /// </summary>
    internal static class MoodReading
    {
        private const int Shown = 6;

        /// <summary>How the settlement's houses are furnished and how their bedrooms score (comfort lane): houses
        /// standing, how many have a table, the furnishings by def, and the bedrooms' impressiveness stages.</summary>
        public static string ComfortLine(SimWorld.World.Settlement settlement)
        {
            Map.Map? map = settlement.InteriorMap;
            if (map == null) return "comfort: (no map)";
            int houses = 0, tables = 0;
            var furnishings = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            var stages = new int[RoomStats.ImpressivenessStages.Length];
            float sum = 0f, wealth = 0f, beauty = 0f, space = 0f, clean = 0f;
            int scored = 0;
            foreach (Dwelling d in DwellingUtility.DwellingsOn(map))
            {
                if (!d.IsComplete(map)) continue;
                houses++;
                foreach (IntVec3 c in d.Spots(DwellingPartKind.TableSpot))
                {
                    if (map.thingGrid.ThingsListAt(c).Any(t => ReferenceEquals(t.def, FurnitureThingDefOf.DiningTable))) tables++;
                }
                foreach (IntVec3 c in d.Spots(DwellingPartKind.FurnishingSpot))
                {
                    foreach (var t in map.thingGrid.ThingsListAt(c).Where(t => HouseFurnishing.IsFurnishing(t.def)))
                    {
                        furnishings[t.def.defName] = furnishings.TryGetValue(t.def.defName, out int n) ? n + 1 : 1;
                    }
                }
                var beds = d.Beds(map);
                if (beds.Count == 0) continue;
                Room? room = map.roomTracker.RoomAt(beds[0].Position);
                if (!RoomStats.IsEnclosed(room)) continue;
                float score = RoomStats.Impressiveness(room!, map);
                wealth += RoomStats.Wealth(room!, map);
                beauty += RoomStats.Beauty(room!, map);
                space += RoomStats.Space(room!, map);
                clean += RoomStats.Cleanliness(room!, map);
                sum += score;
                scored++;
                stages[RoomStats.StageOf(score)]++;
            }
            return "comfort: houses " + houses + ", tables " + tables + ", furnishings ["
                + string.Join(", ", furnishings.Select(kv => kv.Key + " " + kv.Value)) + "], bedrooms scored " + scored
                + (scored > 0 ? ", mean impressiveness " + (sum / scored).ToString("0.0", CultureInfo.InvariantCulture) : "")
                + ", by stage [" + string.Join(" ", stages) + "]"
                + (scored > 0 ? ", mean wealth/beauty/space/cleanliness " + F(wealth / scored, "0") + "/" + F(beauty / scored, "0.00")
                    + "/" + F(space / scored, "0.0") + "/" + F(clean / scored, "0.00") : "")
                + "; food on map by preferability [" + FoodByPreferability(map) + "]";
        }

        private static string F(float v, string format) => v.ToString(format, CultureInfo.InvariantCulture);

        /// <summary>Spawned ingestible food on the map, stack counts summed by its preferability.</summary>
        private static string FoodByPreferability(Map.Map map)
        {
            var counts = new SortedDictionary<SimWorld.Crafting.FoodPreferability, int>();
            foreach (var t in map.listerThings.AllThings)
            {
                var pref = t.def.ingestible?.preferability;
                if (pref == null || t.def.category != SimWorld.Defs.ThingCategory.Item) continue;
                counts[pref.Value] = counts.TryGetValue(pref.Value, out int n) ? n + t.stackCount : t.stackCount;
            }
            return string.Join(", ", counts.Select(kv => kv.Key + " " + kv.Value));
        }

        public static string Line(SimWorld.World.Settlement settlement)
        {
            var sums = new Dictionary<string, (float Sum, int Holders)>();
            int citizens = 0;
            float meanLevel = 0f;
            foreach (Pawn pawn in settlement.Citizens)
            {
                if (pawn.Dead || pawn.needs?.mood == null) continue;
                citizens++;
                meanLevel += pawn.needs.mood.CurLevelPercentage;
                // A social memory groups per other pawn, so one citizen can hold several groups of one def: sum
                // them, and count the citizen once.
                var mine = new Dictionary<string, float>();
                foreach (List<Thought> group in pawn.needs.mood.thoughts.MoodThoughtGroupsAsComputed())
                {
                    string key = group[0].def.defName;
                    float offset = ThoughtHandler.StackedMoodOffset(group);
                    mine[key] = mine.TryGetValue(key, out float had) ? had + offset : offset;
                }
                foreach (KeyValuePair<string, float> kv in mine)
                {
                    sums[kv.Key] = sums.TryGetValue(kv.Key, out var acc) ? (acc.Sum + kv.Value, acc.Holders + 1) : (kv.Value, 1);
                }
            }
            if (citizens == 0) return "mood thoughts: (no citizens with mood)";

            var averaged = sums.Select(kv => (Def: kv.Key, Avg: kv.Value.Sum / citizens, Share: kv.Value.Holders / (float)citizens)).ToList();
            float total = averaged.Sum(a => a.Avg);
            string Part((string Def, float Avg, float Share) a) =>
                a.Def + " " + a.Avg.ToString("+0.0;-0.0", CultureInfo.InvariantCulture)
                + " (" + (a.Share * 100f).ToString("0", CultureInfo.InvariantCulture) + "%)";
            string up = string.Join(", ", averaged.Where(a => a.Avg > 0f).OrderByDescending(a => a.Avg).Take(Shown).Select(Part));
            string down = string.Join(", ", averaged.Where(a => a.Avg < 0f).OrderBy(a => a.Avg).Take(Shown).Select(Part));
            return "mood thoughts over " + citizens + " citizens, mean level "
                + (meanLevel / citizens).ToString("0.00", CultureInfo.InvariantCulture) + ", mean offset "
                + total.ToString("+0.0;-0.0", CultureInfo.InvariantCulture)
                + ": up [" + (up.Length == 0 ? "-" : up) + "]; down [" + (down.Length == 0 ? "-" : down) + "]";
        }
    }
}
