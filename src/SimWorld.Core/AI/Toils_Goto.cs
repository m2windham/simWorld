namespace SimWorld.AI
{
    /// <summary>Toils that walk the pawn to a job target (RimWorld: <c>Verse.AI.Toils_Goto</c>).</summary>
    public static class Toils_Goto
    {
        /// <summary>Paths to the target named by <paramref name="ind"/>, however it moves. Completes on arrival.</summary>
        public static Toil GotoCell(TargetIndex ind, PathEndMode peMode)
        {
            var toil = new Toil { defaultCompleteMode = ToilCompleteMode.PatherArrival };
            toil.initAction = () => toil.Pawn.pather.StartPath(toil.Job.GetTarget(ind), peMode);
            toil.FailOnDespawnedOrNull(ind);
            toil.FailOn(() => toil.Pawn.pather.Failed);
            return toil;
        }

        /// <summary>Same as <see cref="GotoCell"/>; named separately because RimWorld does, for a Thing target.</summary>
        public static Toil GotoThing(TargetIndex ind, PathEndMode peMode) => GotoCell(ind, peMode);
    }
}
