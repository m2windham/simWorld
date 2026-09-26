using SimWorld.Pawns;

namespace SimWorld.AI.Group
{
    /// <summary>
    /// One phase of a lord job — assault, leave, flee — whose one job is to hand every pawn the lord owns the
    /// same duty (RimWorld: <c>Verse.AI.Group.LordToil</c>). The duty is what the pawn actually does; see
    /// <see cref="DutyDef"/> for how a duty reaches the think tree.
    /// </summary>
    public abstract class LordToil
    {
        public Lord? lord;

        /// <summary>Entering this toil, before duties are handed out.</summary>
        public virtual void Init()
        {
        }

        /// <summary>Hands every owned pawn this toil's duty. Called on entering the toil and on every pawn
        /// the lord gains while in it.</summary>
        public abstract void UpdateAllDuties();

        /// <summary>Leaving this toil.</summary>
        public virtual void Cleanup()
        {
        }

        public override string ToString() => GetType().Name;
    }

    /// <summary>Cross the map and take the settlement (RimWorld: <c>RimWorld.LordToil_AssaultColony</c>, which
    /// hands out <c>DutyDefOf.AssaultColony</c> — <see cref="DutyDefOf.AssaultSettlement"/> here).</summary>
    public sealed class LordToil_AssaultColony : LordToil
    {
        public override void UpdateAllDuties()
        {
            if (lord == null) return;
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn.mindState != null) pawn.mindState.duty = DutyDefOf.AssaultSettlement;
            }
        }
    }

    /// <summary>
    /// Walk off the map by the nearest edge (RimWorld: <c>Verse.AI.Group.LordToil_ExitMap</c>, which hands out
    /// <c>DutyDefOf.ExitMapBest</c>). What a raid that has given up does. RimWorld's toil also carries a
    /// locomotion urgency (<c>Jog</c> for a raid) and a <c>canDig</c> flag; this port has neither a movement
    /// speed choice nor digging, so neither is carried.
    ///
    /// <para/>Like RimWorld's, entering this toil ends nobody's current job: a raider mid-fight finishes the
    /// attack it is on and takes the new duty the next time it looks for work.
    /// </summary>
    public sealed class LordToil_ExitMap : LordToil
    {
        public override void UpdateAllDuties()
        {
            if (lord == null) return;
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn.mindState != null) pawn.mindState.duty = LordDutyDefOf.ExitMapBest;
            }
        }
    }

    /// <summary>
    /// Break and run (RimWorld: <c>RimWorld.LordToil_PanicFlee</c>). The toil every lord of an
    /// <see cref="Factions.FactionDef.autoFlee"/> faction can reach from any other once it has lost enough of
    /// itself — see <see cref="Lord.SetJob"/>.
    ///
    /// <para/><b>What RimWorld does here, and the translation.</b> RimWorld's <c>Init</c> starts the
    /// <c>PanicFlee</c> mental state on every member, and its <c>UpdateAllDuties</c> hands each one
    /// <c>DutyDefOf.ExitMapRandom</c> underneath it. The mental state is what the pawn actually acts on: its
    /// subtree in <c>SubTrees_Misc.xml</c> is <c>JobGiver_ExitMapPanic</c> (the nearest edge) falling back to
    /// a wander, it sits above every other tier so a fleeing pawn does not fight, and starting it stops the
    /// pawn's current job at once. This port's mental states are mood breaks with a letter each and no
    /// per-state subtree, so the toil hands the mental state's own subtree out as the duty
    /// (<see cref="LordDutyDefOf.PanicFlee"/>), stops each member's job here the way starting the state
    /// would, and leaves "does not fight" to <see cref="CombatPostureUtility"/>, which gives a leaving pawn
    /// the acquire radius of the unarmed. The one difference that remains: a raider cornered while fleeing
    /// here swings back, where RimWorld's panicking one would not.
    /// </summary>
    public sealed class LordToil_PanicFlee : LordToil
    {
        public override void Init()
        {
            if (lord == null) return;
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn.Spawned && pawn.jobs?.curJob != null) pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }
        }

        public override void UpdateAllDuties()
        {
            if (lord == null) return;
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn.mindState != null) pawn.mindState.duty = LordDutyDefOf.PanicFlee;
            }
        }
    }
}
