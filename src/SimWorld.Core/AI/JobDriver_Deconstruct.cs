using System;
using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// Takes a designated building down and gives back part of what it cost (RimWorld:
    /// <c>JobDriver_Deconstruct</c> over <c>JobDriver_RemoveBuilding</c>, then <c>GenLeaving</c>). The work is
    /// the building's <c>WorkToBuild</c> clamped to [<see cref="MinWork"/>, <see cref="MaxWork"/>], worked at the
    /// same construction-skill rate <see cref="JobDriver_ConstructFinishFrame"/> builds at. On completion the
    /// building is destroyed with <see cref="DestroyMode.Deconstruct"/> and each material in its adjusted cost
    /// list comes back as <see cref="ResourcesFractionWhenDeconstructed"/> of its count, randomly rounded.
    /// RimWorld's per-def <c>resourcesFractionWhenDeconstructed</c> is not a field here; every building takes
    /// the default. All three numbers are recalled rather than re-read from a decompile.
    /// </summary>
    public sealed class JobDriver_Deconstruct : JobDriver
    {
        public const float MinWork = 20f;
        public const float MaxWork = 3000f;
        public const float ResourcesFractionWhenDeconstructed = 0.5f;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            var work = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            work.FailOnDespawnedOrNull(TargetIndex.A);
            float workDone = 0f;
            work.tickAction = () =>
            {
                Thing building = work.Job.GetTarget(TargetIndex.A).Thing!;
                // Only a building comes down this way; anything else ordered here (MapCommands.OrderJob takes any
                // job on any thing) ends the job rather than being destroyed.
                if (building.def.category != ThingCategory.Building || building.def.mineable)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                int skillLevel = work.Pawn.skills?.GetSkill(SkillDefOf.Construction)?.Level ?? 0;
                workDone += JobDriver_ConstructFinishFrame.BaseWorkPerTick
                    * JobDriver_ConstructFinishFrame.WorkSpeedFactorFromConstructionLevel.Evaluate(skillLevel);
                if (workDone < TotalNeededWork(building)) return;
                Deconstruct(building);
                work.actor.ReadyForNextToil();
            };
            yield return work;
        }

        public static float TotalNeededWork(Thing building) =>
            Math.Min(MaxWork, Math.Max(MinWork, building.def.GetStatValue(StatDefOf.WorkToBuild, building.Stuff)));

        /// <summary>Destroys <paramref name="building"/> and drops its refund on its cell (RimWorld:
        /// <c>Destroy(DestroyMode.Deconstruct)</c> → <c>GenLeaving.DoLeavingsFor</c>).</summary>
        public static void Deconstruct(Thing building) => Deconstruct(building, DestroyMode.Deconstruct, null);

        /// <summary>As <see cref="Deconstruct(Thing)"/>, destroyed with <paramref name="mode"/> and its refund
        /// dropped on <paramref name="dropCell"/> (its own cell when null). A building replaced in place
        /// (<see cref="Building.Frame.CompleteConstruction"/>) goes with <see cref="DestroyMode.WillReplace"/>, so
        /// its cell is not read as roof support lost, and drops beside itself, since the new one takes its cell.</summary>
        public static void Deconstruct(Thing building, DestroyMode mode, IntVec3? dropCell)
        {
            Map.Map map = building.Map ?? throw new InvalidOperationException("Building is not spawned.");
            IntVec3 pos = dropCell ?? building.Position;
            List<ThingDefCountClass> cost = building.def.CostListAdjusted(building.Stuff);
            building.Destroy(mode);
            for (int i = 0; i < cost.Count; i++)
            {
                int count = Math.Min(cost[i].count, GenMath.RoundRandom(cost[i].count * ResourcesFractionWhenDeconstructed, Rand.Current));
                if (count <= 0) continue;
                Thing stack = ThingMaker.MakeThing(cost[i].thingDef);
                stack.stackCount = count;
                GenSpawn.Spawn(stack, pos, map);
            }
        }
    }
}
