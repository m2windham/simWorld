using System;
using System.Collections.Generic;

using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>The stone walls <see cref="StoneWallMaterials"/> knows how to choose between, bound by
    /// defName in its own <c>[DefOf]</c> class the way every other per-module binding here is — see
    /// <see cref="ConstructionThingDefOf"/>, which this deliberately does not extend (CLAUDE.md: a new
    /// binding goes in its own class, never appended to a shared one).</summary>
    [DefOf]
    public static class StoneworkThingDefOf
    {
        public static ThingDef WallSandstone = null!;
        public static ThingDef WallLimestone = null!;
        public static ThingDef WallGranite = null!;
    }

    /// <summary>
    /// Which material a settlement raises its next wall out of (system: stonework — rock to usable material).
    ///
    /// <para/><b>The gap this closes.</b> Cut stone blocks had no consumer anywhere on a map: nothing in
    /// <c>src/</c> read a <c>Blocks*</c> Def except <c>MapGen.GenStep_Ruins</c>, which strews already-ruined
    /// walls across a freshly generated map rather than spending a settlement's own production. So a mason
    /// could cut all day and the blocks would sit in a heap forever. Walls are where they go, and the wall
    /// the settlement wanted anyway is the honest place to spend them — a stone wall the settlement builds
    /// *instead of* a wooden one, not on top of it.
    ///
    /// <para/><b>Why this is not "the settlement wants stone walls too".</b>
    /// <see cref="SettlementConstructionInitiative"/> already computes exactly one wall need
    /// (<c>WallsPerCitizen</c>, clamped). A second decider wanting its own stone walls would double the
    /// settlement's wall count for no reason anybody could point at in the fiction. So the need stays where
    /// it is and this only answers the material question for it — which is why
    /// <see cref="EquivalentsOf"/> exists: once two Defs can fill one need, the count that decides whether
    /// the need is met has to see both, or a settlement that built forty granite walls would decide it still
    /// had forty wooden ones to build.
    ///
    /// <para/><b>Why per-material Defs at all.</b> RimWorld has one <c>Wall</c> made from stuff. This port's
    /// construction pipeline costs a building through <see cref="Defs.ThingDef.costList"/> and nothing else
    /// (see <see cref="Frame.MaterialStillNeeded"/>, <see cref="WorkGiver_ConstructDeliverResources"/>), and
    /// a costList entry names one ThingDef — so stuff-chosen construction is a Building-module job of its own
    /// and Def-per-material is the faithful fallback until it lands. See <c>Buildings_Stonework.xml</c>.
    /// </summary>
    public static class StoneWallMaterials
    {
        /// <summary>
        /// Every Def that fills the settlement's wall need, toughest first. Order is read off the content
        /// (<see cref="Defs.ThingDef.BaseMaxHitPoints"/>, with defName settling a tie) rather than written
        /// down here, so adding a fourth stone to <c>Buildings_Stonework.xml</c> needs no code change and
        /// cannot disagree with the numbers it ships.
        /// </summary>
        public static IReadOnlyList<ThingDef> AllWallDefs
        {
            get
            {
                var walls = new List<ThingDef>
                {
                    StoneworkThingDefOf.WallGranite,
                    StoneworkThingDefOf.WallLimestone,
                    StoneworkThingDefOf.WallSandstone,
                    ConstructionThingDefOf.Wall,
                };
                walls.Sort(ByToughnessThenName);
                return walls;
            }
        }

        /// <summary>Whether <paramref name="def"/> is one of the Defs that fill the wall need.</summary>
        public static bool IsWall(ThingDef def)
        {
            if (def == null) return false;
            IReadOnlyList<ThingDef> walls = AllWallDefs;
            for (int i = 0; i < walls.Count; i++)
            {
                if (ReferenceEquals(walls[i], def)) return true;
            }
            return false;
        }

        /// <summary>The Defs that count toward the same need as <paramref name="def"/> — every wall for a
        /// wall, and just itself for everything else. The one hook <see cref="SettlementConstructionInitiative"/>
        /// needs to stop counting a granite wall as a missing wooden one.</summary>
        public static IReadOnlyList<ThingDef> EquivalentsOf(ThingDef def) =>
            IsWall(def) ? AllWallDefs : new[] { def };

        /// <summary>
        /// The wall the settlement should raise next on <paramref name="map"/>: the toughest stone it has a
        /// whole wall's worth of blocks for and has researched, or the wooden
        /// <see cref="ConstructionThingDefOf.Wall"/> when it has none. "A whole wall's worth" is the Def's own
        /// <see cref="Defs.ThingDef.costList"/>, not a number written here — a blueprint the settlement cannot
        /// finish would sit on a cell forever counting toward a need nobody is filling.
        /// <para/>
        /// It follows that a settlement flips back to wood the moment its stone runs out, and back to stone
        /// the next time a mason cuts a pile. That is the intended behaviour and it costs nothing, because
        /// <see cref="EquivalentsOf"/> makes both kinds count toward the one need.
        /// </summary>
        public static ThingDef PreferredWallDef(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            IReadOnlyList<ThingDef> walls = AllWallDefs;
            for (int i = 0; i < walls.Count; i++)
            {
                ThingDef wall = walls[i];
                if (ReferenceEquals(wall, ConstructionThingDefOf.Wall)) continue;
                if (!wall.IsResearchFinished) continue;
                if (CanAffordOne(map, wall)) return wall;
            }
            return ConstructionThingDefOf.Wall;
        }

        /// <summary>Whether every entry of <paramref name="wall"/>'s cost is on the map in full. Counts loose
        /// stacks wherever they lie, exactly as <see cref="WorkGiver_ConstructDeliverResources"/> does when it
        /// goes looking for them.</summary>
        private static bool CanAffordOne(Map.Map map, ThingDef wall)
        {
            List<ThingDefCountClass>? cost = wall.costList;
            if (cost == null || cost.Count == 0) return false;

            for (int i = 0; i < cost.Count; i++)
            {
                int have = 0;
                IReadOnlyList<Thing> stacks = map.listerThings.ThingsOfDef(cost[i].thingDef);
                for (int s = 0; s < stacks.Count; s++)
                {
                    if (stacks[s].Spawned) have += stacks[s].stackCount;
                }
                if (have < cost[i].count) return false;
            }
            return true;
        }

        /// <summary>
        /// The wall Def <paramref name="blockDef"/> is the cost of, or null when nothing is built out of it.
        /// The bridge between "a mason cut this" and "the settlement spends it on that", read off the
        /// costLists rather than written down twice.
        /// </summary>
        public static ThingDef? WallBuiltFrom(ThingDef blockDef)
        {
            if (blockDef == null) return null;
            IReadOnlyList<ThingDef> walls = AllWallDefs;
            for (int i = 0; i < walls.Count; i++)
            {
                List<ThingDefCountClass>? cost = walls[i].costList;
                if (cost == null) continue;
                for (int c = 0; c < cost.Count; c++)
                {
                    if (ReferenceEquals(cost[c].thingDef, blockDef)) return walls[i];
                }
            }
            return null;
        }

        /// <summary>
        /// How many of <paramref name="blockDef"/> the settlement wants cut and waiting: enough for every wall
        /// it could ever decide it needs (<see cref="ConstructionInitiativeTuning.MaxWallShelterCount"/>) at
        /// that wall's own costList price. Derived rather than chosen — a standing bill's target is a
        /// statement about demand, and the demand is already written down one module over. Zero when nothing
        /// is built out of this material at all, which is the honest answer to "how many should we keep".
        /// </summary>
        public static int BlocksWantedOf(ThingDef blockDef)
        {
            ThingDef? wall = WallBuiltFrom(blockDef);
            if (wall?.costList == null) return 0;

            int perWall = 0;
            for (int i = 0; i < wall.costList.Count; i++)
            {
                if (ReferenceEquals(wall.costList[i].thingDef, blockDef)) perWall += wall.costList[i].count;
            }
            return perWall * ConstructionInitiativeTuning.MaxWallShelterCount;
        }

        private static int ByToughnessThenName(ThingDef a, ThingDef b)
        {
            int byHitPoints = b.BaseMaxHitPoints.CompareTo(a.BaseMaxHitPoints);
            return byHitPoints != 0 ? byHitPoints : string.CompareOrdinal(a.defName, b.defName);
        }
    }
}
