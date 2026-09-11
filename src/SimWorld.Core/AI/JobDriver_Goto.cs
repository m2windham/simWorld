using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns;

namespace SimWorld.AI
{
    /// <summary>
    /// Walks to a cell and stops (RimWorld: <c>Verse.AI.JobDriver_Goto</c>). Deliberately not
    /// <see cref="JobDriver_GotoWander"/> without its pause: a wander is an idle beat and this is a march, so
    /// a squad crossing a map should not stand still for a second at the end of every leg of it.
    /// <para/>
    /// <see cref="PathEndMode.Touch"/> rather than <c>OnCell</c>, because the cell this job is issued against
    /// is where an enemy pawn was standing when <see cref="JobGiver_AIGotoNearestHostile"/> looked: touching
    /// is the same end mode <see cref="AttackTargetFinder"/> tested reachability with when it picked that
    /// enemy, so a target that passed the acquire test can never fail the approach on end mode alone.
    /// </summary>
    public sealed class JobDriver_Goto : JobDriver
    {
        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.Touch);
        }
    }

    /// <summary>
    /// Walks to a map-edge cell and leaves (RimWorld: <c>JobDriver_Goto</c> with <c>Job.exitMapOnArrival</c>,
    /// which is a flag there and a job of its own here — a flag on <see cref="Job"/> would be a field on a
    /// shared file that only one driver reads).
    /// <para/>
    /// <b>This is what ends a raid.</b> Leaving is <see cref="Things.Thing.DeSpawn"/>: the pawn comes off the
    /// map's registries and off the tick lists, so a departed raider costs nothing and is simply no longer
    /// there — it is not destroyed, and the object the incident handed back
    /// (<c>Director.IncidentWorker_RaidEnemy.LastRaidPawns</c>) still names it. The duty goes with it, so a
    /// pawn that somehow lands on a map again arrives with no standing orders rather than resuming an
    /// assault on a settlement it has already left.
    /// </summary>
    public sealed class JobDriver_ExitMap : JobDriver
    {
        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);

            var leave = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            leave.initAction = () =>
            {
                Pawn actor = leave.Pawn;
                if (actor.mindState != null) actor.mindState.duty = null;
                if (actor.Spawned) actor.DeSpawn();
            };
            yield return leave;
        }
    }
}
