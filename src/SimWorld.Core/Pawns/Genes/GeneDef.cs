using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Work;

namespace SimWorld.Pawns.Genes
{
    /// <summary>
    /// A single Biotech-style gene (RimWorld: <c>RimWorld.GeneDef</c>), trimmed to the fields that move a
    /// number anywhere in this port: stat and capacity effects reuse the same seams a trait or a hediff stage
    /// already bends (<see cref="StatWorker"/>, <see cref="PawnCapacityUtility"/>), <see cref="disabledWorkTags"/>
    /// reuses <see cref="Pawn.CombinedDisabledWorkTags"/> the same way <see cref="TraitDef.disabledWorkTags"/>
    /// does, and <see cref="biostatMet"/> reuses <see cref="Pawn.HungerRate"/>. RimWorld's archite genes
    /// (<c>biostatArc</c>, gated behind a research/economy system this port does not have) are not ported.
    /// </summary>
    public class GeneDef : Def
    {
        /// <summary>Flat additions to a pawn's stats while this gene is active (RimWorld: <c>GeneDef.statOffsets</c>).</summary>
        public List<StatModifier>? statOffsets;

        /// <summary>Multiplicative stat adjustments while this gene is active (RimWorld: <c>GeneDef.statFactors</c>).</summary>
        public List<StatModifier>? statFactors;

        /// <summary>Health-capacity adjustments — the same shape a <c>HediffStage</c> already carries (RimWorld: <c>GeneDef.capMods</c>).</summary>
        public List<PawnCapacityModifier>? capMods;

        /// <summary>Work this gene bars outright, OR'ed into <see cref="Pawn.CombinedDisabledWorkTags"/> the way
        /// a trait's own <see cref="TraitDef.disabledWorkTags"/> already is (RimWorld: several gene subclasses
        /// each carry their own version of this; collapsed to one flags field here the way <c>TraitDef</c>
        /// already does it in this port, rather than porting a subclass hierarchy for one flags value).</summary>
        public WorkTags disabledWorkTags = WorkTags.None;

        /// <summary>Genetic complexity this gene costs (RimWorld: <c>GeneDef.biostatCpx</c>) — the budget a
        /// xenotype's gene list is meant to spend against. Recorded as data only: nothing in this port enforces
        /// it as a hard cap yet (no gene-editing UI/economy exists to spend it against), so a
        /// <see cref="XenotypeDef"/> here is not validated against it.</summary>
        public int biostatCpx;

        /// <summary>
        /// Metabolism this gene costs (positive) or saves (negative) — RimWorld: <c>GeneDef.biostatMet</c>.
        /// Summed across a pawn's active genes by <see cref="Pawn_GeneTracker.MetabolismTotal"/> and folded into
        /// <see cref="Pawn.HungerRate"/> via <see cref="GeneTuning.HungerRateFactorFromMetabolism"/>. RimWorld's
        /// own metabolism-to-hunger curve was not available to source in this sandbox (see <c>GeneTuning</c>'s
        /// own doc) — the conversion here is this port's own, defensible approximation.
        /// </summary>
        public int biostatMet;

        /// <summary>
        /// Years added to (positive) or taken from (negative) a pawn's hidden lifespan budget the moment
        /// <see cref="Generation.PawnGenerator"/> applies this gene (SimWorld's own field). RimWorld's closest
        /// analogue — the Deathless gene's revive-on-death behaviour — has no equivalent in this port's
        /// mortality model, which is a single hidden budget (<see cref="Pawn_AgeTracker"/>) rather than an
        /// event to intercept on death; this is the defensible translation of "this gene changes how long you
        /// live" onto the seam that already exists (<see cref="Pawn_AgeTracker.AdjustLifespan"/>).
        /// </summary>
        public float lifespanBonusYears;

        /// <summary>Genes that cannot coexist with this one — a shared tag means "keep only one" (RimWorld:
        /// <c>GeneDef.exclusionTags</c>), same mechanism <see cref="TraitDef.exclusionTags"/> already uses; see
        /// <see cref="Pawn_GeneTracker.ActiveGenesListForReading"/> for the tie-break rule.</summary>
        public List<string>? exclusionTags;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (biostatCpx < 0) yield return "biostatCpx must not be negative.";
        }
    }
}
