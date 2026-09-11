namespace SimWorld.Pawns
{
    /// <summary>
    /// Why a pawn died, as a small closed set rather than a string (SimWorld convention: defNames and enums
    /// travel through the sim, prose is a render-layer concern — see <c>docs/spec/simworld-spec.md</c> §6, and
    /// Epoch's own <c>DeathCause</c> four-variant enum this mirrors, <c>docs/research/epoch-inspiration.md</c>
    /// §5). <see cref="Age"/> is reported by <see cref="FamilyManager.ProcessDeathsFromAge"/> and
    /// <see cref="Injury"/> by <c>Director.SettlementRaidResolver</c> — the note that said only Age was wired
    /// predates the raid module and has been corrected. <see cref="Starvation"/> and <see cref="Disease"/>
    /// are still reserved: both of those deaths happen today (a <c>Need_Food</c> at zero and a lethal
    /// <c>Hediff</c> both reach <c>Pawn_HealthTracker.Kill</c>), but they go through the pawn's own health
    /// funnel and nothing routes them back to <see cref="FamilyManager.HandleDeath"/>, so the chronicle never
    /// hears about them at all. <see cref="Unknown"/> is the fallback for a death that reaches the chronicle
    /// without a caller-supplied cause.
    ///
    /// <para/><b>This is the caller's classification, not the body's.</b> Whether a death was <i>violent</i>
    /// is answered separately and from the corpse, by <c>Pawn_HealthTracker.DiedViolently</c> reading
    /// <c>DamageDef.externalViolence</c> — that is what the storyteller and the witnesses in the room
    /// consult. The two agree where both exist (a raid death is an Injury and is violent); they are separate
    /// because only one of them can be known at a site that decides to kill somebody for a reason.
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
