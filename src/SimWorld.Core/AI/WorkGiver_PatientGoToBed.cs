using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Health;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.AI
{
    /// <summary>
    /// A pawn that needs medical rest takes itself to bed and stays there (RimWorld:
    /// <c>WorkGiver_PatientGoToBedEmergencyTreatment</c> and <c>WorkGiver_PatientGoToBedRecuperate</c>, the
    /// givers behind the Patient and Bed rest work types). One class backs both content defs, as
    /// <see cref="WorkGiver_Tend"/> does for its pair: the def's <see cref="WorkGiverDef.emergency"/> flag
    /// picks RimWorld's urgent test or its ordinary one.
    ///
    /// <para/><b>The missing link.</b> The Patient and PatientBedRest work types shipped with nothing behind
    /// them, so nobody in this port ever rested for a medical reason. Traced on a threat-free founding band
    /// (<c>docs/perf/storyteller-pressure</c>): citizens with a wound infection went on mining and sowing
    /// until the infection downed them, spending about a third of the illness in bed where a RimWorld
    /// colonist spends nearly all of it. Immunity is gained 18% faster in a bed, and that is most of the
    /// margin between living and dying at the tend quality a tribe can manage.
    ///
    /// <para/><b>Scans beds, not patients.</b> This port's work scan only drives scanners, so the giver
    /// offers the pawn every bed and takes the nearest one it can reserve and reach. No bed, no job —
    /// RimWorld's own answer (<c>RestUtility.FindPatientBedFor</c> returning null).
    /// </summary>
    public sealed class WorkGiver_PatientGoToBed : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.OnCell;

        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !PatientRestUtility.WantsBed(pawn, def.emergency);

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Thing> beds = map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed);
            for (int i = 0; i < beds.Count; i++) yield return beds[i];
        }

        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (thing.def != ConstructionThingDefOf.Bed || !thing.Spawned || thing.Map != pawn.Map) return false;
            if (!pawn.Map!.reservationManager.CanReserve(pawn, thing)) return false;
            return Reachability.CanReach(pawn, thing, PathEndMode.OnCell);
        }

        public override Job? JobOnThing(Pawn pawn, Thing thing, bool forced = false) =>
            new Job(JobDefOf.LayDown, thing) { restUntilHealed = true };
    }

    /// <summary>What a patient's lie-down asks while it lasts.</summary>
    public static class PatientRestUtility
    {
        /// <summary>RimWorld's two tests, by giver: the emergency one is
        /// <see cref="HealthAIUtility.ShouldSeekMedicalRestUrgent"/>, the ordinary one
        /// <see cref="HealthAIUtility.ShouldSeekMedicalRest"/>.</summary>
        public static bool WantsBed(Pawn pawn, bool emergency) =>
            !pawn.Downed && (emergency ? HealthAIUtility.ShouldSeekMedicalRestUrgent(pawn) : HealthAIUtility.ShouldSeekMedicalRest(pawn));

        /// <summary>
        /// What would get a resting patient out of bed (RimWorld: the lying-down toil re-runs the whole think
        /// tree every 211 ticks). This port asks the three tiers of its tree a patient can sensibly leave a
        /// bed for, in the tree's order: emergency work, food when hungry, then routine work — where Doctor
        /// and Firefighter sit above Bed rest, so a recuperating doctor still gets up to tend, and where the
        /// patient's own giver answers for everything below. Recreation is not asked about.
        /// </summary>
        public static Job? JobWorthGettingUpFor(Pawn pawn)
        {
            // The patient's own giver answers in both work lists; being told to lie down is not a reason
            // to get up, and must not hide the tiers below it.
            Job? emergency = JobGiver_Work.TryGiveEmergencyJob(pawn);
            if (emergency != null && !IsPatientLieDown(emergency)) return emergency;

            Need_Food? food = pawn.needs.food;
            if (food != null && food.CurCategory != HungerCategory.Fed)
            {
                Job? eat = new JobGiver_GetFood().TryIssueJobPackage(pawn).Job;
                if (eat != null) return eat;
            }

            if (pawn.workSettings == null || !pawn.workSettings.EverWork || pawn.Map == null) return null;
            Job? routine = WorkGiverScanUtility.TryGiveJobInGivers(pawn, pawn.workSettings.WorkGiversInOrderNormal);
            return routine != null && !IsPatientLieDown(routine) ? routine : null;
        }

        private static bool IsPatientLieDown(Job job) => job.def == JobDefOf.LayDown && job.restUntilHealed;
    }
}
