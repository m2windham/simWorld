using System;
using SimWorld.Map;

namespace SimWorld.Things
{
    /// <summary>Places a Thing onto a map (RimWorld: <c>Verse.GenSpawn</c>).</summary>
    public static class GenSpawn
    {
        /// <summary>
        /// Sets <paramref name="thing"/>'s position and rotation and spawns it on <paramref name="map"/>.
        /// Throws when <paramref name="loc"/> is outside the map.
        /// </summary>
        public static Thing Spawn(Thing thing, IntVec3 loc, Map.Map map, Rot4 rot = default)
        {
            if (thing == null) throw new ArgumentNullException(nameof(thing));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!GenGrid.InBounds(loc, map))
            {
                throw new ArgumentOutOfRangeException(nameof(loc), loc + " is outside map bounds " + map.Size + ".");
            }
            thing.Position = loc;
            thing.Rotation = rot;
            thing.SpawnSetup(map, respawningAfterLoad: false);
            return thing;
        }
    }
}
