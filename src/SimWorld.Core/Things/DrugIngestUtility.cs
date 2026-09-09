using System;
using SimWorld.Crafting;
using SimWorld.Pawns;

namespace SimWorld.Things
{
    /// <summary>
    /// Consumes one dose of a live drug Thing (RimWorld: the ingestion half of <c>Verse.Thing_Ingestible</c>
    /// eating, trimmed to drugs). <see cref="Crafting.FoodUtility.Eat"/> is the equivalent for the
    /// <see cref="ItemStack"/>-shaped food path Crafting already ships; a drug needs a live
    /// <see cref="ThingWithComps"/> instead, so <see cref="CompDrug.PostIngested"/> has somewhere to read
    /// <c>parent.def</c>/apply its own effect, which is why this is its own small utility rather than a
    /// second overload of that one.
    /// </summary>
    public static class DrugIngestUtility
    {
        /// <summary>Feeds <paramref name="item"/>'s nutrition (usually 0 for a drug) to the pawn, fires its
        /// <see cref="CompDrug"/> if it has one, then removes one unit — destroying the stack once it is
        /// empty.</summary>
        public static void Ingest(Pawn pawn, ThingWithComps item)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (item == null) throw new ArgumentNullException(nameof(item));

            IngestibleProperties? ingestible = item.def.ingestible;
            if (ingestible != null && ingestible.nutrition > 0f)
            {
                pawn.needs.food?.Eat(ingestible.nutrition);
            }

            item.GetComp<CompDrug>()?.PostIngested(pawn);

            item.stackCount--;
            if (item.stackCount <= 0) item.Destroy();
        }
    }
}
