using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Things;

namespace SimWorld.AI
{
    /// <summary>
    /// Reserves a food Thing, walks to it, chews for a while, then eats it (RimWorld: <c>RimWorld.JobDriver_Ingest</c>,
    /// trimmed — no animal-food-in-bowl, no toxic-fallout check). A citizen with a table within reach of the food
    /// takes the sitting to it and eats there (RimWorld: <c>Toils_Ingest.CarryIngestibleToChewSpot</c>, recalled),
    /// and the meal leaves its memories either way (<see cref="IngestThoughtUtility.ApplyThoughts"/>).
    /// </summary>
    public sealed class JobDriver_Ingest : JobDriver
    {
        /// <summary>RimWorld's real chew duration varies by food and pawn; pinned to a constant here rather
        /// than guessed, per this module's own tests (see <c>AITests</c>).</summary>
        public const int IngestDurationTicks = 200;

        /// <summary>The sitting, taken off the map while it is carried to the table.</summary>
        private Thing? carried;

        public override Thing? HeldOffMap
        {
            get => carried;
            set => carried = value;
        }

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        /// <summary>An interrupted carry puts the food down where the eater stands, as a haul does.</summary>
        public override void Notify_Ending()
        {
            base.Notify_Ending();
            Thing? thing = carried;
            carried = null;
            if (thing == null || thing.Destroyed || thing.Spawned) return;
            if (pawn.Map == null || !pawn.Spawned) return;
            GenSpawn.Spawn(thing, pawn.Position, pawn.Map);
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);

            Toil gotoFood = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);
            yield return gotoFood;

            // The table is chosen where the food lies, as RimWorld searches from where the meal is picked up.
            Thing? food = job.GetTarget(TargetIndex.A).Thing;
            Thing? table = food != null && food.Spawned ? IngestThoughtUtility.TableFor(pawn, food.Position) : null;
            if (table != null)
            {
                job.SetTarget(TargetIndex.B, table);
                yield return Toils_General.Do(PickUpSitting);
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
                yield return Toils_General.Wait(IngestDurationTicks);
                yield return Toils_General.Do(() =>
                {
                    Thing? sitting = carried;
                    carried = null;
                    if (sitting == null || sitting.Destroyed) return;
                    pawn.needs.food?.Eat((sitting.def.ingestible?.nutrition ?? 0f) * sitting.stackCount);
                    ThingDef def = sitting.def;
                    sitting.Destroy();
                    Thing? at = job.GetTarget(TargetIndex.B).Thing;
                    IngestThoughtUtility.ApplyThoughts(pawn, def, at != null && at.Spawned ? at : null);
                });
                yield break;
            }

            Toil chew = Toils_General.Wait(IngestDurationTicks);
            chew.FailOnDespawnedOrNull(TargetIndex.A);
            yield return chew;

            yield return Toils_General.Do(() =>
            {
                Thing? eaten = job.GetTarget(TargetIndex.A).Thing;
                if (eaten == null || eaten.Destroyed) return;

                int count = SittingCount(eaten);
                float nutrition = eaten.def.ingestible?.nutrition ?? 0f;
                pawn.needs.food?.Eat(nutrition * count);
                eaten.stackCount -= count;
                if (eaten.stackCount <= 0) eaten.Destroy();
                IngestThoughtUtility.ApplyThoughts(pawn, eaten.def, null);
            });
        }

        /// <summary>
        /// RimWorld eats a *sitting*, not a unit: Toils_Ingest takes job.count units in one chew, and
        /// job.count is FoodUtility.WillIngestStackCountOf. This port ate one unit per job, which for
        /// every raw foodstuff it ships (0.05 nutrition against a 1.0 stomach) meant a citizen could
        /// not out-eat its own hunger no matter how much food was in front of it. See that method for
        /// the whole argument.
        /// </summary>
        private int SittingCount(Thing food)
        {
            int count = SimWorld.Crafting.FoodUtility.WillIngestStackCountOf(pawn, food.def);
            if (count > food.stackCount) count = food.stackCount;
            if (count < 1) count = 1;
            return count;
        }

        /// <summary>Takes the sitting off the stack: the whole stack when it is all one sitting, else a split.</summary>
        private void PickUpSitting()
        {
            Thing? food = job.GetTarget(TargetIndex.A).Thing;
            if (food == null || food.Destroyed || !food.Spawned)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            int count = SittingCount(food);
            if (count >= food.stackCount)
            {
                food.DeSpawn();
                carried = food;
                return;
            }
            food.stackCount -= count;
            Thing sitting = ThingMaker.MakeThing(food.def, food.Stuff);
            sitting.stackCount = count;
            carried = sitting;
        }
    }
}
