using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Pawns.Genes
{
    /// <summary>
    /// A named germline: the set of genes a pawn generated as this xenotype is born with (RimWorld:
    /// <c>RimWorld.XenotypeDef</c>). Applied by <see cref="Generation.PawnGenerator"/> as endogenes — the
    /// natural, inheritable half of <see cref="Pawn_GeneTracker"/> — never as xenogenes; a xenotype describes
    /// what a pawn <i>is</i>, not an implant someone received. RimWorld's own <c>Baseliner</c> (an
    /// unmodified human) is content here too: a <see cref="XenotypeDef"/> with an empty or absent
    /// <see cref="genes"/> list, exactly equivalent to a <see cref="Generation.PawnGenerationRequest"/> that
    /// asks for no xenotype at all.
    /// </summary>
    public class XenotypeDef : Def
    {
        /// <summary>The germline gene template (RimWorld: <c>XenotypeDef.genes</c>). Null/empty means Baseliner.</summary>
        public List<GeneDef>? genes;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (genes == null) yield break;
            var seen = new HashSet<GeneDef>();
            foreach (GeneDef? gene in genes)
            {
                if (gene != null && !seen.Add(gene))
                {
                    yield return "gene '" + gene.defName + "' is listed more than once.";
                }
            }
        }
    }
}
