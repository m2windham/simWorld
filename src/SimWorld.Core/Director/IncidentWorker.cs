using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// Gating and effect for one <see cref="IncidentDef"/> (RimWorld: <c>Verse.IncidentWorker</c>). One instance
    /// per def, created lazily by <see cref="IncidentDef.Worker"/> and reused.
    /// </summary>
    public abstract class IncidentWorker
    {
        public IncidentDef def = null!;

        /// <summary>
        /// True when this incident is allowed to fire right now: earliest day, population and threat-point
        /// floors, refire spacing (skipped when <see cref="IncidentParms.forced"/>), then
        /// <see cref="CanFireNowSub"/>.
        /// </summary>
        public virtual bool CanFireNow(IncidentParms parms)
        {
            if (parms == null) throw new ArgumentNullException(nameof(parms));
            if (def.earliestDay > 0 && GenDate.DaysPassedAt(Find.TickManager.TicksGame) < def.earliestDay) return false;
            if (def.minPopulation > 0 && parms.target.PlayerPawnsForStoryteller.Count() < def.minPopulation) return false;
            if (def.minThreatPoints > 0f && parms.points < def.minThreatPoints) return false;
            if (!EraAllows()) return false;
            if (!parms.forced && FiredTooRecently(parms.target.StoryState)) return false;
            return CanFireNowSub(parms);
        }

        /// <summary>Extra per-incident gating (disease needs a candidate pawn, etc.). Defaults to always allowed.</summary>
        protected virtual bool CanFireNowSub(IncidentParms parms) => true;

        /// <summary>
        /// The civilization's era is within this incident's <see cref="IncidentDef.minEra"/>..<see cref="IncidentDef.maxEra"/>
        /// window. An incident with neither bound is allowed in every era, and so is any incident at all while
        /// no era ladder is loaded — a def-less test fixture must not be silently unable to fire anything.
        /// </summary>
        private bool EraAllows()
        {
            if (def.minEra == null && def.maxEra == null) return true;
            Research.EraDef? era = Find.ResearchManager.CurrentEra;
            if (era == null) return true;
            if (def.minEra != null && era.order < def.minEra.order) return false;
            if (def.maxEra != null && era.order > def.maxEra.order) return false;
            return true;
        }

        private bool FiredTooRecently(StoryState storyState)
        {
            if (def.minRefireDays <= 0f) return false;
            int lastTick = storyState.LastFireTick(def);
            if (lastTick < 0) return false;
            return Find.TickManager.TicksGame < lastTick + (int)(def.minRefireDays * GenDate.TicksPerDay);
        }

        /// <summary>Runs the incident's effect and, on success, records the firing on the target's <see cref="StoryState"/>.</summary>
        public bool TryExecute(IncidentParms parms)
        {
            if (parms == null) throw new ArgumentNullException(nameof(parms));
            bool result = TryExecuteWorker(parms);
            if (result)
            {
                parms.target.StoryState.Notify_IncidentFired(new FiringIncident(def, null, parms));
            }
            return result;
        }

        protected abstract bool TryExecuteWorker(IncidentParms parms);
    }

    /// <summary>
    /// Stand-in for incidents whose real behaviour belongs to a system that doesn't exist yet (trade caravans,
    /// visitors, weather, quests…). Always succeeds, so the chronicle still records that it fired; whichever
    /// system eventually implements the incident replaces this <c>workerClass</c> with its own.
    /// </summary>
    public class IncidentWorker_Placeholder : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms) => true;
    }

    /// <summary>Gives a random candidate pawn (alive, not already sick with it) <see cref="IncidentDef.diseaseIncident"/>.</summary>
    public sealed class IncidentWorker_Disease : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) => CandidatePawns(parms).Any();

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (def.diseaseIncident == null) return false;
            List<Pawn> candidates = CandidatePawns(parms).ToList();
            if (candidates.Count == 0) return false;
            Pawn pawn = candidates[Rand.Range(0, candidates.Count)];
            pawn.health.AddHediff(def.diseaseIncident);
            return true;
        }

        private IEnumerable<Pawn> CandidatePawns(IncidentParms parms)
        {
            HediffDef? disease = def.diseaseIncident;
            if (disease == null) return Enumerable.Empty<Pawn>();
            return parms.target.PlayerPawnsForStoryteller.Where(p => !p.Dead && !p.HasHediff(disease));
        }
    }

    /// <summary>
    /// Raids and other point-scaled threats. Squad composition and tactic selection from
    /// <see cref="IncidentParms.points"/> and tech level land with the Factions/Combat systems; until then this
    /// only validates that there are points to spend so the chronicle can note a threat was due.
    /// </summary>
    public sealed class IncidentWorker_ThreatEvent : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms) => parms.points > 0f;
    }

    /// <summary>Placeholder until pawn generation exists: records that a wanderer would have joined the civilization.</summary>
    public sealed class IncidentWorker_WandererJoin : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms) => true;
    }
}
