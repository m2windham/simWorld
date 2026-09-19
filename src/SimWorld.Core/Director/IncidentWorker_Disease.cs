using System.Collections.Generic;
using System.Linq;

using SimWorld.Health;
using SimWorld.Letters;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// Gives a random candidate pawn (alive, not already sick with it) <see cref="IncidentDef.diseaseIncident"/>
    /// (RimWorld: <c>IncidentWorker_Disease</c>). Split into its own file, out of <c>IncidentWorker.cs</c>, the
    /// day this stopped being a one-line effect.
    ///
    /// <para/><b>Struck whether or not anyone is watching.</b>
    /// <see cref="Targets.CivilizationTarget.PlayerPawnsForStoryteller"/> draws from every settlement's
    /// <c>World.Settlement.Citizens</c> — Full and Interval tier alike — so this incident does not favour
    /// whichever settlement the player currently has open. That used to be a problem nothing else in this
    /// file could see: a citizen struck at <c>PawnTier.Interval</c> gained a hediff that then never ticked
    /// (<c>Pawn_TierTracker.ApplyElapsed</c>'s Interval branch ran no hediff physics at all) and no letter
    /// ever told the player it had happened, which inverted the whole point of tiering — looking away from a
    /// settlement made its people *safer* from a disease that had already caught them. Both gaps are closed
    /// now: <see cref="AbstractDiseaseResolver"/> races the disease to a real outcome even off the Normal
    /// tick list, and <see cref="SendLetter"/> tells the player either way, exactly as
    /// <see cref="IncidentWorker_ManhunterPack"/> already does for its own threat.
    /// </summary>
    public sealed class IncidentWorker_Disease : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) => CandidatePawns(parms).Any();

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (def.diseaseIncident == null) return false;
            List<Pawn> candidates = CandidatePawns(parms).ToList();
            if (candidates.Count == 0) return false;
            Pawn pawn = candidates[Rand.Range(0, candidates.Count)];

            Hediff hediff = pawn.health.AddHediff(def.diseaseIncident);

            // Provenance for a death with no instigator to ask. A disease kill reaches
            // Pawn_HealthTracker.Kill with dinfo null, so StorytellerDeathEvents.SourceOf has nothing to read
            // off an instigator and falls back to exactly this — see Hediff.sourceIncident's own doc.
            hediff.sourceIncident = def.defName;

            SendLetter(pawn, def.diseaseIncident);
            return true;
        }

        private IEnumerable<Pawn> CandidatePawns(IncidentParms parms)
        {
            HediffDef? disease = def.diseaseIncident;
            if (disease == null) return Enumerable.Empty<Pawn>();
            return parms.target.PlayerPawnsForStoryteller.Where(p => !p.Dead && !p.HasHediff(disease));
        }

        /// <summary>
        /// The player is told either way. An unwatched citizen catching something and it never being
        /// mentioned would be indistinguishable from nothing happening — and a threat the player is never
        /// told about cannot teach them anything about where to look next, which is the entire loop this
        /// incident exists to feed (see <see cref="IncidentWorker_ManhunterPack.SendLetter"/>'s own remark).
        ///
        /// <para/>"Watched" reads the one signal that already means exactly that:
        /// <see cref="PawnTier.Full"/> IS attention, by <see cref="Pawn_TierTracker"/>'s own design (a citizen
        /// is promoted there because the player is looking at their settlement, and demoted the instant that
        /// stops being true) — so this needs no second lookup of which settlement the god currently has open.
        /// </summary>
        private static void SendLetter(Pawn pawn, HediffDef disease)
        {
            bool watched = pawn.tier.Tier == PawnTier.Full;
            string diseaseLabel = disease.label ?? disease.defName;
            string text = watched
                ? $"{pawn.Label} has come down with {diseaseLabel}."
                : $"Word reaches you late: {pawn.Label} has come down with {diseaseLabel} while your attention was elsewhere.";

            Find.LetterStack?.ReceiveLetter(
                "Disease: " + pawn.Label, text, LetterDefOf.NegativeEvent, new List<string> { pawn.GetUniqueLoadID() });
        }
    }
}
