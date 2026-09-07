namespace SimWorld.Pawns
{
    /// <summary>
    /// Why a pawn died, as a small closed set rather than a string (SimWorld convention: defNames and enums
    /// travel through the sim, prose is a render-layer concern — see <c>docs/spec/simworld-spec.md</c> §6, and
    /// Epoch's own <c>DeathCause</c> four-variant enum this mirrors, <c>docs/research/epoch-inspiration.md</c>
    /// §5). Only <see cref="Age"/> is wired up by this module (<see cref="FamilyManager.ProcessDeathsFromAge"/>);
    /// the rest are reserved for the systems that will one day report through
    /// <see cref="FamilyManager.HandleDeath"/> — starvation from <c>Need_Food</c> reaching 0, disease from a
    /// lethal <c>Hediff</c>, injury from combat/accidents. <see cref="Unknown"/> is the fallback for a death
    /// that reaches the chronicle without a caller-supplied cause.
    /// </summary>
    public enum DeathCause
    {
        Age,
        Starvation,
        Disease,
        Injury,
        Unknown,
    }
}
