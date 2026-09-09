namespace SimWorld.Social.Ideology
{
    /// <summary>
    /// Ritual-quality tuning. RimWorld rolls ritual quality from a real per-ritual
    /// <c>RitualOutcomeEffectDef</c> — a list of comps (participant count, role presence, an ideoligion's
    /// precepts, the physical setting: room impressiveness, weather, an altar's presence) each contributing
    /// their own offset, which could not be checked against RimWorld's decompiled source from this
    /// environment. This class ports the *shape* — several independent factors summing into one [0, 1]
    /// quality — with SimWorld's own invented magnitudes, exactly as <see cref="Social.SocialTuning"/> and
    /// <see cref="God.GodTuning"/> already do for their own systems; every constant below is documented at its
    /// declaration and pinned by <c>IdeologyTests</c>' own trend assertions (more/better participants never
    /// score lower, never a literal number) rather than trusted as a magic constant.
    /// </summary>
    public static class RitualTuning
    {
        /// <summary>
        /// Quality contributed by how many participants attend, before any role or mood term. Diminishing
        /// returns — a dozen more onlookers matters far less than going from a lone officiant to a real
        /// gathering — the shape RimWorld's own comps use (a curve, not a flat per-head bonus), though not
        /// its literal points. See this class's own header for why the points themselves are this port's own
        /// invention.
        /// </summary>
        public static readonly SimpleCurve ParticipantCountQualityCurve = new SimpleCurve(new[]
        {
            new CurvePoint(1f, 0f),
            new CurvePoint(3f, 0.12f),
            new CurvePoint(6f, 0.22f),
            new CurvePoint(12f, 0.30f),
        });

        /// <summary>Quality bonus when a holder of <see cref="RitualDef.officiantRole"/> is among the
        /// participants — on top of that role's own <see cref="IdeoRoleDef.ritualQualityOffset"/>.</summary>
        public const float OfficiantPresentBonus = 0.15f;

        /// <summary>
        /// Weight of the participants' own mean mood (<c>Need_Mood.CurLevelPercentage</c>) in
        /// <see cref="RitualUtility.ComputeQuality"/>, centered on 0.5 so an average crowd neither helps nor
        /// hurts. RimWorld rolls part of ritual quality from the physical setting (room impressiveness, an
        /// altar, weather) — no Map/Building/room concept reaches <c>Social</c> yet, the same gap this
        /// system's own tracker entry calls out — so a happier crowd stands in for a better atmosphere, the
        /// same kind of honest, documented substitution <see cref="Research.EraDef.threatPointsFactor"/>
        /// already makes for a term nothing computes yet.
        /// </summary>
        public const float MeanMoodQualityWeight = 0.2f;

        /// <summary>Deterministic jitter (via <see cref="Sim.Rand"/>) so two rituals with identical inputs are
        /// not bit-for-bit identical forever, without swamping the participant/role/mood terms above.</summary>
        public const float QualityJitter = 0.05f;
    }
}
