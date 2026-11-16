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
            return "dwellings: " + dwellings.Count + " built, " + DwellingUtility.CountUnderway(map) + " under way; households "
                + housedHouseholds + "/" + households.Count + " housed; citizens housed "
                + DwellingUtility.HousedCitizens(settlement, map) + "/" + settlement.Citizens.Count(p => !p.Dead)
                + "; dormitory beds " + map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count;
        }
    }
}
