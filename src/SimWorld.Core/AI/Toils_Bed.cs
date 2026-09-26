namespace SimWorld.AI
{
    /// <summary>Toils for getting a pawn onto a bed (RimWorld: <c>Verse.AI.Toils_Bed</c>, the one this port
    /// needs).</summary>
    public static class Toils_Bed
    {
        /// <summary>
        /// Walks the pawn onto the bed named by <paramref name="bedIndex"/> — onto its sleeping slot
        /// (<see cref="BedUtility.GetSleepingSlotPos(Things.Thing, int)"/>), not merely onto some cell of it
        /// (RimWorld: <c>Toils_Bed.GotoBed</c>, which paths to <c>RestUtility.GetBedSleepingSlotPosFor</c>
        /// with <see cref="PathEndMode.OnCell"/>). There is one slot to a single bed and no slot assignment in
        /// this port, so it is always slot 0. Completes on arrival; fails if the bed goes or the path does.
        /// </summary>
        public static Toil GotoBed(TargetIndex bedIndex)
        {
            var toil = new Toil { defaultCompleteMode = ToilCompleteMode.PatherArrival };
            toil.initAction = () =>
            {
                Things.Thing? bed = toil.Job.GetTarget(bedIndex).Thing;
                if (bed == null || !bed.Spawned) return; // FailOnDespawnedOrNull below ends the job
                toil.Pawn.pather.StartPath(bed.GetSleepingSlotPos(), PathEndMode.OnCell);
            };
            toil.FailOnDespawnedOrNull(bedIndex);
            toil.FailOn(() => toil.Pawn.pather.Failed);
            return toil;
        }
    }
}
