using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Filth
{
    /// <summary>
    /// Finds a piece of filth inside the region colonists actually clean and sends someone to scrub it
    /// (RimWorld: <c>RimWorld.WorkGiver_CleanFilth</c>). This is the worker behind the <c>CleanFilth</c>
    /// <see cref="WorkGiverDef"/>, which shipped in <c>WorkGivers.xml</c> pointing at
    /// <see cref="WorkGiver_Pending"/> — a whole work type (<c>Cleaning</c>) with nothing it could ever do,
    /// because no <see cref="Filth"/> class existed to target.
    /// <para/>
    /// <b>What bounds it</b> is <see cref="CleaningBounds"/>; that class carries the argument and the
    /// translation, because it is the decision that keeps this job from running forever.
    /// <para/>
    /// <b>Trim — no <c>ListerFilthInHomeArea</c>.</b> RimWorld keeps a second, pre-filtered list of the filth
    /// inside the home area so this scan does not walk every pile on the map. This port filters
    /// <see cref="FilthMaker.AllFilthOn"/> — the map's existing <c>ThingRequestGroup.Filth</c> index — at the
    /// point of use instead. That keeps a field off <c>Map.Map</c> (which another lane is in this batch), and
    /// the cost is the same order as the other scanning givers already shipped here, all of which walk a
    /// whole <c>listerThings</c> group per scan. If filth counts ever make that hurt, the cached lister is
    /// the fix and it is a drop-in.
    /// <para/>
    /// <b>A patch per job, as in RimWorld.</b> <see cref="JobOnThing"/> packs up to
    /// <see cref="MaxPilesPerJob"/> nearby piles from the same room into the job, so the pawn cleans a whole
    /// patch without re-deciding. This port used to issue one job per pile and let
    /// <see cref="WorkGiverScanUtility"/> find the next nearest one afterwards — the same patch by much the same
    /// route, but a full work scan after every pile, about a tenth of a Bronze-age town's tick.
    /// </summary>
    public sealed class WorkGiver_CleanFilth : WorkGiver_Scanner
    {
        /// <summary>
        /// How long a pile must sit untouched before anyone will clean it (RimWorld:
        /// <c>WorkGiver_CleanFilth.MinTicksSinceThickened</c>, 600). It stops colonists chasing filth that is
        /// still actively being made — the doorway somebody is walking through right now — which would have
        /// them scrub the same cell forever. RimWorld's own literal, recalled rather than sourced; the test
        /// pins the behaviour (freshly made filth is not immediately work, and it becomes work later) instead
        /// of this number.
        /// </summary>
        public const int MinTicksSinceThickened = 600;

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            Map.Map? map = pawn.Map;
            if (map == null) return true;
            return map.listerThings.ThingsInGroup(Map.ThingRequestGroup.Filth).Count == 0;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            // Only what IsCleanFilthWork keeps (RimWorld walks listerFilthInHomeArea, not all filth): the
            // side-effect-free tests HasJobOnThing starts with, so the survivors keep their order and the scan
            // takes the same pile.
            foreach (Filth f in FilthMaker.AllFilthOn(map))
            {
                if (IsCleanFilthWork(f)) yield return f;
            }
        }

        /// <summary>It is filth, it is somewhere we clean, and it has settled.</summary>
        private static bool IsCleanFilthWork(Filth filth) =>
            filth.Spawned && !filth.Destroyed && CleaningBounds.IsCleanable(filth)
            && filth.TicksSinceThickened >= MinTicksSinceThickened;

        /// <summary>The whole eligibility test, in RimWorld's own order: it is filth, it is somewhere we
        /// clean, it has settled, we can get to it, and nobody else has claimed it.</summary>
        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is Filth filth) || !IsCleanFilthWork(filth)) return false;
            if (!Reachability.CanReach(pawn, filth, PathEndMode)) return false;
            return pawn.Map!.reservationManager.CanReserve(pawn, filth);
        }

        /// <summary>
        /// The most piles one job takes (RimWorld: the <c>num = 15</c> in <c>WorkGiver_CleanFilth.JobOnThing</c>,
        /// recalled rather than read). It is checked after each cell, as there, so a cell holding two piles can
        /// take a job one past it.
        /// </summary>
        public const int MaxPilesPerJob = 15;

        /// <summary>How many cells of <see cref="GenRadial.RadialPattern"/> round the first pile are searched
        /// for more (RimWorld: the loop to 100 in the same method, recalled), a radius of a little under six.</summary>
        public const int NearbyCellsSearched = 100;

        /// <summary>A queue at least this long is put in order of distance from the cleaner (RimWorld: the
        /// <c>Count &gt;= 5</c> before its <c>SortBy</c>, recalled).</summary>
        public const int SortQueueFrom = 5;

        private static readonly List<LocalTargetInfo> tmpPiles = new List<LocalTargetInfo>();

        /// <summary>
        /// The pile the scan found, and every other pile of cleaning work this pawn could take in the cells round
        /// it that belong to the same room or are a door into it (RimWorld: <c>WorkGiver_CleanFilth.JobOnThing</c>
        /// and its <c>ShouldClean</c>, where a door is its own portal room linked to the rooms either side; here a
        /// door has no room, so a door cell counts when a cardinal neighbour is in the room). A pile on a door,
        /// whose room is the door itself, takes only itself.
        /// </summary>
        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            Map.Map map = thing.Map!;
            Room? room = map.roomTracker.RoomAt(thing.Position);
            tmpPiles.Clear();
            tmpPiles.Add(thing);
            IReadOnlyList<IntVec3> pattern = GenRadial.RadialPattern;
            for (int i = 0; room != null && i < NearbyCellsSearched; i++)
            {
                IntVec3 c = thing.Position + pattern[i];
                if (!ShouldClean(map, c, room)) continue;
                IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(c);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing t = things[j];
                    if (t != thing && HasJobOnThing(pawn, t, forced)) tmpPiles.Add(t);
                }
                if (tmpPiles.Count >= MaxPilesPerJob) break;
            }

            // Stable, so equally distant piles keep the order the radial search found them in.
            if (tmpPiles.Count >= SortQueueFrom)
            {
                IntVec3 from = pawn.Position;
                for (int i = 1; i < tmpPiles.Count; i++)
                {
                    LocalTargetInfo pile = tmpPiles[i];
                    int d = pile.Cell.DistanceToSquared(from);
                    int k = i - 1;
                    while (k >= 0 && tmpPiles[k].Cell.DistanceToSquared(from) > d)
                    {
                        tmpPiles[k + 1] = tmpPiles[k];
                        k--;
                    }
                    tmpPiles[k + 1] = pile;
                }
            }

            var job = new Job(CleaningJobDefOf.Clean, tmpPiles[0]);
            for (int i = 1; i < tmpPiles.Count; i++) job.AddQueuedTarget(TargetIndex.A, tmpPiles[i]);
            tmpPiles.Clear();
            return job;
        }

        private static bool ShouldClean(Map.Map map, IntVec3 c, Room room)
        {
            if (!GenGrid.InBounds(c, map)) return false;
            if (ReferenceEquals(map.roomTracker.RoomAt(c), room)) return true;
            if (!(map.edificeGrid[c] is Door)) return false;
            for (int d = 0; d < 4; d++)
            {
                IntVec3 n = c + GenAdj.CardinalDirections[d];
                if (GenGrid.InBounds(n, map) && ReferenceEquals(map.roomTracker.RoomAt(n), room)) return true;
            }
            return false;
        }
    }
}
