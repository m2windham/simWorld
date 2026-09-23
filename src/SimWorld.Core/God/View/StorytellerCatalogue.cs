using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Director;

namespace SimWorld.God.View
{
    /// <summary>
    /// The storytellers and difficulties a player can choose from, listed without a game.
    ///
    /// <para/><b>Why this is not on the snapshot.</b> <see cref="GodViewSnapshot"/> is one tick of one running
    /// civilization, and the choice this lists has to be offered <i>before</i> there is a civilization to take
    /// a tick of — a host's first screen is the one where the player picks who is going to tell the story.
    /// A snapshot cannot carry that, so this does, and <see cref="StorytellerView"/> then calls straight into
    /// it: the pre-game picker and the in-game one are populated by the same code, so they cannot disagree
    /// about what ships.
    ///
    /// <para/><b>It lists; it does not select.</b> There is no <c>Choose</c> here. Before a game the choice is
    /// an argument to <c>Sim.Game.NewGame</c>; during one it is <see cref="GodCommands.SetStoryteller"/> and
    /// <see cref="GodCommands.SetDifficulty"/>. A selector on a read-only catalogue would be a second write
    /// path into the simulation that the command surface does not know about, which is exactly what
    /// <see cref="GodCommands"/>'s own doc says must not exist ("widening what a god may do means adding a
    /// method there, deliberately, rather than a host discovering it can already do it").
    ///
    /// <para/>Both lists come back empty rather than throwing when no content is loaded — the same state
    /// <see cref="GodViewSnapshot.ContentLoaded"/> exists to let a host recognise.
    /// </summary>
    public static class StorytellerCatalogue
    {
        /// <summary>
        /// Every storyteller in content, in content's own display order.
        ///
        /// <para/>Ordered by <see cref="StorytellerDef.listOrder"/> — the field's own doc calls it "display
        /// ordering only (RimWorld's storyteller-select screen)", and this is that screen. Ties break on
        /// defName so the order is total and stable rather than dependent on file load order.
        /// </summary>
        public static IReadOnlyList<StorytellerOption> Storytellers()
        {
            IReadOnlyList<StorytellerDef> all = DefDatabase<StorytellerDef>.AllDefsListForReading;
            var defs = new List<StorytellerDef>(all.Count);
            for (int i = 0; i < all.Count; i++) defs.Add(all[i]);

            defs.Sort(static (a, b) =>
            {
                int byOrder = a.listOrder.CompareTo(b.listOrder);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(a.defName, b.defName);
            });

            var options = new List<StorytellerOption>(defs.Count);
            for (int i = 0; i < defs.Count; i++) options.Add(StorytellerOption.For(defs[i]));
            return options;
        }

        /// <summary>
        /// Every difficulty in content, gentlest first.
        ///
        /// <para/><see cref="DifficultyDef"/> has no <c>listOrder</c>, so the order is taken from the one
        /// field that already means "how hard" — <see cref="DifficultyDef.threatScale"/>, ascending. That
        /// gives the shipped presets the order a player expects (peaceful, easy, medium, rough, extreme)
        /// without the seam handing out the number itself: an ordering is a thing to choose along, a
        /// multiplier is a thing to solve. Ties break on defName.
        /// </summary>
        public static IReadOnlyList<DifficultyOption> Difficulties()
        {
            IReadOnlyList<DifficultyDef> all = DefDatabase<DifficultyDef>.AllDefsListForReading;
            var defs = new List<DifficultyDef>(all.Count);
            for (int i = 0; i < all.Count; i++) defs.Add(all[i]);

            defs.Sort(static (a, b) =>
            {
                int byThreat = a.threatScale.CompareTo(b.threatScale);
                return byThreat != 0 ? byThreat : string.CompareOrdinal(a.defName, b.defName);
            });

            var options = new List<DifficultyOption>(defs.Count);
            for (int i = 0; i < defs.Count; i++) options.Add(DifficultyOption.For(defs[i]));
            return options;
        }
    }
}
