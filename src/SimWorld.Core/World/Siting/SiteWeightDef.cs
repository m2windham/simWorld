using System.Collections.Generic;
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
    }
}
