using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Director
{
    /// <summary>
    /// One narrator personality (RimWorld: <c>Verse.StorytellerDef</c> — Cassandra, Phoebe, Randy). Behaviour is
    /// entirely composed from <see cref="comps"/>; the def itself only carries the curves every comp shares and
    /// the narrator's identity.
    /// </summary>
    public class StorytellerDef : Def
    {
        /// <summary>Display ordering only (RimWorld's storyteller-select screen); has no gameplay effect.</summary>
        public int listOrder;

        public List<StorytellerCompProperties>? comps;

        /// <summary>
        /// Scales threat chance/severity by current population (RimWorld: fewer raids while the colony is
        /// smaller than the storyteller "wants"). Default is a flat 1 (no scaling).
        /// </summary>
        public SimpleCurve populationIntentFactorFromPopCurve = new SimpleCurve { { 0f, 1f } };

        /// <summary>
        /// RimWorld smooths the population-intent read over this many days so a temporary head-count dip or
        /// spike doesn't snap the threat rate instantly. Loaded for content fidelity but not applied — see
        /// <see cref="StorytellerUtilityPopulation"/> (documented deviation).
        /// </summary>
        public SimpleCurve? populationIntentFactorFromPopAdaptDays;

        /// <summary>Scales threat points by days passed (long games escalate). Default is a flat 1 (no scaling).</summary>
        public SimpleCurve pointsFactorFromDaysPassed = new SimpleCurve { { 0f, 1f } };

        /// <summary>
        /// SimWorld hook: the narrator's in-fiction name/voice (e.g. read out over a chronicle entry). Naming is
        /// still open — "Scribe", "Historian" and "The Chronicle" are all live candidates; the code module stays
        /// <c>Director</c> regardless of what this settles on.
        /// </summary>
        public string? persona;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (comps != null)
            {
                foreach (StorytellerCompProperties comp in comps)
                {
                    foreach (string error in comp.ConfigErrors(this)) yield return error;
                }
            }
        }
    }
}
