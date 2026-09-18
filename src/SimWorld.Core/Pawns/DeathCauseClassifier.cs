using System.Collections.Generic;

using SimWorld.Health;

namespace SimWorld.Pawns
{
    /// <summary>
    /// Reads a <see cref="DeathCause"/> off the evidence a dying pawn carries, so that every death in the
    /// port arrives somewhere classified rather than only the two that happened to have a caller who knew.
    ///
    /// <para/><b>Why this exists.</b> <see cref="DeathCause"/>'s own doc recorded the hole: Starvation and
    /// Disease deaths happen — a <c>Need_Food</c> at zero and a lethal <c>Hediff</c> both reach
    /// <c>Pawn_HealthTracker.Kill</c> — but nothing routed them back to
    /// <see cref="FamilyManager.HandleDeath"/>, so <c>Storyteller.RecordDeath</c> never fired for them and
    /// the chronicle never heard about them at all. Only <see cref="DeathCause.Age"/> (from
    /// <see cref="FamilyManager.ProcessDeathsFromAge"/>) and <see cref="DeathCause.Injury"/> (from
    /// <c>Director.SettlementRaidResolver</c>) were wired. The two causes a *pressure* on this civilization
    /// would actually kill people with were the two the game could not see.
    ///
    /// <para/><b>The caller still wins where it knows.</b> A site that kills somebody for a reason — age, an
    /// abstractly-resolved raid — passes that reason down and it is used verbatim. Classification is the
    /// fallback for the deaths nobody declared, which is most of them.
    ///
    /// <para/><b>Immunizability is what separates a disease from an injury's sequel.</b> Both arrive as a
    /// lethal <see cref="Hediff"/> with no <see cref="DamageInfo"/> behind them, so the hediff is all there
    /// is to go on. Every shipped disease (<c>WoundInfection</c>, <c>Flu</c>, <c>Plague</c>) carries
    /// <see cref="HediffCompProperties_Immunizable"/>; <c>BloodLoss</c> and <c>Malnutrition</c> do not. That
    /// is a real structural difference — an immune system is a thing you have against infections — and not a
    /// list of defNames that content would silently outgrow.
    ///
    /// <para/><b>A wound infection counts as Disease, deliberately.</b> It begins in an injury, so the other
    /// reading is defensible. This classifies by the <i>proximate</i> cause, because that is the one the
    /// evidence supports and because a settlement dying of infected wounds and a settlement dying of arrows
    /// are different situations that want different fixes. The raid's direct toll is still visible: the
    /// people it killed outright are counted as <see cref="DeathCause.Injury"/>.
    /// </summary>
    public static class DeathCauseClassifier
    {
        /// <summary>
        /// <paramref name="declared"/> is the cause the killing site supplied, or null when it had none.
        /// <paramref name="dinfo"/> and <paramref name="culprit"/> are <c>Pawn_HealthTracker.Kill</c>'s own
        /// two arguments, unchanged.
        /// </summary>
        public static DeathCause Classify(DeathCause? declared, DamageInfo? dinfo, Hediff? culprit)
        {
            if (declared.HasValue) return declared.Value;

            // Damage behind the death is the least ambiguous evidence there is.
            if (dinfo != null) return DeathCause.Injury;

            if (culprit?.def != null)
            {
                if (HediffDefOf.Malnutrition != null && culprit.def == HediffDefOf.Malnutrition)
                {
                    return DeathCause.Starvation;
                }

                if (IsImmunizable(culprit.def)) return DeathCause.Disease;

                // BloodLoss and anything else lethal that is not an infection: the wound finished the job.
                return DeathCause.Injury;
            }

            return DeathCause.Unknown;
        }

        /// <summary>Whether <paramref name="def"/> is something a body builds immunity against — the shipped
        /// marker of an infectious disease, carried as a comp rather than a flag.</summary>
        public static bool IsImmunizable(HediffDef def)
        {
            List<HediffCompProperties>? comps = def?.comps;
            if (comps == null) return false;
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] is HediffCompProperties_Immunizable) return true;
            }
            return false;
        }
    }
}
