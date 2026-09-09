using System.Collections.Generic;
using SimWorld.Health;
using SimWorld.Pawns;

namespace SimWorld.Crafting
{
    /// <summary>
    /// The behaviour half of a <see cref="RecipeDef"/> (RimWorld: <c>RimWorld.RecipeWorker</c>), selected by
    /// <see cref="RecipeDef.workerClass"/> the same way every other worker in this content system is. An
    /// ordinary crafting recipe needs none of this — <see cref="GenRecipe"/> makes its products from the def
    /// alone — so the base worker does nothing. Surgery is what the seam exists for: what a surgery does to a
    /// pawn cannot be expressed as "consume these, produce those".
    /// </summary>
    public class RecipeWorker
    {
        public RecipeDef recipe = null!;

        /// <summary>
        /// Applies the recipe to a pawn. <paramref name="billDoer"/> is the pawn doing the work (null for an
        /// effect with no author — a scenario or a debug call), <paramref name="part"/> the body part the bill
        /// named, when the recipe targets one.
        /// </summary>
        public virtual void ApplyOnPawn(Pawn pawn, BodyPartRecord? part, Pawn? billDoer, List<ItemStack>? ingredients)
        {
        }

        /// <summary>
        /// The body parts this recipe could be applied to on <paramref name="pawn"/> (RimWorld:
        /// <c>RecipeWorker.GetPartsToApplyOn</c>). Empty for a recipe that does not target a part.
        /// </summary>
        public virtual IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn)
        {
            yield break;
        }

        /// <summary>True when this recipe could be queued on <paramref name="pawn"/> at all.</summary>
        public virtual bool AvailableOnNow(Pawn pawn) => true;
    }
}
