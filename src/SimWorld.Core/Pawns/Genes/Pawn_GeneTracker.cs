using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Work;

namespace SimWorld.Pawns.Genes
{
    /// <summary>
    /// A pawn's genes (RimWorld: <c>RimWorld.Pawn_GeneTracker</c>): which <see cref="Gene"/>s it carries, split
    /// into endogenes (germline — present from birth, the only half <see cref="GeneInheritanceUtility"/> passes
    /// to children) and xenogenes (acquired — a xenogerm implant, in RimWorld; not modeled as a surgery in this
    /// port yet, so every gene reaching a pawn today arrives as an endogene, through
    /// <see cref="SetXenotype"/> from <see cref="Generation.PawnGenerator"/> or through inheritance at birth,
    /// but the split is real and <see cref="AddGene"/> takes it as a parameter so a future xenogerm mechanic has
    /// somewhere to land without reshaping this tracker).
    /// <para/>
    /// Genes reuse the seams RimWorld itself reuses rather than adding a parallel effect pipeline: stat
    /// offsets/factors join the same list a trait degree or a hediff stage already contributes to
    /// (<see cref="StatWorker.GetValueUnfinalized"/>), capacity modifiers join the same list a hediff
    /// stage's <c>capMods</c> already contributes to (<see cref="PawnCapacityUtility.CalculateCapacityLevel"/>),
    /// disabled work OR's into <see cref="Pawn.CombinedDisabledWorkTags"/> next to <see cref="Pawn_StoryTracker.DisabledWorkTagsBackstoryAndTraits"/>,
    /// and metabolism folds into <see cref="Pawn.HungerRate"/> next to the life-stage/health factors already
    /// there. Nothing above this tracker (and above the three seam call sites) needs to know genes exist.
    /// </summary>
    public class Pawn_GeneTracker : IExposable
    {
        private readonly Pawn pawn;
        private readonly List<Gene> genes = new List<Gene>();

        /// <summary>The named xenotype this pawn's germline was generated from, if any (RimWorld:
        /// <c>Pawn_GeneTracker.Xenotype</c>). Purely descriptive — nothing branches on it besides the Scribe
        /// round trip and a test asserting it landed; a pawn born of two parents' mixed endogenes (see
        /// <see cref="GeneInheritanceUtility"/>) deliberately leaves this null rather than naming either
        /// parent's xenotype, since its actual gene list need not match one exactly.</summary>
        public XenotypeDef? xenotypeDef;

