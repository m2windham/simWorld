using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;

namespace SimWorld.Director
{
    /// <summary>
    /// Grouping for incidents (RimWorld: <c>Verse.IncidentCategoryDef</c>) — ThreatSmall, ThreatBig, Misc,
    /// FactionArrival, and so on. Storyteller comps pick a category, then an <see cref="IncidentDef"/> within it.
    /// </summary>
    public class IncidentCategoryDef : Def
    {
        /// <summary>Reserved, unused: RimWorld's IncidentCategoryDef itself carries only defName/label/description;
        /// per-category refire spacing lives on <see cref="IncidentDef.minRefireDays"/> instead.</summary>
        public float refireDays;
    }

    /// <summary>
    /// One thing that can happen to a target (RimWorld: <c>Verse.IncidentDef</c>): a raid, a disease, a trade
    /// caravan, weather. Gating (<see cref="IncidentWorker.CanFireNow"/>) and effect
    /// (<see cref="IncidentWorker.TryExecuteWorker"/>) both live on the lazily-created <see cref="Worker"/>.
    /// </summary>
    public class IncidentDef : Def
    {
        public IncidentCategoryDef category = null!;

        /// <summary>Which kinds of target this incident may fire against; empty/null means "any".</summary>
        public List<IncidentTargetTagDef>? targetTags;

        public Type workerClass = typeof(IncidentWorker_Placeholder);

        public float baseChance = 1f;

        /// <summary>Days passed before this incident may ever fire (RimWorld's "not before day N" gate).</summary>
        public int earliestDay;

        public int minPopulation;

        public float minThreatPoints;

        /// <summary>Minimum days between two firings of this exact def; 0 disables the check.</summary>
        public float minRefireDays;

        /// <summary>Whether <c>IncidentParms.points</c> should scale this incident's severity (raids do; a
        /// single-effect incident like weather does not).</summary>
        public bool pointsScaleable;

        /// <summary>Hediff <see cref="IncidentWorker_Disease"/> gives to a candidate pawn.</summary>
        public HediffDef? diseaseIncident;

        private IncidentWorker? workerInt;

        public IncidentWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (IncidentWorker)Activator.CreateInstance(workerClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override void ClearCachedData()
        {
            base.ClearCachedData();
            workerInt = null;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (category == null) yield return "category is required.";
            if (workerClass == null || !typeof(IncidentWorker).IsAssignableFrom(workerClass)) yield return "workerClass must derive from IncidentWorker.";
            if (typeof(IncidentWorker_Disease).IsAssignableFrom(workerClass) && diseaseIncident == null) yield return "a disease incident needs diseaseIncident set.";
        }
    }

    /// <summary>
    /// Inputs to one incident firing (RimWorld: <c>Verse.IncidentParms</c>): who it targets, how many threat
    /// points to spend, and whether normal gating (refire spacing, etc.) should be skipped.
    /// </summary>
    public sealed class IncidentParms
    {
        public IIncidentTarget target = null!;

        public float points;

        public bool forced;

        /// <summary>Placeholder until the Factions system lands (RimWorld's originating/target faction).</summary>
        public object? faction;
    }

    /// <summary>An incident selected to fire, waiting to be executed or queued (RimWorld: <c>Verse.FiringIncident</c>).</summary>
    public sealed class FiringIncident
    {
        public IncidentDef def;

        public IncidentParms parms;

        /// <summary>The comp that picked this incident, if any (queued/forced incidents may have none).</summary>
        public StorytellerComp? source;

        public FiringIncident(IncidentDef def, StorytellerComp? source, IncidentParms parms)
        {
            this.def = def ?? throw new ArgumentNullException(nameof(def));
            this.parms = parms ?? throw new ArgumentNullException(nameof(parms));
            this.source = source;
        }
    }
}
