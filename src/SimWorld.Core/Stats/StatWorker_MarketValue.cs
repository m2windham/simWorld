using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Pawns;

namespace SimWorld.Stats
{
    /// <summary>
    /// The <c>MarketValue</c> stat's own worker (RimWorld: <c>RimWorld.StatWorker_MarketValue</c>), selected by
    /// <c>Stats_Economy.xml</c>'s <c>workerClass</c>. It does the two things the generic pipeline cannot:
    /// <list type="bullet">
    /// <item><b>A pawn is priced as an individual.</b> The race's base value from its <c>statBases</c> (read
    /// with no pawn, so none of the pawn's trait or hediff factors apply — RimWorld asks for a
    /// <c>StatRequest.For(def, stuff)</c> here too), times <see cref="PriceUtility.PawnQualityPriceFactor"/>.</item>
    /// <item><b>A def that sets no <c>MarketValue</c> is worth what it costs to make</b>
    /// (<see cref="CalculatedBaseMarketValue"/>) rather than nothing. A wall, a bed and a storage hut set no
    /// value of their own in content, and without this the storyteller's building term would be zero for all
    /// of them. A def that does set one (every item in content) reads it, unchanged — including an explicit 0.</item>
    /// </list>
    ///
    /// <para/><b>Not every reader gets this.</b> <see cref="TradeUtility.BaseMarketValue"/> and
    /// <see cref="ThingDef.BaseMarketValue"/> are deliberate <c>statBases</c>-only lookups (see
    /// <see cref="StatExtension"/>) and stay so: trade prices and the ledger's value of a stored good are
    /// unchanged by this class. Only a read through the full pipeline — <c>thing.GetStatValue(MarketValue)</c>,
    /// <c>def.GetStatValue(MarketValue)</c> — sees the calculated value.
    ///
    /// <para/><b>Not ported, and why.</b> The recipe branch of <c>CalculatedBaseMarketValue</c>
    /// (<c>CalculableRecipe</c>: a def with no cost list that exactly one recipe with only fixed ingredients
    /// makes): every item in content sets its own <c>MarketValue</c>, so nothing could reach it; the day an item
    /// lands without one it belongs here. <c>StatDefOf.WorkToMake</c> does not exist, so the work term reads
    /// <c>WorkToBuild</c> alone. <c>stuffDef.VolumePerUnit</c> does not exist either (it is 1 for everything
    /// that is not a small-volume material, and the port has no such material), so a stuff's cost is its count
    /// times its value.
    /// </summary>
    public class StatWorker_MarketValue : StatWorker
    {
        /// <summary>Silver of value per point of work (RimWorld: <c>StatWorker_MarketValue.ValuePerWork</c>).</summary>
        public const float ValuePerWork = 0.0036f;

        /// <summary>What a unit of an unnamed stuff is guessed to cost (RimWorld: <c>DefaultGuessStuffCost</c>).</summary>
        public const float DefaultGuessStuffCost = 2f;

        public override float GetValueUnfinalized(StatRequest req, bool applyPostProcess = true)
        {
            ThingDef def = req.Def!;
            if (req.Thing is Pawn pawn)
            {
                return base.GetValueUnfinalized(StatRequest.For(def, req.StuffDef), applyPostProcess)
                    * PriceUtility.PawnQualityPriceFactor(pawn, SampledHealthFor(pawn));
            }

            if (def.StatBaseDefined(stat))
            {
                return base.GetValueUnfinalized(req, applyPostProcess);
            }

            return CalculatedBaseMarketValue(def, req.StuffDef);
        }

        /// <summary>
        /// A def with no market value of its own, priced from what goes into it (RimWorld:
        /// <c>StatWorker_MarketValue.CalculatedBaseMarketValue</c>, its non-recipe branch): the cost list at
        /// each material's own market value, plus the stuff the building is made of, plus
        /// <see cref="ValuePerWork"/> for each point of work beyond a token two.
        /// </summary>
        public static float CalculatedBaseMarketValue(ThingDef def, ThingDef? stuffDef)
        {
            float value = 0f;

            // WorkToBuild has a default of its own here (60), meaning "the work an ordinary building takes".
            // Only a building is built: a corpse, a filth or a plant that sets no market value must come out
            // at nothing, not at the price of sixty points of work it never took.
            float work = def.category == ThingCategory.Building ? def.GetStatValueAbstract(StatDefOf.WorkToBuild) : 0f;

            if (def.costList != null)
            {
                for (int i = 0; i < def.costList.Count; i++)
                {
                    ThingDefCountClass cost = def.costList[i];
                    value += cost.count * cost.thingDef.GetStatValue(StatDefOf.MarketValue);
                }
            }

            if (def.costStuffCount > 0)
            {
                value += stuffDef != null
                    ? def.costStuffCount * stuffDef.GetStatValue(StatDefOf.MarketValue)
                    : def.costStuffCount * DefaultGuessStuffCost;
            }

            if (work > 2f) value += work * ValuePerWork;
            return value;
        }

        /// <summary>
        /// A pawn the tiering system keeps at <see cref="PawnTier.Statistical"/> is priced from its coarse
        /// health readout and never has its hediff set opened (see <see cref="PriceUtility.PawnQualityPriceFactor"/>).
        /// Everyone else is priced from their real health.
        /// </summary>
        private static float? SampledHealthFor(Pawn pawn) =>
            pawn.tier != null && pawn.tier.Tier == PawnTier.Statistical ? pawn.tier.SampledHealthFraction : (float?)null;
    }
}
