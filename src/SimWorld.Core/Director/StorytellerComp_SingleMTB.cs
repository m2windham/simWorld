using System.Collections.Generic;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>Tunables for <see cref="StorytellerComp_SingleMTB"/> (RimWorld: <c>Verse.StorytellerCompProperties_SingleMTB</c>).</summary>
    public sealed class StorytellerCompProperties_SingleMTB : StorytellerCompProperties
    {
        public float mtbDays = 6f;
        public IncidentDef incident = null!;

        public StorytellerCompProperties_SingleMTB() : base(typeof(StorytellerComp_SingleMTB))
        {
        }
    }

    /// <summary>Rolls a plain mean-time-between check every interval for one fixed incident (RimWorld: <c>Verse.StorytellerComp_SingleMTB</c>).</summary>
    public sealed class StorytellerComp_SingleMTB : StorytellerComp
    {
        private StorytellerCompProperties_SingleMTB Props => (StorytellerCompProperties_SingleMTB)props;

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            if (!Rand.Current.MTBEventOccurs(Props.mtbDays, GenDate.TicksPerDay, Storyteller.IncidentCycleLengthTicks)) yield break;

            IncidentParms parms = GenerateParms(Props.incident.category, target);
            if (!Props.incident.Worker.CanFireNow(parms)) yield break;
            yield return new FiringIncident(Props.incident, this, parms);
        }
    }
}
