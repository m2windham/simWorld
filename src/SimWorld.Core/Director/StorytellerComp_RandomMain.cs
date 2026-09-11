using System.Collections.Generic;
using System.Linq;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>One weighted category choice for <see cref="StorytellerCompProperties_RandomMain"/> (RimWorld: <c>RimWorld.IncidentCategoryEntry</c>).</summary>
    public sealed class IncidentCategoryEntry
    {
        public IncidentCategoryDef category = null!;
        public float weight = 1f;
    }

    /// <summary>Tunables for <see cref="StorytellerComp_RandomMain"/> (RimWorld: <c>Verse.StorytellerCompProperties_RandomMain</c>).</summary>
    public sealed class StorytellerCompProperties_RandomMain : StorytellerCompProperties
    {
        public float mtbDays = 1f;
        public List<IncidentCategoryEntry>? categoryWeights;

        /// <summary>If no ThreatBig has fired in this many days, force one instead of the weighted pick (safety net); 0 disables it.</summary>
        public float maxThreatBigIntervalDays;

        public FloatRange randomPointsFactorRange = new FloatRange(1f, 1f);

        /// <summary>Reserved: RimWorld can skip a forced ThreatBig when a raid beacon is active. Not modelled (no map/beacons yet).</summary>
        public bool skipThreatBigIfRaidBeacon;

        public StorytellerCompProperties_RandomMain() : base(typeof(StorytellerComp_RandomMain))
        {
        }

        public override IEnumerable<string> ConfigErrors(StorytellerDef parent)
        {
            foreach (string error in base.ConfigErrors(parent)) yield return error;
            if (categoryWeights == null || categoryWeights.Count == 0) yield return "RandomMain needs at least one category weight.";
        }
    }

    /// <summary>
    /// Rolls a mean-time-between check every interval; on a hit, picks a category by weight and a def within it
    /// by chance (RimWorld: <c>Verse.StorytellerComp_RandomMain</c>) — Randy Random's whole personality.
    /// </summary>
    public sealed class StorytellerComp_RandomMain : StorytellerComp
    {
        private StorytellerCompProperties_RandomMain Props => (StorytellerCompProperties_RandomMain)props;

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            if (Props.categoryWeights == null || Props.categoryWeights.Count == 0) yield break;
            if (!Rand.Current.MTBEventOccurs(Props.mtbDays, GenDate.TicksPerDay, Storyteller.IncidentCycleLengthTicks)) yield break;

            IncidentCategoryDef? category = null;
            if (Props.maxThreatBigIntervalDays > 0f)
            {
                int ticksSinceThreatBig = Find.TickManager.TicksGame - target.StoryState.LastThreatBigTick;
                if (ticksSinceThreatBig > Props.maxThreatBigIntervalDays * GenDate.TicksPerDay)
                {
                    category = IncidentCategoryDefOf.ThreatBig;
                }
            }
            if (category == null)
            {
                if (!GenCollection.TryRandomElementByWeight(Props.categoryWeights, e => e.weight, Rand.Current, out IncidentCategoryEntry entry)) yield break;
                category = entry.category;
            }

            IncidentParms parms = GenerateParms(category, target);
            List<IncidentDef> usable = UsableIncidentsInCategory(category, parms).ToList();
            if (usable.Count == 0) yield break;
            if (!GenCollection.TryRandomElementByWeight(usable, d => IncidentChanceFinal(d, target), Rand.Current, out IncidentDef picked)) yield break;

            // IncidentDef.pointsScaleable, which no line of src/ read until now — and this is the only place
            // in the port that multiplies IncidentParms.points by anything random, so it is the only place
            // the flag can mean what its own doc says ("whether points should scale this incident's
            // severity"). RimWorld gates exactly this multiply on exactly this flag; the port dropped the
            // `if` and jittered every incident Randy picked, scaleable or not.
            //
            // Numerically that changed nothing yet: StorytellerUtility.DefaultParmsNow only gives points to
            // the two threat categories, and the two shipped incidents that carry points (RaidEnemy,
            // ManhunterPack) both declare pointsScaleable — so every multiply that ever reached a non-zero
            // number was one this gate allows. What it did change is the seeded stream: a draw was taken for
            // a weather incident that had nothing to scale, moving every roll after it. Restoring the gate
            // moves those rolls back, which is a deliberate re-pinning and not a free change.
            //
            // Of the two flagged incidents only RaidEnemy has severity to scale today (it spends the points
            // through PawnGroupMakerUtility); ManhunterPack's worker is IncidentWorker_ThreatEvent, which
            // validates the points and does nothing with them, so the flag is honest content waiting on a
            // worker rather than a second live reader.
            if (picked.pointsScaleable) parms.points *= Rand.Range(Props.randomPointsFactorRange);
            yield return new FiringIncident(picked, this, parms);
        }
    }
}
