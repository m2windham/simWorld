using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.God.View
{
    /// <summary>
    /// The god's choice of what the town keeps (<c>docs/design/era-progression.md</c> §7.6). A kept structure is
    /// heritage: repaired in its own form and stuff, never renewed. Picking any wall, door or bed of a house keeps
    /// the whole house.
    /// </summary>
    public static partial class GodCommands
    {
        /// <summary>
        /// Marks (<paramref name="heritage"/> true) or unmarks the structure with thing id <paramref name="thingId"/>
        /// on any of the god's settlement maps. Refused when there is no such building, when the house is already
        /// being replaced or pulled down, and when the town already keeps as much as it may
        /// (<see cref="RenewalInitiative.HeritageCap"/>, about one structure in ten).
        /// </summary>
        public static GodCommandResult SetHeritage(int thingId, bool heritage)
        {
            World.World? world = Find.World;
            if (world == null) return GodCommandResult.Refused("There is no world.");
            IReadOnlyList<World.WorldObject> objects = world.worldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (!(objects[i] is World.Settlement s) || s.InteriorMap == null) continue;
                Map.Map map = s.InteriorMap;
                Building.Building? building = FindBuilding(map, thingId);
                if (building == null) continue;

                Dwelling? house = DwellingUtility.DwellingAt(map, building.Position);
                bool current = house?.isHeritage ?? building.isHeritage;
                if (current == heritage) return GodCommandResult.NoChange(heritage ? "It is already kept." : "It was not kept.");
                if (house != null && (house.replacedById != 0 || house.beingDemolished))
                {
                    return GodCommandResult.Refused("That house is already being replaced.");
                }
                if (heritage && RenewalInitiative.HeritageCount(map) >= RenewalInitiative.HeritageCap(map))
                {
                    return GodCommandResult.Refused("The town already keeps as much of its past as it can.");
                }
                if (house != null) RenewalInitiative.SetHeritage(map, house, heritage);
                else building.isHeritage = heritage;
                return GodCommandResult.Done(heritage ? "It will be kept as it is." : "It may be rebuilt.");
            }
            return GodCommandResult.Refused("There is no such building.");
        }

        private static Building.Building? FindBuilding(Map.Map map, int thingId)
        {
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i].thingIDNumber == thingId && buildings[i] is Building.Building b) return b;
            }
            return null;
        }
    }
}
