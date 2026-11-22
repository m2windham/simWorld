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
                + "; beds " + map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count;
        }
    }
}
