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

        /// <summary>Floor on the days between two incidents from this comp, within an on-phase and across the
        /// boundary into the next. <see cref="IncidentCycleUtility"/> enforces it: no two incidents are closer.</summary>
        public float minSpacingDays;

        /// <summary>How many incidents one on-phase holds: <c>RoundRandom</c> of a draw from this range, so
        /// <c>1~2</c> is a fair coin between one and two.</summary>
        public FloatRange numIncidentsRange = new FloatRange(1f, 1f);

        /// <summary>Thins each cycle's plan down to this fraction of its incidents, by day; null means no thinning.</summary>
        public SimpleCurve? acceptFractionByDaysPassedCurve;

        /// <summary>
        /// Reserved: RimWorld additionally scales the accepted fraction by the threat points on offer.
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
            if (onDays <= 0f) yield return "onDays must be above zero.";
            if (numIncidentsRange.max <= 0f) yield return "numIncidentsRange is not configured.";
            // RimWorld's own check: a plan whose incidents cannot be kept this far apart inside the on-phase
            // makes IncidentCycleUtility give up and hand the cycle nothing.
            if (minSpacingDays * numIncidentsRange.max > onDays * 0.9f) yield return "minSpacingDays is too high for the most incidents one on-phase can hold.";
        }
    }

    /// <summary>
    /// Fires incidents only during a recurring "on" phase of an on/off day cycle (RimWorld:
    /// <c>Verse.StorytellerComp_OnOffCycle</c>) — the shape behind Cassandra's and Phoebe's threat cadence.
    /// </summary>
    public class StorytellerComp_OnOffCycle : StorytellerComp
    {
        protected StorytellerCompProperties_OnOffCycle Props => (StorytellerCompProperties_OnOffCycle)props;

        /// <summary>This comp's place in the running storyteller's comp list, which is what keeps two cycle
        /// comps on one storyteller from planning the same days. A comp posed by hand in a test, outside any
        /// storyteller's list, is -1.</summary>
        private int IndexInStoryteller()
        {
            IReadOnlyList<StorytellerComp> all = Find.Storyteller.Comps;
            for (int i = 0; i < all.Count; i++)
            {
                if (ReferenceEquals(all[i], this)) return i;
            }
            return -1;
        }

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            float acceptFraction = 1f;
            if (Props.acceptFractionByDaysPassedCurve != null)
            {
                acceptFraction *= Props.acceptFractionByDaysPassedCurve.Evaluate(Find.TickManager.TicksGame / (float)GenDate.TicksPerDay);
            }

            int count = IncidentCycleUtility.IncidentCountThisInterval(
                target, IndexInStoryteller(), Props.minDaysPassed, Props.onDays, Props.offDays,
                Props.minSpacingDays, Props.numIncidentsRange.min, Props.numIncidentsRange.max, acceptFraction);

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
