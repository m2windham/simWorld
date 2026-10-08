using System.Collections.Generic;

using SimWorld.AI;
using SimWorld.Map;
using SimWorld.Stats;

namespace SimWorld.Building
{
    /// <summary>
    /// Shared shape of a job that walks to a roof cell and spends work on it (RimWorld:
    /// <c>RimWorld.JobDriver_AffectRoof</c>): <see cref="JobDriver_BuildRoof"/> and
    /// <see cref="JobDriver_RemoveRoof"/> each supply <see cref="DoEffect"/> (what happens once the work is
    /// done) and <see cref="DoWorkFailOn"/> (when to give up mid-toil because the target already changed under
    /// them).
    ///
    /// <para/><b>Target A is the cell; target B is where the pawn walks to.</b> The two differ only for
    /// <see cref="JobDriver_BuildRoof"/>, when its <see cref="WorkGiver_BuildRoof"/> substitutes an adjacent
    /// building for B because the roof cell itself is not standable — see that class's own doc. B is where
    /// <see cref="Toils_Goto"/> paths to and where the toil's own reach is asked; A is what gets reserved and
    /// what <see cref="Cell"/> reports, matching RimWorld's own split exactly.
    ///
    /// <para/><b>Reservation shape, not RimWorld's own call.</b> RimWorld reserves the cell directly inside
    /// <c>TryMakePreToilReservations</c>, through a <c>ReservationLayerDef</c> ("Ceiling") that lets a roof
    /// claim and an ordinary claim coexist on the same cell. This port's <see cref="AI.ReservationManager"/>
    /// has no layers — see that class's own remarks on why one un-layered claim per pawn has been enough for
    /// every job this port has shipped so far — so, as every other job driver in this codebase already does,
    /// the actual claim is <see cref="Toils_Reserve.Reserve"/> as this class's own first toil, and
    /// <see cref="TryMakePreToilReservations"/> is only the early-abort check.
    ///
    /// <para/><b><see cref="StatDefOf.ConstructionSpeed"/>, read as the real stat, not a hand-rolled curve.</b>
    /// RimWorld's own toil reads <c>actor.GetStatValue(StatDefOf.ConstructionSpeed, true)</c> directly; this is
    /// the first construction job driver in this codebase written after <c>ConstructionSpeed</c> got a real,
    /// skill-driven <c>StatDef</c> (<c>Stats_Work.xml</c>) — <see cref="JobDriver_ConstructFinishFrame"/> and
    /// <see cref="AI.JobDriver_Repair"/> both predate it and read the Construction skill level directly with
    /// their own hand-rolled curve instead (each says so in its own doc), which <c>Stats_Work.xml</c>'s own
    /// comment already flags as unfinished wiring for "whichever module owns those job drivers" to pick up.
    /// This one reads the real stat, exactly as RimWorld's own <c>JobDriver_AffectRoof</c> does.
    ///
    /// <para/><b>No skill XP is granted, and that is RimWorld's own behaviour, verified against decompile.</b>
    /// Neither <c>JobDriver_AffectRoof</c> nor either of its two subclasses calls
    /// <c>SkillDefOf.Construction</c>/<c>skills.Learn</c> anywhere — unlike RimWorld's own
    /// <c>JobDriver_ConstructFinishFrame</c> (0.25 XP/tick) or <c>JobDriver_Repair</c> (this port's own
    /// <see cref="AI.JobDriver_Repair"/> ported that at 0.05/tick). Roofing a cell does not teach Construction
    /// in real RimWorld; this is not an omission.
    /// </summary>
    public abstract class JobDriver_AffectRoof : JobDriver
    {
        /// <summary>Work-units this job needs, at <see cref="StatDefOf.ConstructionSpeed"/> 1 (RimWorld:
        /// <c>JobDriver_AffectRoof.BaseWorkAmount</c>, sourced from decompile).</summary>
        public const float BaseWorkAmount = 65f;

        private float workLeft;

        /// <summary>The roof cell itself (RimWorld: <c>JobDriver_AffectRoof.Cell</c>, target A).</summary>
        protected IntVec3 Cell => job.GetTarget(TargetIndex.A).Cell;

        protected abstract PathEndMode PathEndMode { get; }

        /// <summary>Runs once the work toil's <see cref="workLeft"/> reaches zero.</summary>
        protected abstract void DoEffect();

        /// <summary>Extra reason, beyond the shared three below, to give up mid-toil (RimWorld:
        /// <c>JobDriver_AffectRoof.DoWorkFailOn</c>).</summary>
        protected abstract bool DoWorkFailOn();

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, Cell);

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);

            Toil gotoTarget = Toils_Goto.GotoCell(TargetIndex.B, PathEndMode);
            yield return gotoTarget;

            var doWork = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            doWork.initAction = () => workLeft = BaseWorkAmount;
            doWork.FailOnCannotReach(TargetIndex.B, PathEndMode);
            doWork.FailOn(DoWorkFailOn);
            doWork.tickAction = () =>
            {
                workLeft -= doWork.Pawn.GetStatValue(StatDefOf.ConstructionSpeed);
                if (workLeft > 0f) return;
                DoEffect();
                doWork.actor.ReadyForNextToil();
            };
            yield return doWork;
        }
    }
}
