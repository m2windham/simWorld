using System.Linq;

using SimWorld.Building;

namespace SimWorld.Bench.Suites
{
    /// <summary>One line on how a settlement is housed at the end of a run: dwellings standing, households,
    /// households with a home, citizens sleeping in one. Printed by <see cref="StorytellerSuite"/>.</summary>
    internal static class DwellingReading
    {
        public static string Line(SimWorld.World.Settlement settlement)
        {
            Map.Map? map = settlement.InteriorMap;
            if (map == null) return "dwellings: (settlement has no map)";
            var dwellings = DwellingUtility.DwellingsOn(map);
            var households = DwellingUtility.Households(settlement);
            int housedHouseholds = dwellings.Count(d => households.ContainsKey(d.householdId));
            int built = dwellings.Count(d => d.IsComplete(map));
            string rungs = string.Join(",", dwellings.GroupBy(d => d.def.defName).OrderBy(g => g.Min(d => d.def.rung)).Select(g => g.Key + "x" + g.Count()));
            return "dwellings: " + built + " built, " + DwellingUtility.CountUnderway(map) + " under way [" + rungs + "]; households "
                + housedHouseholds + "/" + households.Count + " housed; citizens housed "
                + DwellingUtility.HousedCitizens(settlement, map) + "/" + settlement.Citizens.Count(p => !p.Dead)
                + "; beds " + map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count
                + "\n" + RenewalLine();
        }

        /// <summary>The town's renewal (era-progression.md 7.5): structures in the current or previous era's form
        /// over all, by category, with the heritage count and why renewal waits, if it does.</summary>
        internal static string RenewalLine()
        {
            God.View.RenewalSummary r = God.View.GodViewSnapshot.Capture().Era.Renewal;
            string categories = string.Join(", ", r.Categories.Select(c =>
                c.Category + " " + c.InCurrentEra + "/" + c.InCurrentOrPreviousEra + "/" + c.Count + (c.Underway > 0 ? " (" + c.Underway + " renewing)" : "")));
            return "renewal: share " + (r.Share * 100f).ToString("0") + "% [current/recent/all: " + categories + "]; heritage "
                + r.HeritageCount + (r.WaitingFor != null ? "; waiting: " + r.WaitingFor : "");
        }

        /// <summary>Per sample: the renewal line, plus what the home pass would meet next if it ran now (the
        /// house it would build, whether the stock holds it, whether a plot is free, the candidates), read off
        /// the core's public calls without changing anything.</summary>
        public static string SampleLine(SimWorld.World.Settlement settlement)
        {
            Map.Map? map = settlement.InteriorMap;
            if (map == null || map.settlementCompound == null) return "renewal: (no compound)";
            DwellingDef? target = DwellingUtility.PreferredDwellingDef();
            var homes = DwellingUtility.DwellingsOn(map);
            int obsolete = homes.Count(d => !d.isHeritage && d.replacedById == 0 && !d.beingDemolished && target != null && d.def.rung < target.rung);
            int underway = homes.Count(d => d.replacedById != 0);
            int citizens = settlement.Citizens.Count(p => !p.Dead);
            int beds = map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count;
            string plot = target == null ? "-" : DwellingPlanner.TryFindPlot(map, map.settlementCompound, target) != null ? "free" : "none";
            string afford = target == null ? "-" : RenewalInitiative.Affordable(map, target) ? "yes" : "no";
            return RenewalLine() + "\n  renewal next: target " + (target?.defName ?? "-") + ", obsolete homes " + obsolete
                + ", renewing " + underway + ", houses going up " + DwellingUtility.CountUnderway(map) + ", affordable " + afford
                + ", plot " + plot + ", beds " + beds + "/" + citizens
                + "\n  building stock (map+ledger): " + BuildingStock(settlement, map);
        }

        /// <summary>What the town holds of the materials it builds with, on the map and in the ledger: wood,
        /// stone chunks and blocks, and the stuffs a wall, door or store can be made of.</summary>
        private static string BuildingStock(SimWorld.World.Settlement settlement, Map.Map map)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (Defs.ThingDef def in Defs.DefDatabase<Defs.ThingDef>.AllDefsListForReading)
            {
                bool material = def.stuffProps != null || def.defName.StartsWith("Chunk", System.StringComparison.Ordinal)
                    || def.defName.StartsWith("Blocks", System.StringComparison.Ordinal) || def.defName.StartsWith("Ore", System.StringComparison.Ordinal);
                if (!material || def.IsNutritionGivingIngestible) continue;
                int onMap = Crafting.MaterialLadder.OnMap(map, def);
                int ledger = settlement.StoreCountOf(def);
                if (onMap + ledger > 0) parts.Add(def.defName + " " + onMap + "+" + ledger);
            }
            return parts.Count == 0 ? "-" : string.Join(", ", parts);
        }
    }
}
