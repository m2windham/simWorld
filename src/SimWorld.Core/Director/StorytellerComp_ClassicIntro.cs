using System.Collections.Generic;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>Tunables for <see cref="StorytellerComp_ClassicIntro"/> (RimWorld: <c>Verse.StorytellerCompProperties_ThreatCycle</c>'s intro use).</summary>
    public sealed class StorytellerCompProperties_ClassicIntro : StorytellerCompProperties
    {
        public IncidentDef incident = null!;

        /// <summary>Day passed on which the intro incident becomes eligible to fire.</summary>
        public int day = 4;

        public StorytellerCompProperties_ClassicIntro() : base(typeof(StorytellerComp_ClassicIntro))
        {
        }
    }

    /// <summary>
    /// Fires one specific incident exactly once, no earlier than a fixed day (RimWorld's "first threat" cold
    /// open). Uses the target's <see cref="StoryState"/> firing history rather than its own state, so it
    /// survives a save without any extra bookkeeping.
    /// </summary>
    public sealed class StorytellerComp_ClassicIntro : StorytellerComp
    {
        private StorytellerCompProperties_ClassicIntro Props => (StorytellerCompProperties_ClassicIntro)props;

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            if (target.StoryState.HasFired(Props.incident)) yield break;
            if (GenDate.DaysPassedAt(Find.TickManager.TicksGame) < Props.day) yield break;

            IncidentParms parms = GenerateParms(Props.incident.category, target);
            if (!Props.incident.Worker.CanFireNow(parms)) yield break;
            yield return new FiringIncident(Props.incident, this, parms);
        }
    }
}
