using SimWorld.Crafting;
using SimWorld.Defs;

namespace SimWorld.AI
{
    /// <summary>
    /// Defs the <c>Hunt</c> work type binds by name (system 9: AI — hunting). Its own class rather than two
    /// more fields on <see cref="JobDefOf"/>/<c>Crafting.RecipeDefOf</c>: <c>DefOfHelper</c> binds by scanning
    /// every <c>[DefOf]</c> type, so a module's bindings need no edit to a file other lanes are also editing
    /// (CLAUDE.md — "add a file rather than edit a shared one"). <c>Building.BuildingJobDefOf</c> and
    /// <c>Crafting.CraftingJobDefOf</c> already do exactly this.
    /// </summary>
    [DefOf]
    public static class HuntingDefOf
    {
        /// <summary>Stalk a wild animal, kill it, and butcher it where it fell (see
        /// <see cref="JobDriver_Hunt"/>; RimWorld: <c>JobDefOf.Hunt</c>).</summary>
        public static JobDef Hunt = null!;

        /// <summary>The shipped butchery recipe <see cref="JobDriver_Hunt"/> applies at the kill site — the
        /// same <c>ButcherAnimal</c> def a <c>TableButcher</c> lists, reused rather than duplicated. Bound
        /// here because <c>Crafting.RecipeDefOf</c> (a file this lane does not own) does not carry it.</summary>
        public static RecipeDef ButcherAnimal = null!;
    }
}
