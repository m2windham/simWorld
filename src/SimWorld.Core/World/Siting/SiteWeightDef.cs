using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Research;

namespace SimWorld.World.Siting
{
    /// <summary>One deposit category's contribution to a <see cref="SiteWeightDef"/>'s advantage sum.</summary>
    public class DepositWeight
    {
        public DepositDef deposit = null!;
        public float weight;
    }

    /// <summary>
    /// Era-keyed advantage weights for <see cref="SiteScorer"/> (spec §5b.2: "the advantage weights are
    /// era-dependent... a neolithic founding reads water, game and flint and does not care about
    /// defensibility; an iron-age one reads the ford and the ridge; an industrial one reads coal and
    /// navigable water"). Keyed off the same <see cref="EraDef"/> ladder <c>Research</c> already authors
    /// (<c>ResearchProjectDef.era</c>, <c>Data/Core/Defs/EraDefs/Eras.xml</c>), rather than a parallel era
    /// concept invented for site scoring.
    /// </summary>
    public class SiteWeightDef : Def
    {
        public EraDef era = null!;

        public List<DepositWeight> depositWeights = new List<DepositWeight>();

        /// <summary>Weight on how many cheap trade routes would pass through the tile
        /// (<see cref="TradePositionScorer"/>); RimWorld has no equivalent to source this from.</summary>
        public float tradePositionWeight;

        public float WeightFor(DepositDef deposit)
        {
            for (int i = 0; i < depositWeights.Count; i++)
            {
                if (depositWeights[i].deposit == deposit) return depositWeights[i].weight;
            }
            return 0f;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (era == null) yield return "era is required.";
        }

        /// <summary>
        /// The loaded <see cref="SiteWeightDef"/> for <paramref name="era"/> — content authors exactly one
        /// per <see cref="EraDef"/> (see <c>Data/Core/Defs/SiteWeightDefs/SiteWeights.xml</c>). Falls back to
        /// the nearest era at or below it by <see cref="EraDef.order"/> so a caller keyed off a partially
        /// authored ladder still gets a usable weight set rather than null; null only when no weight def is
        /// loaded at all. Used by anything that sites a settlement against "the current era" rather than a
        /// player-chosen one — <c>EmergenceManager</c>, so far.
        /// </summary>
        public static SiteWeightDef? ForEra(EraDef? era)
        {
            if (era == null) return null;
            IReadOnlyList<SiteWeightDef> all = DefDatabase<SiteWeightDef>.AllDefsListForReading;
            SiteWeightDef? exact = all.FirstOrDefault(w => w.era == era);
            if (exact != null) return exact;
            return all.Where(w => w.era != null && w.era.order <= era.order)
                .OrderByDescending(w => w.era!.order)
                .FirstOrDefault();
        }
    }
}
