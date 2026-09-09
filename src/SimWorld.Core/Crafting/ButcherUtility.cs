using System;
using SimWorld.Pawns;

namespace SimWorld.Crafting
{
    /// <summary>
    /// Runs a butchery recipe on a dead animal (RimWorld: <c>RimWorld.ButcherUtility</c>). The direct,
    /// no-job-driver call site <c>Recipe_ButcherAnimal</c> is invoked through — see that type's own doc.
    /// </summary>
    public static class ButcherUtility
    {
        /// <summary>Butchers <paramref name="animal"/> with <paramref name="recipe"/>, as <paramref name="butcher"/>.
        /// False (no-op) when the animal is not dead; throws if <paramref name="recipe"/> is not a butchery
        /// recipe at all.</summary>
        public static bool TryButcher(Pawn animal, Pawn? butcher, RecipeDef recipe)
        {
            if (animal == null) throw new ArgumentNullException(nameof(animal));
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            if (!recipe.isButchery) throw new ArgumentException("RecipeDef " + recipe.defName + " is not a butchery recipe.", nameof(recipe));
            if (!animal.Dead) return false;

            recipe.Worker.ApplyOnPawn(animal, null, butcher, null);
            return true;
        }
    }
}
