using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Health
{
    /// <summary>
    /// A drug's chemical identity — what builds tolerance and what an overdose of it looks like as an
    /// addiction (RimWorld: <c>RimWorld.ChemicalDef</c>). Several drugs can share one (this port doesn't need
    /// that yet, but nothing stops content from doing it, same as RimWorld's own Wake-up/Go-juice).
    /// </summary>
    public class ChemicalDef : Def
    {
        /// <summary>Builds with every dose, decays slowly when the pawn abstains (RimWorld: <c>ChemicalDef.toleranceHediff</c>).</summary>
        public HediffDef? toleranceHediff;

        /// <summary>The dependency itself: absent until tolerance crosses <see cref="minToleranceToAddict"/> and
        /// a dose rolls it, present (and worsening between doses) after (RimWorld: <c>ChemicalDef.addictionHediff</c>).</summary>
        public HediffDef? addictionHediff;

        /// <summary>
        /// Tolerance severity at/above which a dose can start the addiction. SimWorld's own number — RimWorld's
        /// real per-chemical addiction curve isn't sourced here; <c>DrugTests</c> pins the trend (no addiction
        /// below the threshold, however unlucky the roll; addiction only ever appears at/above it) rather than
        /// this literal.
        /// </summary>
        public float minToleranceToAddict = 0.15f;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (minToleranceToAddict < 0f || minToleranceToAddict > 1f)
            {
                yield return "minToleranceToAddict must be in [0, 1].";
            }
        }
    }
}
