using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// The two plant-felling work givers, <see cref="AI.WorkGiver_PlantsCut"/> and
    /// <see cref="WorkGiver_ConstructChopWood"/>, offer the plants that stand where something is being built
    /// (and, for the first, in a growing zone they do not belong in). RimWorld keeps those as a short list of
    /// player designations; this port derives them from the map, and used to do it by asking every plant on the
    /// map whether it qualified. These helpers find the same plants by looking where they have to be instead.
    /// </summary>
    internal static class PlantScanUtility
    {
        /// <summary>Every in-bounds cell under a blueprint or a frame. A plant "stands on a site" exactly when
        /// its cell is one of these (<see cref="ThingGrid"/> registers a multi-cell Thing under each cell of its
        /// <see cref="Thing.OccupiedRect"/>).</summary>
        public static IEnumerable<IntVec3> SiteCells(Map.Map map)
        {
            foreach (ThingRequestGroup group in SiteGroups)
            {
                IReadOnlyList<Thing> sites = map.listerThings.ThingsInGroup(group);
                for (int i = 0; i < sites.Count; i++)
                {
                    foreach (IntVec3 c in sites[i].OccupiedRect().Cells)
                    {
                        if (GenGrid.InBounds(c, map)) yield return c;
                    }
                }
            }
        }

        private static readonly ThingRequestGroup[] SiteGroups = { ThingRequestGroup.Blueprint, ThingRequestGroup.BuildingFrame };

        /// <summary>Adds to <paramref name="found"/> each <see cref="Plant"/> in <paramref name="cell"/>'s thing
        /// list that <paramref name="keep"/> accepts. A plant reached from two cells (a site over a growing
        /// zone) is added twice; <see cref="InMapOrder"/> yields each once, and is not looking a plant up in a
        /// list to find out, which would make a field of wrong crops quadratic.</summary>
        public static void CollectPlantsAt(Map.Map map, IntVec3 cell, ref List<Plant>? found, PlantTest keep)
        {
            IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Plant plant && keep(plant)) (found ??= new List<Plant>()).Add(plant);
            }
        }

        public delegate bool PlantTest(Plant plant);

        /// <summary>
        /// <paramref name="plants"/> in the order the map's own plant list holds them. A scan keeps the first
        /// of several equally near candidates, so the order candidates arrive in decides which of them is
        /// worked first; handing over the few plants a cell lookup found in the same order the whole-list walk
        /// would have met them keeps that decision exactly where it was. One plant needs no ordering.
        /// </summary>
        public static IEnumerable<Thing> InMapOrder(Map.Map map, List<Plant> plants)
        {
            if (plants.Count == 1)
            {
                yield return plants[0];
                yield break;
            }

            var wanted = new HashSet<Plant>(plants);
            IReadOnlyList<Thing> all = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is Plant p && wanted.Contains(p)) yield return p;
            }
        }
    }
}