        public Pawn_GeneTracker(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public IReadOnlyList<Gene> GenesListForReading => genes;

        public IEnumerable<Gene> Endogenes
        {
            get
            {
                for (int i = 0; i < genes.Count; i++)
                {
                    if (!genes[i].xenogene) yield return genes[i];
                }
            }
        }

        public IEnumerable<Gene> Xenogenes
        {
            get
            {
                for (int i = 0; i < genes.Count; i++)
                {
                    if (genes[i].xenogene) yield return genes[i];
                }
            }
        }

        /// <summary>
        /// Every gene actually in effect: every stored gene except one an <see cref="GeneDef.exclusionTags"/>
        /// conflict has silenced (RimWorld: <c>Gene.Overridden</c>). A xenogene beats a conflicting endogene —
        /// an implant overwrites the natural gene it replaces; among genes of the same origin, the one added
        /// first wins, for a stable and deterministic result. The overridden gene stays in
        /// <see cref="GenesListForReading"/> (and still saves/loads) — it is silenced, not gone, matching
        /// RimWorld's own "remove the gene that is overriding it and the old one reactivates" behaviour.
        /// Recomputed on demand rather than cached: a pawn's gene list is tiny (single digits) and changes only
        /// at generation, birth or (eventually) surgery, nowhere near the per-tick reads
        /// <see cref="PawnCapacitiesHandler"/> caches against.
        /// </summary>
        public IReadOnlyList<Gene> ActiveGenesListForReading
        {
            get
            {
                // Two fast paths matter a lot more than they look: this property is read from
                // Stats.StatWorker.GetValueUnfinalized, which pather.PatherTick calls every single tick for
                // MoveSpeed — a per-tick, per-pawn hot path this codebase has an allocation-free regression
                // test for (AITests.Steady_state_pather_ticking_allocates_nothing). The overwhelming common
                // case today is zero genes at all; a shared empty array costs nothing. The next most common
                // case, once content carries genes, is genes with no exclusionTags conflict to resolve at
                // all — returning the backing list directly (never mutated while a caller might be reading
                // it — genes change only at generation, birth, or a future surgery, none of which happen
                // mid-stat-read) skips the copy entirely rather than allocating a new list every read.
                if (genes.Count == 0) return Array.Empty<Gene>();
                bool anyExclusionTags = false;
                for (int i = 0; i < genes.Count; i++)
                {
                    if (genes[i].def.exclusionTags != null && genes[i].def.exclusionTags!.Count > 0)
                    {
                        anyExclusionTags = true;
                        break;
                    }
                }
                if (!anyExclusionTags) return genes;

                var result = new List<Gene>(genes.Count);
                HashSet<string>? claimedTags = null;
                void TryAdd(Gene gene)
                {
                    List<string>? tags = gene.def.exclusionTags;
                    if (tags != null && claimedTags != null)
                    {
                        for (int i = 0; i < tags.Count; i++)
                        {
                            if (claimedTags.Contains(tags[i])) return;
                        }
                    }
                    result.Add(gene);
                    if (tags != null && tags.Count > 0)
                    {
                        claimedTags ??= new HashSet<string>();
                        claimedTags.UnionWith(tags);
                    }
                }
                for (int i = 0; i < genes.Count; i++)
                {
                    if (genes[i].xenogene) TryAdd(genes[i]);
                }
                for (int i = 0; i < genes.Count; i++)
                {
                    if (!genes[i].xenogene) TryAdd(genes[i]);
                }
                return result;
            }
        }

        public bool HasGene(GeneDef def)
        {
            for (int i = 0; i < genes.Count; i++)
            {
                if (genes[i].def == def) return true;
            }
            return false;
        }

        public bool HasActiveGene(GeneDef def)
        {
            IReadOnlyList<Gene> active = ActiveGenesListForReading;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].def == def) return true;
            }
            return false;
        }

        /// <summary>Adds one gene instance. Does not itself apply <see cref="GeneDef.lifespanBonusYears"/> —
        /// that one-time effect is <see cref="Generation.PawnGenerator"/>'s job (see <see cref="SetXenotype"/>'s
        /// doc), since adding a gene here also happens at inheritance, where re-granting a lifespan bonus every
        /// birth for a gene merely passed down would double-count it against the parent's own.</summary>
        public Gene AddGene(GeneDef def, bool xenogene)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            var gene = new Gene(def, xenogene);
            genes.Add(gene);
            pawn.health?.capacities?.Notify_CapacityLevelsDirty();
            pawn.Notify_DisabledWorkTagsChanged();
            return gene;
        }

        public bool RemoveGene(Gene gene)
        {
            bool removed = genes.Remove(gene);
            if (removed)
            {
                pawn.health?.capacities?.Notify_CapacityLevelsDirty();
                pawn.Notify_DisabledWorkTagsChanged();
            }
            return removed;
        }

        /// <summary>
        /// Assigns this pawn's germline from a named xenotype (RimWorld: pawn generation sets
        /// <c>Pawn_GeneTracker.Xenotype</c> directly, bypassing the xenogerm-implant pathway — see this class's
        /// own doc on why every gene lands as an endogene here). Every gene in <paramref name="xenotype"/>'s
        /// list is added as an endogene and <see cref="GeneDef.lifespanBonusYears"/> style effects are left for the
        /// caller (<see cref="Generation.PawnGenerator"/>) to apply, since only generation — not inheritance —
        /// should grant them. Clears any existing endogenes first so calling this twice does not double a
        /// pawn's genes.
        /// </summary>
        public void SetXenotype(XenotypeDef xenotype)
        {
            if (xenotype == null) throw new ArgumentNullException(nameof(xenotype));
            genes.RemoveAll(g => !g.xenogene);
            xenotypeDef = xenotype;
            pawn.Notify_DisabledWorkTagsChanged();
            if (xenotype.genes == null) return;
            foreach (GeneDef gene in xenotype.genes)
            {
                AddGene(gene, xenogene: false);
            }
        }

        /// <summary>Every active gene's <see cref="GeneDef.disabledWorkTags"/>, OR'ed together — reads exactly
        /// like <see cref="Pawn_StoryTracker.DisabledWorkTagsBackstoryAndTraits"/>, joined into
        /// <see cref="Pawn.CombinedDisabledWorkTags"/> at that same seam.</summary>
        public WorkTags CombinedDisabledWorkTags
        {
            get
            {
                WorkTags combined = WorkTags.None;
                IReadOnlyList<Gene> active = ActiveGenesListForReading;
                for (int i = 0; i < active.Count; i++)
                {
                    combined |= active[i].def.disabledWorkTags;
                }
                return combined;
            }
        }

        /// <summary>Sum of every active gene's <see cref="GeneDef.biostatMet"/> — RimWorld: the pawn's total
        /// genetic metabolism. Read by <see cref="Pawn.HungerRate"/> through <see cref="GeneTuning.HungerRateFactorFromMetabolism"/>.</summary>
        public int MetabolismTotal
        {
            get
            {
                int total = 0;
                IReadOnlyList<Gene> active = ActiveGenesListForReading;
                for (int i = 0; i < active.Count; i++)
                {
                    total += active[i].def.biostatMet;
                }
                return total;
            }
        }

        /// <summary>Stat offset total across every active gene for <paramref name="stat"/> — the value
        /// <see cref="StatWorker"/> adds next to the trait and hediff-stage offsets it already sums.</summary>
        public float StatOffsetTotal(StatDef stat)
        {
            float total = 0f;
            IReadOnlyList<Gene> active = ActiveGenesListForReading;
            for (int i = 0; i < active.Count; i++)
            {
                total += active[i].def.statOffsets.GetStatOffsetFromList(stat);
            }
            return total;
        }

        /// <summary>Stat factor across every active gene for <paramref name="stat"/>, multiplied together —
        /// the value <see cref="StatWorker"/> multiplies in next to the trait and hediff-stage factors.</summary>
        public float StatFactorTotal(StatDef stat)
        {
            float factor = 1f;
            IReadOnlyList<Gene> active = ActiveGenesListForReading;
            for (int i = 0; i < active.Count; i++)
            {
                factor *= active[i].def.statFactors.GetStatFactorFromList(stat);
            }
            return factor;
        }

        /// <summary>Every active gene's <see cref="GeneDef.capMods"/>, flattened — the list
        /// <see cref="PawnCapacityUtility.CalculateCapacityLevel"/> folds in next to a hediff stage's own.</summary>
        public IEnumerable<PawnCapacityModifier> CapMods
        {
            get
            {
                IReadOnlyList<Gene> active = ActiveGenesListForReading;
                for (int i = 0; i < active.Count; i++)
                {
                    List<PawnCapacityModifier>? mods = active[i].def.capMods;
                    if (mods == null) continue;
                    for (int j = 0; j < mods.Count; j++)
                    {
                        yield return mods[j];
                    }
                }
            }
        }

        public void ExposeData()
        {
            List<Gene>? list = genes;
            Scribe_Collections.Look(ref list, "genes", LookMode.Deep);
            genes.Clear();
            if (list != null) genes.AddRange(list);
            XenotypeDef? x = xenotypeDef;
            Scribe_Defs.Look(ref x, "xenotypeDef");
            xenotypeDef = x;
        }
    }
}
