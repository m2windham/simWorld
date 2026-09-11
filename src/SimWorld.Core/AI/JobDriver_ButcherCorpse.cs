using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Fetches a <see cref="Corpse"/>, carries it to a butcher bench and butchers it there (RimWorld:
    /// <c>JobDriver_DoBill</c> hauling a corpse ingredient to a butcher table and working a
    /// <c>ButcherCorpseFlesh</c> bill). Target A is the corpse, target B the bench.
    /// <para/>
    /// <b>Carrying, with nothing to carry it in.</b> This port has no carry tracker, so the corpse is taken
    /// off the map at the pickup toil and put back on at the bench — the same abstract carry
    /// <see cref="JobDriver_HaulToCell"/> and <see cref="JobDriver_Warden_Feed"/> already use. The difference
    /// that matters is that a corpse is not interchangeable with another of its def (it holds a specific
    /// person or animal), so the real Thing travels: nothing is destroyed and re-created, and
    /// <see cref="Notify_Ending"/> puts the body back on the ground where the pawn is standing if anything
    /// interrupts the job mid-carry, rather than leaving it in limbo.
    /// <para/>
    /// <b>Work rate</b> is <see cref="JobDriver_DoBill"/>'s, reused outright rather than copied: the same
    /// per-tick base and the same skill-speed curve a bench recipe is worked at, against the shipped
    /// <c>ButcherAnimal</c> recipe's own <see cref="RecipeDef.workAmount"/> and
    /// <see cref="RecipeDef.workSkill"/>. Cooking XP for the job is awarded by
    /// <see cref="Recipe_ButcherAnimal"/> itself, once, exactly as it is for the hunt's kill-site butchery.
    /// </summary>
    public sealed class JobDriver_ButcherCorpse : JobDriver
    {
        /// <summary>Set while the body is off the map between the pickup toil and the bench; see the class
        /// remarks. Never Scribed — no <see cref="JobDriver"/> in this port is (see that class's own doc) —
        /// so a save taken mid-carry loses the body, the same window every other abstract carry here has.</summary>
        private Corpse? carried;

        public override bool TryMakePreToilReservations()
        {
            Map.Map? map = pawn.Map;
            if (map == null) return false;
            return map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A))
                && map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.B));
        }

        public override void Notify_Ending()
        {
            base.Notify_Ending();
            DropCarriedCorpse();
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Reserve.Reserve(TargetIndex.B);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);

            yield return Toils_General.Do(() =>
            {
                if (!(job.GetTarget(TargetIndex.A).Thing is Corpse corpse) || corpse.Destroyed || !corpse.Spawned)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                carried = corpse;
                corpse.DeSpawn();
            });

            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);

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

                Corpse? corpse = carried;
                Thing? bench = job.GetTarget(TargetIndex.B).Thing;
                if (corpse == null || corpse.Destroyed || corpse.InnerPawn == null || bench == null || pawn.Map == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                // Put the body down on the bench's own cell before butchering: Recipe_ButcherAnimal drops its
                // products where the carcass is, and JobDriver_DoBill already drops a bench recipe's products
                // on the bench cell, so this is the same place a cooked meal would appear.
                GenSpawn.Spawn(corpse, bench.Position, pawn.Map);
                carried = null;
                ButcherUtility.TryButcher(corpse.InnerPawn, pawn, CorpseWorkDefOf.ButcherAnimal);
                ReadyForNextToil();
            };
            yield return work;
        }

        private void DropCarriedCorpse()
        {
            Corpse? corpse = carried;
            carried = null;
            if (corpse == null || corpse.Destroyed || corpse.Spawned) return;

            Map.Map? map = pawn.Map;
            if (map == null || !pawn.Spawned) return;
            GenSpawn.Spawn(corpse, pawn.Position, map);
        }
    }
}
