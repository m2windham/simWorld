using System;
using SimWorld.Map;

namespace SimWorld.Things
{
    /// <summary>Places a Thing onto a map (RimWorld: <c>Verse.GenSpawn</c>).</summary>
    public static class GenSpawn
    {
        /// <summary>
        /// Sets <paramref name="thing"/>'s position and rotation and spawns it on <paramref name="map"/>.
        /// Throws when the footprint it would occupy is not entirely inside the map.
        ///
        /// <para/><b>The whole footprint, not the centre cell.</b> A Thing with a <c>ThingDef.size</c> larger
        /// than 1x1 registers in <see cref="Map.ThingGrid"/> and <see cref="Map.EdificeGrid"/> under every
        /// cell of its <see cref="Thing.OccupiedRect"/>, and both of those silently skip cells that fall off
        /// the map. Checking only the centre would therefore let a 1x2 bed spawn half outside, register in one
        /// cell, and then deregister from one cell while its <c>OccupiedRect</c> still claims two — a Thing
        /// the grids half-remember. <c>Bed</c> is the first shipped def with a footprint (1x2).
        /// </summary>
        public static Thing Spawn(Thing thing, IntVec3 loc, Map.Map map, Rot4 rot = default)
        {
            if (thing == null) throw new ArgumentNullException(nameof(thing));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!GenGrid.InBounds(loc, map))
            {
                throw new ArgumentOutOfRangeException(nameof(loc), loc + " is outside map bounds " + map.Size + ".");
            }

            CellRect footprint = GenAdj.OccupiedRect(loc, rot, thing.Size);
            if (footprint.width > 1 || footprint.height > 1)
            {
                foreach (IntVec3 c in footprint.Cells)
                {
                    if (GenGrid.InBounds(c, map)) continue;
                    throw new ArgumentOutOfRangeException(
                        nameof(loc),
                        thing.def.defName + " at " + loc + " would occupy " + c + ", outside map bounds " + map.Size + ".");
                }
            }
            thing.Position = loc;
            thing.Rotation = rot;
            thing.SpawnSetup(map, respawningAfterLoad: false);
            return thing;
        }
    }
}
