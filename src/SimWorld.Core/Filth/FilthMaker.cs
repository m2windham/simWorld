using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.Filth
{
    /// <summary>
    /// The one way filth ever appears on a map (RimWorld: <c>Verse.FilthMaker</c>). Every source — a pawn
    /// walking over soil, a butcher's block, a bleeding wound — goes through
    /// <see cref="TryMakeFilth(IntVec3, Map.Map, ThingDef, string?, int)"/> rather than spawning a
    /// <see cref="Filth"/> itself, because the interesting behaviour is what happens when the cell already
    /// has some: the existing pile thickens (to its def's cap) instead of a second Thing joining it. A caller
    /// that spawned filth directly would silently reintroduce unbounded per-cell stacking, which is the exact
    /// thing <see cref="Filth.thickness"/> exists to prevent.
    /// </summary>
    public static class FilthMaker
    {
        /// <summary>
        /// Puts <paramref name="count"/> units of <paramref name="filthDef"/> on <paramref name="c"/>,
        /// thickening what is already there when it can. Returns true if the cell ended up dirtier than it
        /// started. Silently does nothing for a cell that cannot hold filth (see <see cref="CanMakeFilthAt"/>),
        /// which is the normal case at a wall or off the map edge, not an error.
        /// </summary>
        /// <param name="source">Who or what produced it, remembered on the pile (<see cref="Filth.AddSource"/>).</param>
        public static bool TryMakeFilth(IntVec3 c, Map.Map map, ThingDef filthDef, string? source = null, int count = 1)
        {
            if (map == null || filthDef == null) return false;
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                if (TryMakeFilthSingle(c, map, filthDef, source)) any = true;
            }
            return any;
        }

        private static bool TryMakeFilthSingle(IntVec3 c, Map.Map map, ThingDef filthDef, string? source)
        {
            if (!CanMakeFilthAt(c, map)) return false;

            IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < things.Count; i++)
            {
                if (!(things[i] is Filth existing) || existing.def != filthDef) continue;

                // Already at its cap: RimWorld reports the attempt as having failed rather than pretending a
                // layer landed, and so does this. The source is still recorded — something really did dirty
                // this cell, the cell just cannot get any dirtier.
                existing.AddSource(source);
                if (!existing.CanBeThickened)
                {
                    existing.ThickenFilth();
                    return false;
                }
                existing.ThickenFilth();
                return true;
            }

            var filth = (Filth)ThingMaker.MakeThing(filthDef);
            filth.AddSource(source);
            GenSpawn.Spawn(filth, c, map);
            return true;
        }

        /// <summary>
        /// Whether a cell can hold filth at all (RimWorld: <c>FilthMaker.ShouldMakeFilth</c>).
        /// <para/>
        /// <b>Translation.</b> RimWorld gates this on <c>TerrainDef.acceptFilth</c>, a per-terrain flag whose
        /// shipped value is false for exactly one thing: water, where filth washes away instead of settling.
        /// This port has no <c>acceptFilth</c> field, but it does already have
        /// <see cref="TerrainDef.water"/>/<see cref="TerrainDef.IsWater"/> on every terrain def — the same
        /// distinction under a different name — so that is read instead of adding a second flag that would
        /// mean editing <c>Map/TerrainDef.cs</c> and every shipped terrain element. The other half is
        /// RimWorld's own: a cell filled by an impassable edifice is inside a wall and holds nothing.
        /// </summary>
        public static bool CanMakeFilthAt(IntVec3 c, Map.Map map)
        {
            if (map == null || !GenGrid.InBounds(c, map)) return false;

            TerrainDef terrain = map.terrainGrid.TerrainAt(c);
            if (terrain.IsWater || terrain.passability == Traversability.Impassable) return false;

            Thing? edifice = map.edificeGrid[c];
            return edifice == null || edifice.def.passability != Traversability.Impassable;
        }

        /// <summary>
        /// The filth def <paramref name="terrain"/> generates underfoot, or null for terrain that stays clean
        /// (RimWorld: <c>TerrainDef.generatedFilth</c>, read from the filth end — see
        /// <see cref="FilthProperties.sourceTerrains"/> for why). Walks the loaded filth defs; there are a
        /// handful of them, and the result is cached per terrain because a pawn asks this on every cell it
        /// steps into.
        /// </summary>
        public static ThingDef? FilthFromTerrain(TerrainDef? terrain)
        {
            if (terrain == null) return null;
            if (cachedTerrainFilth.TryGetValue(terrain, out ThingDef? cached)) return cached;

            ThingDef? found = null;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                List<TerrainDef>? terrains = def.filth?.sourceTerrains;
                if (terrains == null) continue;
                for (int i = 0; i < terrains.Count; i++)
                {
                    if (terrains[i] != terrain) continue;
                    found = def;
                    break;
                }
                if (found != null) break;
            }

            cachedTerrainFilth[terrain] = found;
            return found;
        }

        private static readonly Dictionary<TerrainDef, ThingDef?> cachedTerrainFilth = new Dictionary<TerrainDef, ThingDef?>();

        /// <summary>Drops the terrain lookup cache. Tests that swap <see cref="DefDatabase.Global"/> between
        /// content loads call this; nothing in a running game needs to, since defs never change mid-game.</summary>
        public static void ResetCache() => cachedTerrainFilth.Clear();

        /// <summary>Every <see cref="Filth"/> currently on <paramref name="map"/>. Reads the map's own
        /// <see cref="ThingRequestGroup.Filth"/> index, which <see cref="ListerThings"/> has maintained since
        /// before this module existed — RimWorld keeps a second, home-area-filtered list
        /// (<c>Map.listerFilthInHomeArea</c>) purely to make the cleaning scan cheaper; this port filters the
        /// one list at the point of use instead of adding a field to <c>Map.Map</c> (see
        /// <see cref="WorkGiver_CleanFilth"/>).</summary>
        public static IEnumerable<Filth> AllFilthOn(Map.Map map)
        {
            if (map == null) yield break;
            IReadOnlyList<Thing> all = map.listerThings.ThingsInGroup(ThingRequestGroup.Filth);
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is Filth f) yield return f;
            }
        }
    }
}
