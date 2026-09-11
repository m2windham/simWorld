using SimWorld.Crafting;
using SimWorld.Defs;

namespace SimWorld.AI
{
    /// <summary>
    /// Defs the corpse work givers bind by name (system: corpses). Its own class rather than more fields on
    /// <see cref="JobDefOf"/> or <c>Crafting.RecipeDefOf</c>: <c>DefOfHelper</c> binds by scanning every
    /// <c>[DefOf]</c> type, so a module's bindings need no edit to a file another lane may also be editing
    /// (CLAUDE.md — "add a file rather than edit a shared one"). <see cref="HuntingDefOf"/> and
    /// <c>Building.BuildingJobDefOf</c> already do exactly this.
    /// </summary>
    [DefOf]
    public static class CorpseWorkDefOf
    {
        /// <summary>Fetch a corpse, carry it to a butcher bench and butcher it there (see
        /// <see cref="JobDriver_ButcherCorpse"/>; RimWorld reaches the same end through a <c>DoBill</c> job
        /// on a butcher table).</summary>
        public static JobDef ButcherCorpse = null!;

        /// <summary>The shipped butchery recipe, bound again here rather than reached through
        /// <see cref="HuntingDefOf"/>: this module's butchery has nothing to do with hunting, and two
        /// <c>[DefOf]</c> fields naming one def resolve to the same object.</summary>
        public static RecipeDef ButcherAnimal = null!;
    }
}
