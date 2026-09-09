using System;
using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.MapGen
{
    /// <summary>
    /// Carries a settlement's world-tile roads onto its interior map as streets (spec §5, §11.2's seam).
    /// <see cref="World.Tile.Roads"/> only records which <em>neighbouring tile</em> each road actually
    /// reaches (<see cref="RoadLink.neighbor"/>); turning that into a real heading — so a road that comes
    /// from the north-east does not arrive from the south — needs the world grid's own tile positions
    /// (<see cref="World.WorldGrid.LongLatOf"/>), which is exactly what <see cref="MapGenContext.grid"/>
    /// exists to carry (see its own doc on why it can be null, and what this step does then).
    /// <para/>
    /// Each road link becomes a straight street from the point on the map's border facing its neighbour tile
    /// to the map's centre — a hub, not a guess at a second heading: with two or more links this reads as one
    /// street entering on one side and leaving on another (spec: "entering and leaving the map on the sides
    /// that face the neighbouring tiles the road actually connects"); with exactly one, it is honestly a
    /// dead end at the settlement rather than a fabricated far side. Links are carved lowest-<see cref="RoadDef.priority"/>
    /// first, so where two roads' spokes overlap near the centre, the higher-priority class's terrain is
    /// what is left showing — <see cref="RoadDef.priority"/>'s own documented purpose ("when two road classes
    /// could both occupy an edge, the higher priority wins"), not a new rule invented for this step.
    /// <para/>
    /// Runs last in the pipeline (order 700, after <see cref="GenStep_Scatterers"/>) precisely so it can
    /// clear whatever an earlier step left on its own path — rock, ore, chunks, wild plant growth, any roof
    /// over the cell — the same way <see cref="GenStep_Caves"/> clears rock it carves through. A street this
    /// step draws is guaranteed walkable no matter what hilliness or scatter the tile otherwise produced;
    /// see the module's report for why this reach-into-earlier-steps' output is judged worth it here.
    /// </summary>
    public class GenStep_Roads : GenStep
    {
        public override void Generate(MapGenContext ctx)
        {
            if (ctx.grid == null || ctx.tile.Roads.Count == 0) return;

            Map.Map map = ctx.map;
            WorldGrid grid = ctx.grid;
            var center = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);

            var links = new List<RoadLink>(ctx.tile.Roads);
            links.Sort((a, b) => a.road.priority.CompareTo(b.road.priority));

            foreach (RoadLink link in links)
            {
                TerrainDef? terrain = link.road.localTerrain;
                if (terrain == null) continue; // no honest terrain to paint (RoadDef.localTerrain's own doc)

                IntVec3 edge = MapGenGeometry.EdgePointTowards(map, grid, ctx.tileId, link.neighbor);
                CarveStreet(map, edge, center, terrain);
            }
        }

        /// <summary>Straight DDA line, <see cref="MapGenTuning.RoadWidthCells"/> wide, clearing and repainting every cell it touches.</summary>
        private static void CarveStreet(Map.Map map, IntVec3 from, IntVec3 to, TerrainDef terrain)
        {
            int steps = Math.Max(Math.Abs(to.x - from.x), Math.Abs(to.z - from.z));
            steps = Math.Max(steps, 1);
            int half = MapGenTuning.RoadWidthCells / 2;

            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                int cx = (int)Math.Round(from.x + (to.x - from.x) * t);
                int cz = (int)Math.Round(from.z + (to.z - from.z) * t);

                for (int dz = -half; dz <= half; dz++)
                {
                    int z = cz + dz;
                    if (z < 0 || z >= map.Size.z) continue;
                    for (int dx = -half; dx <= half; dx++)
                    {
                        int x = cx + dx;
                        if (x < 0 || x >= map.Size.x) continue;
                        PaintStreetCell(map, new IntVec3(x, 0, z), terrain);
                    }
                }
            }
        }

        private static void PaintStreetCell(Map.Map map, IntVec3 c, TerrainDef terrain)
        {
            var occupants = new List<Thing>(map.thingGrid.ThingsListAt(c));
            foreach (Thing thing in occupants)
            {
                thing.Destroy();
            }
            map.roofGrid.SetRoof(c, null);
            map.terrainGrid.SetTerrain(c, terrain);
        }
    }
}
