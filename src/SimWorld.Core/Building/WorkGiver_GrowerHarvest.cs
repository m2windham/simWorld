using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>
    /// Harvests every reachable, unreserved, harvestable-now <see cref="Plant"/> on the map (RimWorld:
    /// <c>RimWorld.WorkGiver_GrowerHarvest</c>). Thing-scanning, like <c>WorkGiver_Miner</c>. <b>Deviation
    /// from RimWorld, deliberately kept:</b> not restricted to plants inside a growing zone — RimWorld's own
    /// harvester scans every harvestable plant on the map, zones only control where sowing happens.
    ///
    /// <para/><b>Ripe, unless the settlement is short of food.</b> RimWorld's grower harvests a plant only
    /// once it is fully grown (<c>LifeStage == Mature</c>). A crop is harvestable sooner, from 65% growth at
    /// half the yield (rimworldwiki.com/wiki/Plants), but only by a player who designates the harvest, and a
    /// player does that when the larder runs out before the crop ripens. There is no player, so the
    /// settlement makes that call on the food economy's one shortage figure
    /// (<see cref="HuntingInitiative.ShortOfFood(Map.Map)"/>, the same one that sends hunters out): while it
    /// is short, a food plant past its <see cref="Defs.PlantProperties.harvestMinGrowth"/> is harvest work;
    /// otherwise only a fully grown one is. The gate closes as soon as the early harvest has put enough on
    /// the map, so the rest of the field keeps growing.
    /// </summary>
    public sealed class WorkGiver_GrowerHarvest : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant)) yield return t;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Plant plant) || !plant.Spawned || !plant.HarvestableNow) return false;
            // A wild tree is not a crop. RimWorld's grower only harvests inside a growing zone; a tree outside
            // one is felled by a Chop Wood designation instead. The deviation above (every plant, not only
            // zoned ones) exists for wild food, and read literally it would have every grower on the map
            // clear-cutting the forest. A wild tree is WorkGiver_ConstructChopWood's, which fells one only
            // when construction is short of wood.
            if (plant.def.plant!.IsTree && !(plant.Map!.zoneManager.ZoneAt(plant.Position) is Zone_Growing)) return false;
            if (!plant.FullyGrown && !EarlyHarvestWanted(plant)) return false;
            if (!Reachability.CanReach(pawn, plant, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, plant);
        }

        /// <summary>
        /// Whether a harvestable but not yet ripe <paramref name="plant"/> should be taken now: it bears food
        /// and its settlement is short of it. A tree (felled early for wood inside a zone, as before) is not
        /// held back. See the class doc.
        /// </summary>
        public static bool EarlyHarvestWanted(Plant plant)
        {
            if (plant == null) throw new System.ArgumentNullException(nameof(plant));
            if (plant.def.plant!.IsTree) return true;
            if (!(plant.def.plant.harvestedThingDef?.IsNutritionGivingIngestible ?? false)) return false;
            return ShortOfFoodThisTick(plant.Map!);
        }

        private static Map.Map? memoMap;
        private static int memoTick = -1;
        private static bool memoShort;

        /// <summary>
        /// <see cref="HuntingInitiative.ShortOfFood(Map.Map)"/>, read once per map per tick. A job search asks
        /// this of every unripe plant in a field, and a field is a thousand cells; the figure walks the map's
        /// items and the ledger, and cannot change between two plants of the same search.
        /// </summary>
        private static bool ShortOfFoodThisTick(Map.Map map)
        {
            int tick = Find.TickManager.TicksGame;
            if (!ReferenceEquals(map, memoMap) || tick != memoTick)
            {
                memoShort = HuntingInitiative.ShortOfFood(map);
                memoMap = map;
                memoTick = tick;
            }
            return memoShort;
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(BuildingJobDefOf.Harvest, thing);
    }
}
