using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.MapGen
{
    /// <summary>
    /// Scatters weathered ancient structures across the map (RimWorld: <c>GenStep_ScatterRuinsSimple</c> /
    /// <c>GenStep_ScatterShrines</c> and the <c>RuinsGenerator</c>/<c>SymbolResolver_Ruins</c> family behind
    /// them). A handful of rectangles of broken, gap-riddled wall, some roofed, some carrying a bit of loot,
    /// their standing wall segments weathered down from full hit points rather than pristine — placed at
    /// random, clear of rock, water, the map border and each other, deterministic from the map's own seed.
    ///
    /// <para/><b>What this deliberately does not port</b> (per this repository's brief: "RimWorld's full
    /// RuleDef/SymbolResolver grammar is a large subsystem, and reproducing all of it is not what this item
    /// asks for"):
    /// <list type="bullet">
    /// <item>RimWorld builds a ruin from a data-driven grammar of <c>RuleDef</c>s and <c>SymbolResolver</c>s
    /// (edge walls, corner pillars, room subdivision, door placement, per-symbol rubble/roof/loot resolvers
    /// chained through <c>BaseGen</c>'s symbol stack). This class is the direct, hand-written equivalent of
    /// exactly one such resolver chain (walls-with-gaps, rubble, a roof chance, a loot chance) rather than a
    /// reusable grammar another feature could compose differently — there is only one "shape" of ruin here,
    /// not a library of them.</item>
    /// <item><c>GenStep_ScatterShrines</c> — a single guarded ancient altar/statue with a unique, higher-value
    /// reward and (in RimWorld) an associated quest/mechanoid-guard incident — is not ported at all. It is a
    /// distinct feature front-ended by content this pass has no home for yet (a unique-reward table, a guard
    /// spawn, a questgiver hook), not a smaller version of the same scatter this class already does.</item>
    /// <item>No corpses. RimWorld's ruins can carry a dead pawn among the rubble; this port has no
    /// <c>Corpse</c> Thing/system yet (Health/Pawns own that, and are off limits to this pass — see
    /// CLAUDE.md's lane boundaries). Inventing a flavor-only "corpse" marker the way
    /// <see cref="GenStep_Scatterers"/> already does for wild plant growth was considered and rejected: unlike
    /// a plant, a corpse carries real identity and simulation weight (whose body, how they died, the thoughts
    /// discovering one produces) that a placeholder marker would misrepresent rather than simplify. Loot is
    /// ported; a body on the floor next to it is not — see the <c>mapgen.ruins</c> label in
    /// <c>docs/status.json</c> for where that lands.</item>
    /// <item>No interior subdivision, no doors, no varied room shapes — one rectangle, one wall ring, one
    /// open interior. RimWorld's ruins can be larger, multi-room structures; this pass's are always the one
    /// size class <see cref="MapGenTuning.RuinSizeRange"/> describes.</item>
    /// <item>Wall material is chosen from a content-driven pool (<see cref="MapGenThingDefOf.BlocksSandstone"/>,
    /// <see cref="MapGenThingDefOf.Steel"/>, <see cref="MapGenThingDefOf.WoodLog"/> — real stuff-capable
    /// Defs, never invented in C#) and passed through <see cref="ThingMaker.MakeThing"/>'s <c>stuff</c>
    /// parameter the way every future stuff-aware caller will once a Stuff system exists — see that method's
    /// own remark. Today that parameter is accepted but not applied to anything (<c>Thing</c> carries no
    /// Stuff field yet), so the choice has no visible effect on the spawned wall; wiring that up belongs to
    /// whichever module actually builds the Stuff system, not this one.</item>
    /// </list>
    ///
    /// <para/><b>Never blocking the map</b> (the brief's own requirement): every ring gets at least
    /// <see cref="MapGenTuning.RuinMinGuaranteedGaps"/> forced-open, non-corner gaps regardless of how the
    /// per-cell dice land, and the cell immediately outside each forced gap is actively cleared of rock —
    /// a structural guarantee by construction, not a probability. A runtime repair loop that rebuilds the
    /// region graph and re-opens a wall if a ruin somehow still turned out to enclose a pocket was considered
    /// and rejected in favor of this simpler, purely local guarantee: a rectangle whose own ring always keeps
    /// two forced-open, pre-cleared exits can not enclose anything by itself, no matter what an earlier step
    /// left around it, so there is nothing left for a global repair pass to catch. What the class does not do
    /// itself, a test does: <c>MapGenTests</c> rebuilds the real region graph after generating a heavily-ruined
    /// map and asserts every walkable cell is reachable from the border through it — using the same
    /// <see cref="RegionGrid"/>/<see cref="RegionTraverser"/> graph <see cref="AI.Reachability"/> queries,
    /// proving the guarantee against the actual reachability graph rather than only against this class's own
    /// bookkeeping.
    ///
    /// <para/>Runs after <see cref="GenStep_Scatterers"/> (order 600) so a ruin's footprint is cleared of
    /// whatever chunk or wild-plant scatter randomly landed there rather than co-existing oddly with it, and
    /// before <see cref="GenStep_Roads"/> (order 700) so a road still clears straight through a ruin exactly
    /// as it already clears through rock and scatter — <see cref="GenStep_Roads"/>'s own doc already explains
    /// why that reach-into-earlier-steps' output is worth it.
    /// </summary>
    public class GenStep_Ruins : GenStep
    {
        /// <summary>Stuff-capable resource Defs a ruin wall may be built from — one of each real "stuff" category (Stony/Metallic/Woody) already in content.</summary>
        private static readonly ThingDef[] WallStuffs =
        {
            MapGenThingDefOf.BlocksSandstone,
            MapGenThingDefOf.Steel,
            MapGenThingDefOf.WoodLog,
        };

        /// <summary>Loot a ruin may scatter inside: stackable raw resources plus a couple of simple hand weapons — all pre-existing content, none invented for this class.</summary>
        private static readonly ThingDef[] LootPool =
        {
            MapGenThingDefOf.Silver,
            MapGenThingDefOf.Steel,
            MapGenThingDefOf.WoodLog,
            MapGenThingDefOf.MeleeWeapon_Knife,
            MapGenThingDefOf.MeleeWeapon_Club,
            MapGenThingDefOf.Bow_Short,
        };

        public override void Generate(MapGenContext ctx)
        {
            RandomStream rand = SeededStream(ctx);
            Map.Map map = ctx.map;

            int attempts = (int)Math.Round(map.cellIndices.NumGridCells / MapGenTuning.RuinCellsPerAttempt);
            if (attempts <= 0) return;

            var placed = new List<CellRect>();
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                CellRect rect = PickRuinRect(map, rand);
                if (rect.IsEmpty) continue;
                if (!CanPlaceRuin(map, ctx, rect, placed)) continue;

                ThingDef wallStuff = rand.Element(WallStuffs);
                BuildRuin(ctx, rand, rect, wallStuff);
                placed.Add(rect);
            }
        }

        private static CellRect PickRuinRect(Map.Map map, RandomStream rand)
        {
            int width = rand.Range(MapGenTuning.RuinSizeRange);
            int height = rand.Range(MapGenTuning.RuinSizeRange);
            int margin = MapGenTuning.RuinEdgeMargin;

            int minXHigh = map.Size.x - width - margin;
            int minZHigh = map.Size.z - height - margin;
            if (minXHigh <= margin || minZHigh <= margin) return CellRect.Empty;

            int minX = rand.Range(margin, minXHigh + 1);
            int minZ = rand.Range(margin, minZHigh + 1);
            return new CellRect(minX, minZ, width, height);
        }

        /// <summary>Clear of rock and water for the rect plus its placement margin, inside the map, and not crowding an already-placed ruin.</summary>
        private static bool CanPlaceRuin(Map.Map map, MapGenContext ctx, CellRect rect, List<CellRect> placed)
        {
            CellRect checkArea = rect.ExpandedBy(MapGenTuning.RuinCellsMarginBeforePlacement);
            if (checkArea.minX < 0 || checkArea.minZ < 0 || checkArea.maxX >= map.Size.x || checkArea.maxZ >= map.Size.z)
            {
                return false;
            }
            foreach (IntVec3 c in checkArea.Cells)
            {
                int i = map.cellIndices.CellToIndex(c);
                if (ctx.rock[i]) return false;
                if (map.terrainGrid.TerrainAt(c).IsWater) return false;
            }
            for (int i = 0; i < placed.Count; i++)
            {
                if (Overlaps(checkArea, placed[i])) return false;
            }
            return true;
        }

        private static bool Overlaps(CellRect a, CellRect b) =>
            a.minX <= b.maxX && a.maxX >= b.minX && a.minZ <= b.maxZ && a.maxZ >= b.minZ;

        private static void BuildRuin(MapGenContext ctx, RandomStream rand, CellRect rect, ThingDef wallStuff)
        {
            Map.Map map = ctx.map;
            ClearFootprint(ctx, rect);

            var ring = new List<IntVec3>(rect.EdgeCells);
            HashSet<int> forcedGaps = ChooseForcedGapIndices(ring, rect, rand, MapGenTuning.RuinMinGuaranteedGaps);

            for (int i = 0; i < ring.Count; i++)
            {
                IntVec3 c = ring[i];
                bool corner = IsCorner(rect, c);
                bool forcedGap = forcedGaps.Contains(i);
                bool gap = forcedGap || rand.Chance(corner ? MapGenTuning.RuinCornerGapChance : MapGenTuning.RuinWallGapChance);

                if (gap)
                {
                    if (forcedGap) OpenExterior(ctx, OutwardNeighbor(rect, c));
                    if (rand.Chance(MapGenTuning.RuinRubbleChance))
                    {
                        SpawnRubble(map, rand, c);
                    }
                    continue;
                }

                SpawnWeatheredWall(map, rand, c, wallStuff);
            }

            CellRect interior = rect.ContractedBy(1);
            if (interior.IsEmpty) return;

            if (rand.Chance(MapGenTuning.RuinRoofedChance))
            {
                foreach (IntVec3 c in interior.Cells)
                {
                    map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                }
            }
            if (rand.Chance(MapGenTuning.RuinLootChance))
            {
                ScatterLoot(map, rand, interior);
            }
        }

        /// <summary>Destroys every Thing in the rect (rock included) and clears any roof — a clean slate for the walls, gaps and interior this method is about to place.</summary>
        private static void ClearFootprint(MapGenContext ctx, CellRect rect)
        {
            Map.Map map = ctx.map;
            foreach (IntVec3 c in rect.Cells)
            {
                DestroyEverythingAt(ctx, c);
                map.roofGrid.SetRoof(c, null);
            }
        }

        private static void DestroyEverythingAt(MapGenContext ctx, IntVec3 c)
        {
            Map.Map map = ctx.map;
            var occupants = new List<Thing>(map.thingGrid.ThingsListAt(c));
            foreach (Thing t in occupants)
            {
                t.Destroy();
            }
            int i = map.cellIndices.CellToIndex(c);
            if (ctx.rock[i]) ctx.rock[i] = false;
        }

        /// <summary>Clears the single cell just outside a forced-open gap, so that gap actually leads somewhere walkable rather than into whatever rock happened to be there.</summary>
        private static void OpenExterior(MapGenContext ctx, IntVec3 outside)
        {
            if (!GenGrid.InBounds(outside, ctx.map)) return;
            DestroyEverythingAt(ctx, outside);
        }

        private static IntVec3 OutwardNeighbor(CellRect rect, IntVec3 ringCell)
        {
            if (ringCell.x == rect.minX) return ringCell + new IntVec3(-1, 0, 0);
            if (ringCell.x == rect.maxX) return ringCell + new IntVec3(1, 0, 0);
            if (ringCell.z == rect.minZ) return ringCell + new IntVec3(0, 0, -1);
            return ringCell + new IntVec3(0, 0, 1); // ringCell.z == rect.maxZ
        }

        private static bool IsCorner(CellRect rect, IntVec3 c) =>
            (c.x == rect.minX || c.x == rect.maxX) && (c.z == rect.minZ || c.z == rect.maxZ);

        /// <summary>Picks <paramref name="count"/> distinct, non-corner ring indices via the same partial Fisher-Yates shuffle <see cref="GenStep_RocksAndMountains"/> already uses for its ore-vein pick.</summary>
        private static HashSet<int> ChooseForcedGapIndices(List<IntVec3> ring, CellRect rect, RandomStream rand, int count)
        {
            var candidates = new List<int>();
            for (int i = 0; i < ring.Count; i++)
            {
                if (!IsCorner(rect, ring[i])) candidates.Add(i);
            }
            count = Math.Min(count, candidates.Count);
            for (int k = 0; k < count; k++)
            {
                int pick = rand.Range(k, candidates.Count);
                (candidates[k], candidates[pick]) = (candidates[pick], candidates[k]);
            }
            var result = new HashSet<int>();
            for (int k = 0; k < count; k++) result.Add(candidates[k]);
            return result;
        }

        private static void SpawnWeatheredWall(Map.Map map, RandomStream rand, IntVec3 c, ThingDef wallStuff)
        {
            Thing wall = ThingMaker.MakeThing(MapGenThingDefOf.Wall, wallStuff);
            int weathered = (int)Math.Round(wall.MaxHitPoints * rand.Range(MapGenTuning.RuinWallHitPointsFractionMin, MapGenTuning.RuinWallHitPointsFractionMax));
            wall.HitPoints = Math.Max(1, weathered);
            GenSpawn.Spawn(wall, c, map);
        }

        private static void SpawnRubble(Map.Map map, RandomStream rand, IntVec3 c)
        {
            ThingDef chunk = rand.Element(MapGenThingDefOf.ChunkSandstone, MapGenThingDefOf.ChunkGranite, MapGenThingDefOf.ChunkLimestone);
            Thing rubble = ThingMaker.MakeThing(chunk);
            GenSpawn.Spawn(rubble, c, map);
        }

        private static void ScatterLoot(Map.Map map, RandomStream rand, CellRect interior)
        {
            var cells = new List<IntVec3>(interior.Cells);
            int count = Math.Min(rand.Range(MapGenTuning.RuinLootItemCountRange), cells.Count);

            for (int k = 0; k < count; k++)
            {
                int pick = rand.Range(k, cells.Count);
                (cells[k], cells[pick]) = (cells[pick], cells[k]);
                IntVec3 c = cells[k];
                if (!GenGrid.Walkable(c, map)) continue; // defensive: interior was already cleared, but never spawn loot inside a wall

                ThingDef lootDef = rand.Element(LootPool);
                Thing loot = ThingMaker.MakeThing(lootDef);
                if (lootDef.stackLimit > 1)
                {
                    loot.stackCount = Math.Min(lootDef.stackLimit, rand.Range(MapGenTuning.RuinLootStackRange));
                }
                GenSpawn.Spawn(loot, c, map);
            }
        }
    }
}
