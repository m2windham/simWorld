using System.Collections.Generic;
using System.Linq;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>Tunables for <see cref="StorytellerComp_Disease"/> (RimWorld: <c>Verse.StorytellerCompProperties_Disease</c>).</summary>
    public sealed class StorytellerCompProperties_Disease : StorytellerCompProperties
    {
        public IncidentCategoryDef category = null!;

        /// <summary>
        /// RimWorld reads this from the map's biome (<c>BiomeDef.diseaseMtbDays</c>); SimWorld has no biomes yet,
        /// so it is a flat per-difficulty-scaled value here instead (documented deviation).
        /// </summary>
        public float baseMtbDays = 10f;

        public StorytellerCompProperties_Disease() : base(typeof(StorytellerComp_Disease))
        {
        }
    }

    /// <summary>Rolls a mean-time-between check for disease every interval, scaled by <see cref="DifficultyDef.diseaseIntervalFactor"/>.</summary>
    public sealed class StorytellerComp_Disease : StorytellerComp
    {
        private StorytellerCompProperties_Disease Props => (StorytellerCompProperties_Disease)props;

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            float mtb = Props.baseMtbDays * (Find.Storyteller.difficulty?.diseaseIntervalFactor ?? 1f);
            if (!Rand.Current.MTBEventOccurs(mtb, GenDate.TicksPerDay, Storyteller.IncidentCycleLengthTicks)) yield break;

            IncidentParms parms = GenerateParms(Props.category, target);
            List<IncidentDef> usable = UsableIncidentsInCategory(Props.category, parms).ToList();
            if (usable.Count == 0) yield break;
            if (!GenCollection.TryRandomElementByWeight(usable, d => IncidentChanceFinal(d, target), Rand.Current, out IncidentDef picked)) yield break;

            yield return new FiringIncident(picked, this, parms);
        }
    }
}
