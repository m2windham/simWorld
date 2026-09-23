using System.Collections.Generic;

using SimWorld.Director;
using SimWorld.Sim;

namespace SimWorld.God.View
{
    /// <summary>
    /// One narrator personality as the view needs it — the handle to pick it by, and the two strings that let
    /// a player tell it from the other two. The same value shape <see cref="ResearchProjectOption"/> gives a
    /// research project and <see cref="EdictOption"/> gives an edict.
    ///
    /// <para/><b>What is deliberately not here.</b> None of the storyteller's cadence — no <c>onDays</c>, no
    /// mean-time-between, no category weights, and above all no threat-point reading. The numbers are how the
    /// machine decides; the <see cref="Description"/> is how the player decides, and it is the only one of the
    /// two that survives contact with <c>CLAUDE.md</c>'s rule about instruments ("the moment it becomes the
    /// target it starts doing the opposite"). RimWorld never shows a player their colony's threat points and
    /// neither does this: a storyteller is chosen for the kind of run you want, not optimised against.
    /// </summary>
    public sealed class StorytellerOption
    {
        internal StorytellerOption(string defName, string label, string description)
        {
            DefName = defName;
            Label = label;
            Description = description;
        }

        /// <summary>The handle to pass to <see cref="GodCommands.SetStoryteller"/>. Not a
        /// <see cref="StorytellerDef"/> — see <see cref="GodViewSnapshot"/> on why the seam is a string.</summary>
        public string DefName { get; }

        public string Label { get; }

        /// <summary>Content's own sentence about this narrator, empty when content ships none. The player's
        /// whole basis for choosing, which is why the option carries it rather than leaving the host to keep
        /// its own copy of three descriptions that live in XML.</summary>
        public string Description { get; }

        internal static StorytellerOption For(StorytellerDef def) =>
            new StorytellerOption(def.defName, def.LabelCap, def.description ?? "");
    }

    /// <summary>
    /// One difficulty preset as the view needs it. Same three fields as <see cref="StorytellerOption"/>, and
    /// the same omission: no <c>threatScale</c>, no yield or mood factor. Those are the knobs the preset turns
    /// on the player's behalf, and a player picking between "gentler threats and better yields" and "severe
    /// threats and poor yields" is making the decision the preset exists to offer. A column of multipliers
    /// beside them turns the same screen into a spreadsheet to solve.
    /// </summary>
    public sealed class DifficultyOption
    {
        internal DifficultyOption(string defName, string label, string description)
        {
            DefName = defName;
            Label = label;
            Description = description;
        }

        /// <summary>The handle to pass to <see cref="GodCommands.SetDifficulty"/>.</summary>
        public string DefName { get; }

        public string Label { get; }

        public string Description { get; }

        internal static DifficultyOption For(DifficultyDef def) =>
            new DifficultyOption(def.defName, def.LabelCap, def.description ?? "");
    }

    /// <summary>
    /// Who is telling this civilization's story, how hard they are telling it, and what the player could
    /// change either to — the read half of the storyteller lever, without which
    /// <see cref="GodCommands.SetStoryteller"/> and <see cref="GodCommands.SetDifficulty"/> would be commands
    /// naming things the player cannot see (<c>docs/design/player-first.md</c> §9).
    ///
    /// <para/><b>Why this is a lever at all, and not a setter in a hat.</b>
    /// <c>docs/design/the-loop.md</c> makes the storyteller the one source of pressure in a one-settlement
    /// game, which makes the choice of storyteller and difficulty the player's largest single statement about
    /// what kind of run they want — and until now the only way to make it was
    /// <c>Sim.Game.NewGame(..., storytellerDef, difficultyDef)</c>, which a host bound to this seam cannot
    /// reach and which can only be said once. Writing <see cref="Director.Storyteller.difficulty"/> is not the
    /// player overwriting an outcome: nothing caches it. <see cref="StorytellerUtility.DefaultThreatPointsNow"/>,
    /// <see cref="StoryWatcher_Adaptation.AdaptationTick"/>, <see cref="DifficultyUtility"/> and
    /// <see cref="StorytellerComp_Disease"/> each re-read it the next time they compute, so the consequence is
    /// worked out by the machinery afterwards rather than asserted by the command. That is the test in
    /// <c>docs/design/player-first.md</c> §2, and it is why this one passes where "set her age to 40" does not.
    ///
    /// <para/><b>No reading of what the storyteller currently intends.</b> Not the threat points, not the
    /// adaptation factor, not when the next incident is due. See <see cref="StorytellerOption"/>.
    /// </summary>
    public sealed class StorytellerView
    {
        internal StorytellerView(
            StorytellerOption? current,
            DifficultyOption? currentDifficulty,
            IReadOnlyList<StorytellerOption> storytellers,
            IReadOnlyList<DifficultyOption> difficulties)
        {
            Current = current;
            CurrentDifficulty = currentDifficulty;
            Storytellers = storytellers;
            Difficulties = difficulties;
        }

        /// <summary>The storyteller in effect, or null when no game has been started — the main-menu state
        /// <see cref="GodViewSnapshot.ContentLoaded"/> exists to keep a host from mistaking for a broken read
        /// model. A host drawing a picker before a game reads <see cref="Storytellers"/> and
        /// <see cref="Difficulties"/>, which are populated either way; see <see cref="StorytellerCatalogue"/>
        /// for the same two lists without taking a snapshot at all.</summary>
        public StorytellerOption? Current { get; }

        /// <summary>The difficulty in effect, or null when no game has been started.</summary>
        public DifficultyOption? CurrentDifficulty { get; }

        /// <summary>Every storyteller in content, in content's own display order
        /// (<see cref="StorytellerDef.listOrder"/>) — including the one in effect, which the host matches by
        /// comparing <see cref="StorytellerOption.DefName"/> against <see cref="Current"/> rather than being
        /// told twice.</summary>
        public IReadOnlyList<StorytellerOption> Storytellers { get; }

        /// <summary>Every difficulty in content, gentlest first.</summary>
        public IReadOnlyList<DifficultyOption> Difficulties { get; }

        internal static StorytellerView Capture()
        {
            Storyteller storyteller = Find.Storyteller;
            StorytellerDef? def = storyteller.def;
            DifficultyDef? difficulty = storyteller.difficulty;

            return new StorytellerView(
                def == null ? null : StorytellerOption.For(def),
                difficulty == null ? null : DifficultyOption.For(difficulty),
                StorytellerCatalogue.Storytellers(),
                StorytellerCatalogue.Difficulties());
        }
    }

    public sealed partial class GodViewSnapshot
    {
        private StorytellerView? storyteller;

        /// <summary>
        /// Who is telling this civilization's story and how hard, plus everything the player could switch to.
        ///
        /// <para/><b>Lazily computed and cached rather than threaded through <see cref="Capture(int)"/>'s
        /// constructor</b>, for the reason <see cref="Research"/> and <see cref="VisitingTraders"/> already
        /// give: that constructor lives in <c>GodViewSnapshot.cs</c>, and a partial file cannot widen a
        /// constructor it does not declare without editing the shared file CLAUDE.md says not to. The cache
        /// means two reads of the same snapshot always agree with each other.
        /// </summary>
        public StorytellerView Storyteller => storyteller ??= StorytellerView.Capture();
    }
}
