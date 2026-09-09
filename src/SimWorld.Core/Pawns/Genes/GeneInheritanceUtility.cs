using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Sim;

namespace SimWorld.Pawns.Genes
{
    /// <summary>
    /// Turns a newborn's two parents' endogenes into the newborn's own (RimWorld: <c>Pawn_GeneTracker</c>'s own
    /// two-parent inheritance path at birth). The exact per-gene random-selection rule RimWorld uses was not
    /// available to source in this sandbox — no decompiled source, no network access. This is SimWorld's own,
    /// defensible stand-in, chosen for one property that matters for a civilization sim more than any exact
    /// curve does: shared ancestry has to show. The rule:
    /// <list type="bullet">
    /// <item>A gene carried by <b>both</b> parents is inherited for certain — a trait that breeds true does not
    /// need a coin flip to keep breeding true.</item>
    /// <item>A gene carried by only <b>one</b> parent is inherited with <see cref="GeneTuning.SingleParentInheritanceChance"/>
    /// (50%), one independent roll per such gene — so siblings from the same couple visibly diverge in which
    /// single-parent genes they pick up, the way real pedigree does.</item>
    /// <item><b>Xenogenes never pass down.</b> Only <see cref="Pawn_GeneTracker.Endogenes"/> are read from
    /// either parent — this part the port <i>is</i> confident it has right: RimWorld's own xenogenes are an
    /// acquired trait (a xenogerm implant), not germline, so a child is generated from the germline alone.</item>
    /// </list>
    /// Every roll is made in a fixed order (parents' shared genes, then single-parent genes, both lists sorted
    /// by <c>defName</c>) rather than in dictionary/set iteration order, so the exact same two parents under
    /// the same seeded <see cref="Rand"/> stream always produce the exact same child — the property
    /// <c>GenesTests</c> pins instead of any literal gene list.
    /// </summary>
    public static class GeneInheritanceUtility
    {
        /// <summary>Applies the rule above to <paramref name="child"/>, reading <paramref name="parentA"/> and
        /// <paramref name="parentB"/>'s current endogenes. Safe to call on a child that already has genes (e.g.
        /// from a <see cref="Generation.PawnGenerationRequest.Xenotype"/>) — inherited genes are added
        /// alongside them; a duplicate <see cref="GeneDef"/> already present on the child is skipped rather than
        /// added twice. Consumes no <see cref="Rand"/> calls at all when neither parent carries any endogene
        /// (the common case for a population nobody has ever assigned a xenotype to), so calling this
        /// unconditionally on every birth does not perturb a fixed-seed run that never touches genes.</summary>
        public static void InheritEndogenesFrom(Pawn child, Pawn parentA, Pawn parentB)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            if (parentA == null) throw new ArgumentNullException(nameof(parentA));
            if (parentB == null) throw new ArgumentNullException(nameof(parentB));

            List<GeneDef> aGenes = parentA.genes.Endogenes.Select(g => g.def).Distinct().ToList();
            List<GeneDef> bGenes = parentB.genes.Endogenes.Select(g => g.def).Distinct().ToList();
            if (aGenes.Count == 0 && bGenes.Count == 0) return;

            List<GeneDef> shared = aGenes.Where(bGenes.Contains)
                .OrderBy(g => g.defName, StringComparer.Ordinal)
                .ToList();
            List<GeneDef> singleParent = aGenes.Where(g => !bGenes.Contains(g))
                .Concat(bGenes.Where(g => !aGenes.Contains(g)))
                .OrderBy(g => g.defName, StringComparer.Ordinal)
                .ToList();

            foreach (GeneDef gene in shared)
            {
                if (!child.genes.HasGene(gene)) child.genes.AddGene(gene, xenogene: false);
            }
            foreach (GeneDef gene in singleParent)
            {
                if (Rand.Chance(GeneTuning.SingleParentInheritanceChance) && !child.genes.HasGene(gene))
                {
                    child.genes.AddGene(gene, xenogene: false);
                }
            }
        }
    }
}
