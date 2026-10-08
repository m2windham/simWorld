using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Fetches a <see cref="Corpse"/>, carries it to a butcher bench and butchers it there (RimWorld:
    /// <c>JobDriver_DoBill</c> hauling a corpse ingredient to a butcher table and working a
    /// <c>ButcherCorpseFlesh</c> bill). Target A is the corpse — and, once it has been picked up, the Thing in
    /// the pawn's hands, which is the same corpse — target B the bench.
    /// <para/>
    /// <b>The body is carried, not parked in a field.</b> Pickup moves the corpse into the pawn's
    /// <see cref="Pawns.Pawn_CarryTracker"/> (<see cref="Toils_Haul.StartCarryThing"/>, the toil
    /// <see cref="JobDriver_HaulToCell"/> uses) and it is put down on the bench's own cell when the work is
    /// done. A corpse is not interchangeable with another of its def (it holds a specific person or animal), so
    /// the real Thing travels: nothing is destroyed and re-created, and if anything interrupts the job — on the
    /// way or at the bench — <see cref="Pawn_JobTracker.EndCurrentJob"/> puts the body down where the pawn is
    /// standing rather than leaving it in limbo. This driver used to keep the body in a field of its own, which
    /// did the same for an interruption but lost it to a save taken mid-carry (no <see cref="JobDriver"/> in this
    /// port is Scribed); the carry tracker is deep-saved with the pawn.
    /// <para/>
    /// <b>Work rate</b> is <see cref="JobDriver_DoBill"/>'s, reused outright rather than copied: the same
    /// per-tick base and the same skill-speed curve a bench recipe is worked at, against the shipped
    /// <c>ButcherAnimal</c> recipe's own <see cref="RecipeDef.workAmount"/> and
    /// <see cref="RecipeDef.workSkill"/>. Cooking XP for the job is awarded by
    /// <see cref="Recipe_ButcherAnimal"/> itself, once, exactly as it is for the hunt's kill-site butchery.
    /// </summary>
    public sealed class JobDriver_ButcherCorpse : JobDriver
    {
        public override bool TryMakePreToilReservations()
        {
            Map.Map? map = pawn.Map;
            if (map == null) return false;
            return map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A))
                && map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.B));
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Reserve.Reserve(TargetIndex.B);

            yield return Toils_Haul.GotoThingToCarry(TargetIndex.A, PathEndMode.ClosestTouch);

            // Only a body can be butchered; anything else is refused here, before it leaves the map.
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, thing => thing is Corpse ? thing.stackCount : 0);

            Toil gotoBench = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            gotoBench.FailOn(() => !Toils_Haul.IsCarryingTarget(pawn, job, TargetIndex.A));
            yield return gotoBench;

            var work = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            work.FailOnDespawnedOrNull(TargetIndex.B);
            float workDone = 0f;
            work.tickAction = () =>
            {
                RecipeDef recipe = CorpseWorkDefOf.ButcherAnimal;
                int skillLevel = recipe.workSkill != null
                    ? pawn.skills?.GetSkill(recipe.workSkill)?.Level ?? 0
                    : 0;
                workDone += JobDriver_DoBill.BaseWorkPerTick * JobDriver_DoBill.WorkSpeedFactorFromSkillLevel.Evaluate(skillLevel);
                if (workDone < recipe.WorkAmountTotal()) return;

                Corpse? corpse = pawn.carryTracker.CarriedThing as Corpse;
                Thing? bench = job.GetTarget(TargetIndex.B).Thing;
                if (corpse == null || corpse.Destroyed || corpse.InnerPawn == null || bench == null || pawn.Map == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // Put the body down on the bench's own cell before butchering: Recipe_ButcherAnimal drops its
                // products where the carcass is, and JobDriver_DoBill already drops a bench recipe's products
                // on the bench cell, so this is the same place a cooked meal would appear.
                if (!pawn.carryTracker.TryDropCarriedThing(bench.Position, out _))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                ButcherUtility.TryButcher(corpse.InnerPawn, pawn, CorpseWorkDefOf.ButcherAnimal);
                ReadyForNextToil();
            };
            yield return work;
        }
    }
}
