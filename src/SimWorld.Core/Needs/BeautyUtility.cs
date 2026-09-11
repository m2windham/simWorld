using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Map;
using SimWorld.Stats;
using SimWorld.Things;

namespace SimWorld.Needs
{
    /// <summary>
    /// How good the surroundings look from a given cell (RimWorld: <c>RimWorld.BeautyUtility</c>, whose
    /// <c>CellBeauty</c>/<c>AverageBeautyPerceptible</c> pair is what <c>Need_Beauty</c> and the beauty
    /// overlay both read). One cell's beauty is the sum of the <see cref="BeautyStatDefOf.Beauty"/> stat of
    /// every Thing standing in it; a citizen's beauty is the average of that over the cells they can
    /// perceive.
    /// <para/>
    /// <b>Which cells "perceptible" means, and why it is room-first.</b> RimWorld walks regions outward from
    /// the pawn with <c>RegionTraverser</c>, which stops at walls and so amounts to "the room you are in, plus
    /// what you can see out of it", bounded by a region count. This port samples the pawn's
    /// <see cref="Room"/> when they are in an enclosed one and a fixed radius otherwise. The room half is both
    /// cheaper and truer here: a room is already flood-filled and cached by <see cref="RoomTracker"/>, its
    /// cells are exactly the ones a wall does not hide, and a typical room (an 8x8 interior is 64 cells) is
    /// smaller than the radius sample it replaces. The radius half is the fallback for the two cases a room
    /// cannot answer — the open outdoors, which is one enormous <see cref="Room"/> covering most of the map,
    /// and a room larger than <see cref="MaxRoomSampleCells"/>, where averaging over a hall the citizen cannot
    /// see the far end of is both wrong and unbounded work.
    /// <para/>
    /// <b>Cost is bounded by construction</b>, which is the property that matters for a sampler every Full-tier
    /// citizen runs (see <see cref="MapEnvironmentSampler"/> for the cadence and the measured figure): never
    /// more than <c>max(MaxRoomSampleCells, cells within SampleRadius)</c> cells, no allocation per sample —
    /// the radial walk indexes <see cref="GenRadial.RadialPattern"/> directly rather than going through the
    /// <c>IEnumerable</c> helpers, which allocate an iterator per call.
    /// <para/>
    /// <b>Trim — terrain contributes nothing</b>, the same trim (and the same reason) as
    /// <c>Filth.RoomCleanlinessUtility</c>. RimWorld adds the cell's <c>TerrainDef</c> beauty, which is how a
    /// carpet makes a room prettier; this port's <see cref="TerrainDef"/> carries no <c>statBases</c> at all
    /// (<see cref="StatRequest"/>'s own doc records that the abstract request narrows to <see cref="ThingDef"/>
    /// for exactly this reason), and every terrain this port ships — soil, sand, gravel, mud, marsh, rock,
    /// roads — is one RimWorld scores at zero anyway. So nothing is faked and nothing is lost; the day a
    /// carpet ships, widen <see cref="StatRequest"/> and add one line to <see cref="CellBeauty"/>.
    /// </summary>
    public static class BeautyUtility
    {
        /// <summary>
        /// How far a citizen perceives beauty when no enclosed room bounds it. <b>SimWorld's own; RimWorld's
        /// own bound is a region count, not a radius</b>, and a region is a concept this sampler deliberately
        /// does not reach into (rooms answer the same question here and are already cached). Sized so the
        /// sample covers a small room's worth of cells — <c>GenRadial</c> puts 149 cells inside 6.9 — which
        /// keeps the outdoor sample and the room sample within the same order of cost. Pinned by tests that
        /// assert ordering (filth underfoot is uglier than filth a long way off), never as a literal.
        /// </summary>
        public const float SampleRadius = 6.9f;

        /// <summary>
        /// Above this many cells a room is sampled by radius instead of wall to wall. <b>SimWorld's own</b>,
        /// for the same reason as <see cref="SampleRadius"/>: it exists to bound the work, and it sits just
        /// above the radial sample's own cell count so neither path can be much dearer than the other.
        /// </summary>
        public const int MaxRoomSampleCells = 200;

        /// <summary>
        /// Beauty of everything standing in one cell (RimWorld: <c>BeautyUtility.CellBeauty</c>). Filth is
        /// negative, art is positive, everything else has said nothing and contributes zero — see
        /// <c>Stats_Beauty.xml</c> on why the stat's default of zero is what lets this ship without editing
        /// every existing Def.
        /// </summary>
        public static float CellBeauty(Map.Map map, IntVec3 cell)
        {
            IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(cell);
            float total = 0f;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];

                // RimWorld keeps a tempCountedThings list so a multi-cell Thing is counted once however many
                // of the sampled cells it covers. Nothing in this port's content sets ThingDef.size, so that
                // list would never fire; the same rule is enforced here by anchor cell instead, which costs
                // one comparison and no allocation. A multi-cell Thing therefore counts once, at its own
                // Position — and is missed entirely when only its other cells are in the sample, the one way
                // this differs from RimWorld's list.
                IntVec2 size = thing.def.size;
                if ((size.x != 1 || size.z != 1) && thing.Position != cell) continue;

                total += thing.GetStatValue(BeautyStatDefOf.Beauty);
            }
            return total;
        }

        /// <summary>
        /// Average beauty of the cells perceptible from <paramref name="root"/> (RimWorld:
        /// <c>BeautyUtility.AverageBeautyPerceptible</c>) — the number
        /// <c>Need_Beauty.LevelFromBeauty</c> turns into a need level. Zero for a cell off any map, which is
        /// the same answer an empty, unremarkable room gives and therefore never a surprise to a caller.
        /// See this class's remarks for which cells are sampled.
        /// </summary>
        public static float AverageBeautyPerceptible(IntVec3 root, Map.Map? map)
        {
            if (map == null || !GenGrid.InBounds(root, map)) return 0f;

            Room? room = map.roomTracker.RoomAt(root);
            if (room != null && !room.TouchesOutside && room.Cells.Count > 0 && room.Cells.Count <= MaxRoomSampleCells)
            {
                return RoomBeauty(room, map);
            }
            return RadialBeauty(root, map);
        }

        /// <summary>Average beauty over every cell of an enclosed room — the room-aware variant, and what a
        /// citizen indoors actually gets (RimWorld: <c>RoomStatWorker_Beauty</c> computes the same average as
        /// a room stat).</summary>
        public static float RoomBeauty(Room room, Map.Map map)
        {
            IReadOnlyList<IntVec3> cells = room.Cells;
            if (cells.Count == 0) return 0f;
            float total = 0f;
            for (int i = 0; i < cells.Count; i++)
            {
                total += CellBeauty(map, cells[i]);
            }
            return total / cells.Count;
        }

        /// <summary>Average beauty over the cells within <see cref="SampleRadius"/> — the fallback for the
        /// outdoors and for a room too big to be perceived whole. Cells off the map edge are skipped rather
        /// than counted as zero, so standing at the edge of the map does not read as standing in a void.</summary>
        public static float RadialBeauty(IntVec3 root, Map.Map map)
        {
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            int count = GenRadial.NumCellsInRadius(SampleRadius);
            float total = 0f;
            int sampled = 0;
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell = root + pattern[i];
                if (!GenGrid.InBounds(cell, map)) continue;
                total += CellBeauty(map, cell);
                sampled++;
            }
            return sampled == 0 ? 0f : total / sampled;
        }
    }
}
