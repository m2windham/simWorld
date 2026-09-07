using System.Collections.Generic;
using System.Linq;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>Tunables for <see cref="StorytellerComp_OnOffCycle"/> (RimWorld: <c>Verse.StorytellerCompProperties_OnOffCycle</c>).</summary>
    public sealed class StorytellerCompProperties_OnOffCycle : StorytellerCompProperties
    {
        public float onDays = 6f;
        public float offDays = 4.5f;

        /// <summary>Days passed before this cycle starts considering incidents at all.</summary>
        public float minDaysPassed;

        /// <summary>Floor on the average days between two incidents from this comp within one on-phase.</summary>
        public float minSpacingDays;

        public FloatRange numIncidentsRange = new FloatRange(1f, 1f);

        /// <summary>Scales down the incident rate early on; null means no scaling (RimWorld's early-game ramp-up).</summary>
        public SimpleCurve? acceptFractionByDaysPassedCurve;

        /// <summary>
        /// Reserved: RimWorld additionally scales accept chance down for unusually large threat-point rolls.
        /// Loaded for content fidelity but not applied here (documented deviation).
        /// </summary>
        public SimpleCurve? acceptPercentFactorPerThreatPointsCurve;

        /// <summary>Fires exactly this def every time, skipping category selection, when set.</summary>
        public IncidentDef? incident;

        /// <summary>Picked from by weight (<see cref="StorytellerComp.IncidentChanceFinal"/>) when <see cref="incident"/> is null.</summary>
        public IncidentCategoryDef? category;

        public StorytellerCompProperties_OnOffCycle() : base(typeof(StorytellerComp_OnOffCycle))
        {
        }

        public override IEnumerable<string> ConfigErrors(StorytellerDef parent)
        {
            foreach (string error in base.ConfigErrors(parent)) yield return error;
            if (incident == null && category == null) yield return "OnOffCycle needs either incident or category.";
        }
    }

    /// <summary>
    /// Fires incidents only during a recurring "on" phase of an on/off day cycle (RimWorld:
    /// <c>Verse.StorytellerComp_OnOffCycle</c>) — the shape behind Cassandra's and Phoebe's threat cadence.
    /// </summary>
    public class StorytellerComp_OnOffCycle : StorytellerComp
    {
        protected StorytellerCompProperties_OnOffCycle Props => (StorytellerCompProperties_OnOffCycle)props;

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            int count = IncidentCycleUtility.IncidentCountThisInterval(
                target, Rand.Current, Props.minDaysPassed, Props.onDays, Props.offDays, Props.minSpacingDays,
                Props.numIncidentsRange.min, Props.numIncidentsRange.max, Props.acceptFractionByDaysPassedCurve);

            for (int i = 0; i < count; i++)
            {
                IncidentDef? explicitIncident = Props.incident;
                IncidentCategoryDef category = explicitIncident?.category ?? Props.category!;
                IncidentParms parms = GenerateParms(category, target);

                IncidentDef picked;
                if (explicitIncident == null)
                {
                    List<IncidentDef> usable = UsableIncidentsInCategory(category, parms).ToList();
                    if (usable.Count == 0) continue;
                    if (!GenCollection.TryRandomElementByWeight(usable, d => IncidentChanceFinal(d, target), Rand.Current, out picked)) continue;
                }
                else if (!explicitIncident.Worker.CanFireNow(parms))
                {
                    continue;
                }
                else
                {
                    picked = explicitIncident;
                }

                yield return new FiringIncident(picked, this, parms);
            }
        }
    }
}
