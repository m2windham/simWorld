using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>What a household furnishes its house with (comfort lane; content in <c>Buildings_Furniture.xml</c>).</summary>
    [DefOf]
    public static class FurnitureThingDefOf
    {
        public static ThingDef DiningTable = null!;
        public static ThingDef Furnishing_Agrarian = null!;
        public static ThingDef Furnishing_Bronze = null!;
        public static ThingDef Furnishing_Classical = null!;
        public static ThingDef Furnishing_Medieval = null!;
        public static ThingDef Furnishing_Industrial = null!;
        public static ThingDef Furnishing_Information = null!;
        public static ThingDef Furnishing_Exotic = null!;
    }

    /// <summary>
    /// Translation: <b>a household furnishes its own house</b>, because there is no player to place the table
    /// and the art. RimWorld's colonists live better as the colony gets richer because the player builds them
    /// better rooms: furniture of quality, floors, sculptures (room impressiveness, <see cref="RoomStats"/>).
    /// Here the town builds for itself, so the era is what furnishes a house: a household gets a table once the
    /// town knows how to make one, and the furnishings of its age on the house's furnishing spot, replacing the
    /// last age's in place as the town climbs (<see cref="GenConstruct.ReplaceTag"/>). The house rungs say where
    /// (<see cref="DwellingPartKind.TableSpot"/>, <see cref="DwellingPartKind.FurnishingSpot"/>); a pit house has
    /// no spot for either, which is the point.
    ///
    /// <para/>Only a finished house a household lives in is furnished, and nothing is planned while
    /// <see cref="RenewalInitiative.WaitReason"/> says the town is short of food or under attack: comfort never
    /// outranks either. A bed shortage alone does not wait it, as it does not wait a renewal that adds beds. At most <see cref="MaxUnderwayPerCitizens"/> sites at a time, the shape of
    /// <see cref="RenewalTuning.MaxUnderway"/>, so a town furnishes a house or two at a time rather than diverting
    /// every builder at once. No state of its own: everything is read off the map each pass.
    /// </summary>
    public static class HouseFurnishing
    {
        /// <summary>One furnishing site underway per this many citizens, at least one. SimWorld's own figure.</summary>
        public const int MaxUnderwayPerCitizens = 15;

        /// <summary>Every era's furnishing, oldest first, read off their research gates.</summary>
        public static IReadOnlyList<ThingDef> Ladder => new[]
        {
            FurnitureThingDefOf.Furnishing_Agrarian, FurnitureThingDefOf.Furnishing_Bronze,
            FurnitureThingDefOf.Furnishing_Classical, FurnitureThingDefOf.Furnishing_Medieval,
            FurnitureThingDefOf.Furnishing_Industrial, FurnitureThingDefOf.Furnishing_Information,
            FurnitureThingDefOf.Furnishing_Exotic,
        }.OrderBy(d => EraAvailability.EraOf(d)?.order ?? -1).ToArray();

        public static bool IsFurnishing(ThingDef def) =>
            ReferenceEquals(def, FurnitureThingDefOf.Furnishing_Agrarian) || ReferenceEquals(def, FurnitureThingDefOf.Furnishing_Bronze)
            || ReferenceEquals(def, FurnitureThingDefOf.Furnishing_Classical) || ReferenceEquals(def, FurnitureThingDefOf.Furnishing_Medieval)
            || ReferenceEquals(def, FurnitureThingDefOf.Furnishing_Industrial) || ReferenceEquals(def, FurnitureThingDefOf.Furnishing_Information)
            || ReferenceEquals(def, FurnitureThingDefOf.Furnishing_Exotic);

        /// <summary>The newest furnishing the town knows how to make, or null before the first.</summary>
        public static ThingDef? BestKnownFurnishing()
        {
            IReadOnlyList<ThingDef> ladder = Ladder;
            for (int i = ladder.Count - 1; i >= 0; i--)
            {
                if (ladder[i].IsResearchFinished) return ladder[i];
            }
            return null;
        }

        private static int Rank(ThingDef def)
        {
            IReadOnlyList<ThingDef> ladder = Ladder;
            for (int i = 0; i < ladder.Count; i++)
            {
                if (ReferenceEquals(ladder[i], def)) return i;
            }
            return -1;
        }

        /// <summary>One pass over the settlement's houses; returns how many sites it planned.</summary>
        public static int Tick(Settlement settlement, Map.Map map)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (map == null) throw new ArgumentNullException(nameof(map));
            IReadOnlyList<Dwelling> dwellings = DwellingUtility.DwellingsOn(map);
            if (dwellings.Count == 0) return 0;
            // A bed shortage does not hold a table back: a growing town is short of beds for years at a time, and
            // its beds are planned by their own pass. Hunger and a raid do.
            string? wait = RenewalInitiative.WaitReason(settlement, map);
            if (wait != null && wait != RenewalInitiative.BedsShort) return 0;

            int budget = Math.Max(1, settlement.Citizens.Count(p => !p.Dead) / MaxUnderwayPerCitizens) - SitesUnderway(map, dwellings);
            ThingDef? furnishing = BestKnownFurnishing();
            bool table = FurnitureThingDefOf.DiningTable.IsResearchFinished;
            int placed = 0;
            for (int i = 0; i < dwellings.Count && placed < budget; i++)
            {
                Dwelling d = dwellings[i];
                if (d.householdId == Pawn_RelationsTracker.None || !d.IsComplete(map)) continue;
                if (table) placed += FurnishTable(map, d);
                if (furnishing != null && placed < budget) placed += Furnish(map, d, furnishing);
            }
            return placed;
        }

        private static int FurnishTable(Map.Map map, Dwelling d)
        {
            ThingDef def = FurnitureThingDefOf.DiningTable;
            foreach (IntVec3 spot in d.Spots(DwellingPartKind.TableSpot))
            {
                if (DwellingPlanner.SiteAt(map, spot) || FirstOf(map, spot, t => ReferenceEquals(t.def, def)) != null) continue;
                if (!GenConstruct.CanPlaceBlueprintAt(def, spot, map, out _)) continue;
                ThingDef? blueprintDef = GenConstruct.BlueprintDefFor(def);
                if (blueprintDef == null) continue;
                var blueprint = (Blueprint)ThingMaker.MakeThing(blueprintDef);
                blueprint.stuffToUse = GenStuff.AffordableStuffFor(map, def, StuffDefOf.WoodLog);
                GenSpawn.Spawn(blueprint, spot, map, Rot4.North);
                return 1;
            }
            return 0;
        }

        private static int Furnish(Map.Map map, Dwelling d, ThingDef best)
        {
            foreach (IntVec3 spot in d.Spots(DwellingPartKind.FurnishingSpot))
            {
                if (DwellingPlanner.SiteAt(map, spot)) continue;
                Thing? standing = FirstOf(map, spot, t => IsFurnishing(t.def));
                if (standing == null)
                {
                    if (!GenConstruct.CanPlaceBlueprintAt(best, spot, map, out _)) continue;
                    ThingDef? blueprintDef = GenConstruct.BlueprintDefFor(best);
                    if (blueprintDef == null) continue;
                    GenSpawn.Spawn(ThingMaker.MakeThing(blueprintDef), spot, map, Rot4.North);
                    return 1;
                }
                if (Rank(standing.def) >= Rank(best)) continue;
                if (GenConstruct.PlaceReplacement(best, null, spot, map) != null) return 1;
            }
            return 0;
        }

        private static int SitesUnderway(Map.Map map, IReadOnlyList<Dwelling> dwellings)
        {
            int n = 0;
            for (int i = 0; i < dwellings.Count; i++)
            {
                Dwelling d = dwellings[i];
                foreach (IntVec3 spot in d.Spots(DwellingPartKind.TableSpot).Concat(d.Spots(DwellingPartKind.FurnishingSpot)))
                {
                    if (DwellingPlanner.SiteAt(map, spot)) n++;
                }
            }
            return n;
        }

        private static Thing? FirstOf(Map.Map map, IntVec3 cell, Func<Thing, bool> match)
        {
            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i].def.category == ThingCategory.Building && match(here[i])) return here[i];
            }
            return null;
        }
    }
}
