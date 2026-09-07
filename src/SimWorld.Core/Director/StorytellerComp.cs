using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>Data half of one storyteller behaviour (RimWorld: <c>Verse.StorytellerCompProperties</c>).</summary>
    public abstract class StorytellerCompProperties
    {
        public Type compClass = null!;

        protected StorytellerCompProperties(Type compClass)
        {
            this.compClass = compClass;
        }

        public virtual void ResolveReferences(StorytellerDef parent)
        {
        }

        public virtual IEnumerable<string> ConfigErrors(StorytellerDef parent)
        {
            if (compClass == null || !typeof(StorytellerComp).IsAssignableFrom(compClass))
            {
                yield return GetType().Name + " has an invalid compClass.";
            }
        }
    }

    /// <summary>
    /// One storyteller behaviour (RimWorld: <c>Verse.StorytellerComp</c>): decides, every interval tick, whether
    /// to fire zero or more incidents against a target. Stateless between calls — everything it needs to decide
    /// comes from <see cref="props"/>, the target, and <see cref="Sim.Find"/>.
    /// </summary>
    public abstract class StorytellerComp
    {
        public StorytellerCompProperties props = null!;

        /// <summary>Called once per <see cref="Storyteller.IncidentCycleLengthTicks"/> interval; yields zero or more incidents to fire now.</summary>
        public abstract IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target);

        protected static IncidentParms GenerateParms(IncidentCategoryDef category, IIncidentTarget target) =>
            StorytellerUtility.DefaultParmsNow(category, target);

        /// <summary>Every <see cref="IncidentDef"/> of <paramref name="category"/> that can fire now against <paramref name="parms"/>'s target.</summary>
        protected static IEnumerable<IncidentDef> UsableIncidentsInCategory(IncidentCategoryDef category, IncidentParms parms)
        {
            foreach (IncidentDef def in DefDatabase<IncidentDef>.AllDefsListForReading)
            {
                if (def.category != category) continue;
                if (!TargetTagsMatch(def, parms.target)) continue;
                if (!def.Worker.CanFireNow(parms)) continue;
                yield return def;
            }
        }

        /// <summary>
        /// Final selection weight for a def: its base chance, scaled for threat categories by the storyteller's
        /// population-intent factor (RimWorld: <c>RimWorld.StorytellerUtilityPopulation.PopulationIntent</c>).
        /// </summary>
        protected static float IncidentChanceFinal(IncidentDef incidentDef, IIncidentTarget target)
        {
            float chance = incidentDef.baseChance;
            if (incidentDef.category == IncidentCategoryDefOf.ThreatBig || incidentDef.category == IncidentCategoryDefOf.ThreatSmall)
            {
                chance *= StorytellerUtilityPopulation.PopulationIntentFactor(Find.Storyteller.def, target.PlayerPawnsForStoryteller.Count());
            }
            return Math.Max(chance, 0f);
        }

        private static bool TargetTagsMatch(IncidentDef def, IIncidentTarget target)
        {
            if (def.targetTags == null || def.targetTags.Count == 0) return true;
            foreach (IncidentTargetTagDef tag in target.IncidentTargetTags())
            {
                if (def.targetTags.Contains(tag)) return true;
            }
            return false;
        }
    }
}
