using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.MapGen
{
    /// <summary>
    /// Carves winding passages through the rock <see cref="GenStep_RocksAndMountains"/> placed, via a
    /// directional random walk: each cave picks a heading and mostly keeps it, occasionally turning, for a
    /// random length — RimWorld's own cave generator (<c>Verse.GenStep_Caves</c>) is a directional random
    /// walk rather than a cellular-automata cavern, and this is a faithful-in-shape translation of that (see
    /// the report for what could and could not be confirmed against source). Cleared cells stop being rock
    /// (<see cref="MapGenContext.rock"/>), so <see cref="GenStep_Roofs"/> reads them as passages, not solid
    /// mountain, and <see cref="GenStep_Scatterers"/> can place chunks/plants in whatever daylight reaches them.
    /// </summary>
    public class GenStep_Caves : GenStep
    {
        public override void Generate(MapGenContext ctx)
        {
            RandomStream rand = SeededStream(ctx);
            Map.Map map = ctx.map;
            bool[] rock = ctx.rock;

            int rockCount = CountRock(rock);
            if (rockCount == 0) return;

            float rockFraction = (float)rockCount / rock.Length;
            int caveCount = (int)System.Math.Round(rockFraction * MapGenTuning.CavesPerFullMountain);
            for (int i = 0; i < caveCount; i++)
            {
                CarveOneCave(map, rock, rand);
            }
        }

        private static int CountRock(bool[] rock)
        {
            int count = 0;
            for (int i = 0; i < rock.Length; i++)
            {
                if (rock[i]) count++;
            }
            return count;
        }

        private static void CarveOneCave(Map.Map map, bool[] rock, RandomStream rand)
        {
            IntVec3? start = PickStartCell(map, rock, rand);
            if (start == null) return;

            IntVec3 pos = start.Value;
            int dirIndex = rand.Range(0, GenAdj.CardinalDirections.Length);
            int length = rand.Range(MapGenTuning.CaveMinLength, MapGenTuning.CaveMaxLength + 1);
            int width = rand.Chance(MapGenTuning.CaveWideChance) ? 2 : 1;

            for (int step = 0; step < length; step++)
            {
                if (!GenGrid.InBounds(pos, map)) break;
                CarveAt(map, rock, pos, width);
                if (!rand.Chance(MapGenTuning.CaveStraightChance))
                {
                    dirIndex = rand.Range(0, GenAdj.CardinalDirections.Length);
                }
                pos += GenAdj.CardinalDirections[dirIndex];
            }
        }

        /// <summary>Prefers a rock cell on the map border (a visible mountain edge to carve inward from); falls back to any rock cell for a mountain that never reaches the border.</summary>
        private static IntVec3? PickStartCell(Map.Map map, bool[] rock, RandomStream rand)
        {
            int sizeX = map.Size.x, sizeZ = map.Size.z;
            var border = new List<IntVec3>();
            for (int x = 0; x < sizeX; x++)
            {
                AddIfRock(map, rock, border, x, 0);
                AddIfRock(map, rock, border, x, sizeZ - 1);
            }
            for (int z = 0; z < sizeZ; z++)
            {
                AddIfRock(map, rock, border, 0, z);
                AddIfRock(map, rock, border, sizeX - 1, z);
            }
            if (border.Count > 0) return rand.Element(border);

            for (int z = 0; z < sizeZ; z++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    if (rock[map.cellIndices.CellToIndex(x, z)]) return new IntVec3(x, 0, z);
                }
            }
            return null;
        }

        private static void AddIfRock(Map.Map map, bool[] rock, List<IntVec3> list, int x, int z)
        {
            if (rock[map.cellIndices.CellToIndex(x, z)]) list.Add(new IntVec3(x, 0, z));
        }

        private static void CarveAt(Map.Map map, bool[] rock, IntVec3 pos, int width)
        {
            ClearCellIfRock(map, rock, pos);
            if (width > 1)
            {
                ClearCellIfRock(map, rock, pos + new IntVec3(1, 0, 0));
                ClearCellIfRock(map, rock, pos + new IntVec3(0, 0, 1));
            }
        }

        private static void ClearCellIfRock(Map.Map map, bool[] rock, IntVec3 c)
        {
            if (!GenGrid.InBounds(c, map)) return;
            int i = map.cellIndices.CellToIndex(c);
            if (!rock[i]) return;
            Thing? edifice = map.edificeGrid[c];
            edifice?.Destroy();
            rock[i] = false;
        }
    }
}
