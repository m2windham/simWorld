using SimWorld.Map;

namespace SimWorld.MapGen
{
    /// <summary>
    /// Paints the <see cref="RoofGrid"/> over mountain and cave cells (spec §5's map-gen line). Solid rock
    /// always carries a thick natural roof; a non-rock cell gets a roof graded by how enclosed by rock its
    /// immediate surroundings are — deep inside a mountain (mostly rock nearby) reads thick, a cave passage or
    /// a mountain's edge (some rock nearby) reads thin, and open ground (no rock nearby) gets none. This local-
    /// density read is deliberately not a flood fill from the map border: a tunnel <see cref="GenStep_Caves"/>
    /// carves out to the map edge should stay roofed along its whole length, the way real mined tunnels do in
    /// RimWorld, and a border-seeded flood fill would instead un-roof it the moment it touched the edge.
    /// </summary>
    public class GenStep_Roofs : GenStep
    {
        public override void Generate(MapGenContext ctx)
        {
            Map.Map map = ctx.map;
            bool[] rock = ctx.rock;
            int sizeX = map.Size.x, sizeZ = map.Size.z;

            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    int i = map.cellIndices.CellToIndex(x, z);
                    var c = new IntVec3(x, 0, z);

                    if (rock[i])
                    {
                        map.roofGrid.SetRoof(c, RoofDefOf.RoofRockThick);
                        continue;
                    }

                    float localRockFraction = LocalRockFraction(map, rock, x, z);
                    if (localRockFraction >= MapGenTuning.ThickRoofRockFraction)
                    {
                        map.roofGrid.SetRoof(c, RoofDefOf.RoofRockThick);
                    }
                    else if (localRockFraction >= MapGenTuning.ThinRoofRockFraction)
                    {
                        map.roofGrid.SetRoof(c, RoofDefOf.RoofRockThin);
                    }
                    // else: open ground; the grid already defaults every cell to no roof.
                }
            }
        }

        /// <summary>Fraction of cells within <see cref="MapGenTuning.RoofSampleRadius"/> (Chebyshev, excluding self) that are rock.</summary>
        private static float LocalRockFraction(Map.Map map, bool[] rock, int x, int z)
        {
            int radius = MapGenTuning.RoofSampleRadius;
            int sizeX = map.Size.x, sizeZ = map.Size.z;
            int rockCount = 0, total = 0;

            for (int dz = -radius; dz <= radius; dz++)
            {
                int nz = z + dz;
                if (nz < 0 || nz >= sizeZ) continue;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = x + dx;
                    if (nx < 0 || nx >= sizeX) continue;
                    total++;
                    if (rock[map.cellIndices.CellToIndex(nx, nz)]) rockCount++;
                }
            }
            return total > 0 ? (float)rockCount / total : 0f;
        }
    }
}
