using System;
using SimWorld.Sim;

namespace SimWorld.Pawns.Genes
{
    /// <summary>
    /// One gene on one pawn (RimWorld: <c>RimWorld.Gene</c> — the runtime half of the def/instance split
    /// RimWorld itself uses; ported for the same reason <c>TraitDef</c>/<c>Trait</c> already split that way in
    /// this codebase: the def is shared content, the instance is per-pawn save state).
    /// </summary>
    public class Gene : IExposable
    {
        public GeneDef def = null!;

        /// <summary>
        /// True for a xenogene — acquired (a xenogerm implant, in RimWorld; this port has no such surgery yet,
        /// so nothing sets this true today, but the field is real so a future implant mechanic has somewhere to
        /// land) and never inherited. False for an endogene — germline, present from birth, and the only half
        /// <see cref="GeneInheritanceUtility"/> ever reads (RimWorld's own rule, and the one part of gene
        /// inheritance this port is confident it has sourced correctly: xenogenes do not pass to children).
        /// </summary>
        public bool xenogene;

        public Gene()
        {
        }

        public Gene(GeneDef def, bool xenogene)
        {
            this.def = def ?? throw new ArgumentNullException(nameof(def));
            this.xenogene = xenogene;
        }

        public void ExposeData()
        {
            GeneDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref xenogene, "xenogene");
        }

        public override string ToString() => (def?.defName ?? "Gene(null)") + (xenogene ? " [xeno]" : " [endo]");
    }
}
