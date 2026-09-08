using SimWorld.Pawns;

namespace SimWorld.God
{
    /// <summary>
    /// The one concrete <see cref="EdictWorker"/> this pass ships, exercising the <c>Class=</c> seam for
    /// something <see cref="EdictDef"/>'s declarative fields cannot express on their own: a harsh edict
    /// (a great-works levy, forced overtime) whose work push and mood cost reach every adult citizen but not
    /// children. <see cref="DemographyTuning.MinMarriageAgeYears"/> is reused rather than inventing a second
    /// "adult" threshold — it is already this codebase's one sourced line between child and adult.
    /// </summary>
    public sealed class EdictWorker_ExemptMinors : EdictWorker
    {
        public override bool AppliesTo(Pawn pawn) =>
            base.AppliesTo(pawn) && pawn.ageTracker.AgeBiologicalYearsFloat >= DemographyTuning.MinMarriageAgeYears;
    }
}
