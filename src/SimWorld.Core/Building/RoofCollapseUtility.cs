using System;

using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Whether a roofed cell is held up (RimWorld: <c>Verse.RoofCollapseUtility</c>). Which cells then fall is
    /// <see cref="RoofCollapseCellsFinder"/>'s question and what falling does is
    /// <see cref="RoofCollapserImmediate"/>'s — the same three-way split RimWorld has, where this pass found
    /// one class doing all three.
    ///
    /// <para/><b>What counts as a roof holder:</b> any spawned edifice with <c>Fillage.Full</c> — a wall, a
    /// door, a Frame mid-construction of one (its fillage is copied from what it will become, per
    /// <see cref="Frame"/>'s own remarks), unmined natural rock, or the <c>CollapsedRocks</c> a mountain
    /// leaves when it falls. This reuses <see cref="RoomTracker.IsFullEdifice"/>'s own notion of "wall-like"
    /// rather than adding a separate <c>ThingDef.holdsRoof</c> flag RimWorld carries — nothing in this port's
    /// content needs the two concepts to differ yet, and every shipped Fillage.Full def would set it.
    ///
    /// <para/><b>What the check is, and what it was.</b> RimWorld's <see cref="WithinRangeOfRoofHolder"/> is a
    /// <i>flood fill through roofed cells</i> bounded by a straight-line <see cref="RoofMaxSupportDistance"/>
    /// from the cell being asked about, which counts a holder only where it is cardinally adjacent to (or
    /// under) one of those roofed cells. This port had a plain radius query instead, at a radius of 5 that its
    /// own comment recorded as unsourced. Both halves of that were wrong and in opposite directions:
    /// <list type="bullet">
    /// <item>The distance is <b>6.9</b>, not 5 — sourced now, from decompiled 1.6 source, where it is
    /// <c>RoofCollapseUtility.RoofMaxSupportDistance</c> and feeds <c>RoofSupportRadialCellsCount</c>
    /// exactly as it does below. A 5-cell radius condemns a band of roof RimWorld holds up.</item>
    /// <item>The reach has to travel <i>through roof</i>. A straight radius lets support leak across a gap
    /// with no roof over it at all — two separate roofed areas five cells apart hold each other's roofs up,
    /// which is not a load path. The flood fill is what makes "within range" mean "along the ceiling".</item>
    /// </list>
    ///
    /// <para/><b>Why not build this on the region graph or on Room/RoomGroup</b> (the question this module's
    /// original brief asked explicitly, answered the same way and now with RimWorld agreeing): both already
    /// answer a different question. The region graph
    /// (<see cref="RegionGrid"/>/<see cref="RegionAndRoomUpdater"/>) partitions <i>reachability</i> — it stops
    /// at a portal the way a load path should not, and covers only walkable cells, while a roofed-but-solid
    /// cell still has to be answered for. Room/RoomGroup (<see cref="RoomTracker"/>) partitions <i>thermal
    /// enclosure</i> — a Room can be one contiguous thermal unit and still have a corner too far from any wall
    /// to hold its own roof up. RimWorld's own answer is a private flood fill over the roof grid, which is
    /// what this is.
    ///
    /// <para/><b>When this runs:</b> event-driven, not a per-tick scan — <see cref="Map.EdificeGrid.DeRegister"/>
    /// calls <see cref="Notify_RoofHolderDespawned"/> whenever a Fillage.Full edifice leaves the map
    /// (destroyed, mined out, deconstructed), which is the only way support is ever lost.
    /// </summary>
    public static class RoofCollapseUtility
    {
        /// <summary>
        /// How far a roofed cell may sit from the nearest support and still count as held up — RimWorld's own
        /// <c>RoofCollapseUtility.RoofMaxSupportDistance</c>, sourced from decompiled 1.6 source. Measured
        /// straight-line from the cell being asked about, while the reach itself travels along roofed cells:
        /// see <see cref="WithinRangeOfRoofHolder"/>.
        /// </summary>
        public const float RoofMaxSupportDistance = 6.9f;

        /// <summary>How many entries of <see cref="GenRadial.RadialPattern"/> fall within
        /// <see cref="RoofMaxSupportDistance"/> (RimWorld: <c>RoofSupportRadialCellsCount</c>, derived there
        /// from the same constant in the same way).</summary>
        public static readonly int RoofSupportRadialCellsCount = GenRadial.NumCellsInRadius(RoofMaxSupportDistance);

        /// <summary>
        /// An edifice that may have been holding up roof just left <paramref name="vacatedFootprint"/>
        /// (RimWorld: <c>RoofCollapseCellsFinder.Notify_RoofHolderDespawned</c>). Kept on this type, rather
        /// than moved to <see cref="RoofCollapseCellsFinder"/> where RimWorld has it, only because
        /// <c>Map/EdificeGrid.cs</c> names it and <c>Map/</c> belongs to another lane in this batch; it
        /// forwards, and the call site should move when that path is free.
        /// </summary>
        public static void Notify_RoofHolderDespawned(CellRect vacatedFootprint, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            RoofCollapseCellsFinder.ProcessRoofHolderDespawned(vacatedFootprint, map);
        }

        /// <summary>
        /// True if <paramref name="c"/> holds roof up: a spawned edifice filling the cell (RimWorld:
        /// <c>c.GetEdifice(map)?.def.holdsRoof</c>). <paramref name="ignoring"/> answers the question as if
        /// that one Thing were already gone, which is how <see cref="WouldCollapseRoofIfRemoved"/> asks about
        /// a support that is still standing without taking it down to find out.
        /// </summary>
        public static bool HoldsRoof(IntVec3 c, Map.Map map, Thing? ignoring = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!GenGrid.InBounds(c, map)) return false;
            Thing? edifice = map.edificeGrid[c];
            if (edifice == null || ReferenceEquals(edifice, ignoring)) return false;
            return edifice.def.Fillage == FillCategory.Full;
        }

        /// <summary>
        /// Would taking <paramref name="edifice"/> off the map bring a roof down somewhere?
        ///
        /// <para/><b>This is a translation, and it is recorded as one.</b> RimWorld has no such question,
        /// because in RimWorld nobody mines anything a player did not designate: <c>WorkGiver_Miner</c> scans
        /// <c>DesignationDefOf.Mine</c>, and the player choosing where to dig is looking at the overhead-
        /// mountain overlay while they choose. This port has no designation layer at all
        /// (<c>docs/WORK-REGISTER.md</c> §10 names that as a missing layer, not an oversight), so
        /// <c>AI.WorkGiver_Miner</c> substitutes "mine any reachable mineable edifice" — and with it, the
        /// judgement the player was making silently went missing along with the player. Measured on a founded
        /// settlement of twenty-five, that substitution mined <b>2,286 rock cells on its second day</b> and
        /// brought <b>724 roof cells</b> down on the people doing it. The rule restored here is the one the
        /// overlay existed to let a player follow: <i>do not dig out what is holding the ceiling up.</i>
        ///
        /// <para/>It is deliberately the narrowest form of that rule. It does not decide <i>whether</i> a
        /// settlement should be mining, or how much — that is the designation layer's question and it is still
        /// open. It only refuses the cells that kill.
        ///
        /// <para/><b>Asked twice, and the second time is the one that works.</b>
        /// <see cref="AI.WorkGiver_Miner.HasJobOnThing"/> asks it when the job is handed out, and
        /// <see cref="AI.JobDriver_Mine"/> asks it again in the toil that actually removes the rock. A
        /// settlement mines with twenty-five people at once on one seam, so the answer given at hand-out goes
        /// stale while the miner walks: a neighbour taken in the meantime is all it takes to leave this cell
        /// holding a ceiling up. Measured, three seeds, eight days: with the gate only at hand-out, twelve
        /// thousand mined cells still brought fifty-two roofs down and crushed nine of twenty-five founders.
        /// With it in both places, no roof came down at all and nobody was crushed, and the settlements still
        /// mined out 98% of their mountains — the rule is about <i>which</i> cells, not about mining less.
        ///
        /// <para/><b>Cost, measured and not hidden.</b> <c>WorkGiver_Miner.HasJobOnThing</c> asks this on every
        /// candidate it scans, and this asks <see cref="WithinRangeOfRoofHolder"/> on every roofed cell in
        /// range, so it is written to give up early in both directions: the radial walk is nearest-first and
        /// stops at the first cell that would be left unsupported, and the support check's own fast path is
        /// five array lookups with nothing allocated. It is still not free. On the one in-game day a
        /// twenty-five-strong settlement does most of its mining, a watched map's tick cost roughly doubled.
        /// The multiplier is not this check but the scan around it: <c>AI.WorkGiverScanUtility</c> walks every
        /// mineable edifice on the map — twelve thousand of them — and only lowers its "nearest so far" bound
        /// when a candidate is <i>accepted</i>, so a predicate that rejects lets more candidates through to
        /// every predicate in front of it. A nearest-first scan there, or the designation layer that would
        /// stop a settlement scanning the whole mountain in the first place, removes the cost at its source;
        /// neither belongs to this module.
        /// </summary>
        public static bool WouldCollapseRoofIfRemoved(Thing edifice)
        {
            if (edifice == null) throw new ArgumentNullException(nameof(edifice));
            Map.Map? map = edifice.Map;
            if (map == null) return false;
            if (edifice.def.Fillage != FillCategory.Full) return false;

            foreach (IntVec3 origin in edifice.OccupiedRect().Cells)
            {
                for (int i = 0; i < RoofSupportRadialCellsCount; i++)
                {
                    IntVec3 c = origin + GenRadial.RadialPattern[i];
                    if (!GenGrid.InBounds(c, map)) continue;
                    if (!map.roofGrid.Roofed(c)) continue;
                    if (!WithinRangeOfRoofHolder(c, map, edifice)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// True if a roof over <paramref name="c"/> has something holding it up within
        /// <see cref="RoofMaxSupportDistance"/>, reached along the roof (RimWorld:
        /// <c>RoofCollapseUtility.WithinRangeOfRoofHolder</c>).
        ///
        /// <para/>The fill starts at <paramref name="c"/> — which is always walked, roofed or not, exactly as
        /// RimWorld's <c>(x.Roofed(map) || x == c) &amp;&amp; x.InHorDistOf(c, 6.9f)</c> predicate does — then
        /// travels cardinally through cells that are roofed and within <see cref="RoofMaxSupportDistance"/>
        /// straight-line of <paramref name="c"/>, asking at each one whether it or one of its four cardinal
        /// neighbours, itself still inside the distance, holds roof.
        ///
        /// <para/>RimWorld's third parameter (<c>assumeNonNoRoofCellsAreRoofed</c>) is not ported: it exists
        /// to answer the question against the player's own no-roof <c>Area</c>, and this port has no player
        /// at that seam to draw one. <paramref name="ignoring"/> is this port's own, for
        /// <see cref="WouldCollapseRoofIfRemoved"/>; passing null is RimWorld's question exactly.
        ///
        /// <para/>The first step of the fill — the root cell and its four cardinal neighbours — is unrolled
        /// ahead of the queue. It is not an optimisation of the algorithm but of its <i>allocation</i>: inside
        /// rock, or under a roof with a wall next to it, the answer is those five lookups and the queue and
        /// the set are never built at all. That case is almost all of them, and it is the one a work scan pays
        /// for on every candidate.
        /// </summary>
        public static bool WithinRangeOfRoofHolder(IntVec3 c, Map.Map map, Thing? ignoring = null)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!GenGrid.InBounds(c, map)) return false;

            if (HoldsRoof(c, map, ignoring)) return true;
            for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
            {
                if (HoldsRoof(c + GenAdj.CardinalDirections[i], map, ignoring)) return true;
            }

            const float maxDistSquared = RoofMaxSupportDistance * RoofMaxSupportDistance;
            int[] stamp = VisitStamp;
            IntVec3[] queue = VisitQueue;
            int generation = NextGeneration(stamp);
            int head = 0, tail = 0;

            stamp[WindowIndex(0, 0)] = generation;
            for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
            {
                IntVec3 d = GenAdj.CardinalDirections[i];
                IntVec3 neighbour = c + d;
                int index = WindowIndex(d.x, d.z);
                if (stamp[index] == generation) continue;
                stamp[index] = generation;
                if (GenGrid.InBounds(neighbour, map) && map.roofGrid.Roofed(neighbour)) queue[tail++] = neighbour;
            }

            while (head < tail)
            {
                IntVec3 x = queue[head++];

                if (HoldsRoof(x, map, ignoring)) return true;
                for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
                {
                    IntVec3 neighbour = x + GenAdj.CardinalDirections[i];
                    int dx = neighbour.x - c.x;
                    int dz = neighbour.z - c.z;
                    if (dx * dx + dz * dz > maxDistSquared) continue;
                    if (!GenGrid.InBounds(neighbour, map)) continue;
                    if (HoldsRoof(neighbour, map, ignoring)) return true;
                    int index = WindowIndex(dx, dz);
                    if (stamp[index] == generation) continue;
                    stamp[index] = generation;
                    if (map.roofGrid.Roofed(neighbour)) queue[tail++] = neighbour;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------------------------------------
        // The fill's scratch space. Every cell it can reach lies inside a fixed window around the cell being
        // asked about (the distance bound puts |dx| and |dz| at 6 or less), so "visited" is an array indexed
        // by offset rather than a set, and the queue is an array rather than a Queue<T> — and both are reused.
        // This is not premature: WorkGiver_Miner.HasJobOnThing asks WouldCollapseRoofIfRemoved on every
        // candidate it scans, which asks this on every roofed cell in range, and the allocating version cost a
        // measured four-fold slowdown on the one in-game day a settlement does most of its mining.
        // Per-thread, like Sim.Rand's own stream, so tests running in parallel collections cannot share it.
        // ---------------------------------------------------------------------------------------------

        /// <summary>Widest offset the fill can reach, plus slack: 6.9 straight-line bounds |dx| at 6.</summary>
        private const int VisitWindowHalf = 7;

        private const int VisitWindowSize = VisitWindowHalf * 2 + 1;

        [ThreadStatic] private static int[]? visitStampBuffer;

        [ThreadStatic] private static IntVec3[]? visitQueueBuffer;

        [ThreadStatic] private static int visitGeneration;

        private static int[] VisitStamp => visitStampBuffer ??= new int[VisitWindowSize * VisitWindowSize];

        private static IntVec3[] VisitQueue => visitQueueBuffer ??= new IntVec3[VisitWindowSize * VisitWindowSize];

        /// <summary>A stamp value no earlier call used, so the "visited" array needs no clearing between
        /// calls — except once, if the counter ever wraps.</summary>
        private static int NextGeneration(int[] stamp)
        {
            if (visitGeneration == int.MaxValue)
            {
                Array.Clear(stamp, 0, stamp.Length);
                visitGeneration = 0;
            }
            return ++visitGeneration;
        }

        private static int WindowIndex(int dx, int dz) => (dx + VisitWindowHalf) * VisitWindowSize + (dz + VisitWindowHalf);
    }
}
