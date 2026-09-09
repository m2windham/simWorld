using System;

using SimWorld.Director;

namespace SimWorld.Sim
{
    /// <summary>
    /// Service locator for the running simulation (RimWorld: <c>Verse.Find</c>). Ported code reads
    /// <c>Find.TickManager.TicksGame</c> everywhere; keeping the name keeps those call sites. Per thread,
    /// so tests and worker threads each own a clock.
    /// <para/>
    /// <b>Resolves through <see cref="CurrentGame"/> when one exists.</b> RimWorld backs <c>Find</c> with
    /// <c>Verse.Current.Game</c>; this port folds that indirection in here instead of porting a separate
    /// <c>Current</c> static class. Every property below checks <see cref="CurrentGame"/> first and only
    /// falls back to its own thread-static field when no game is current — so every caller written before
    /// <see cref="Game"/> existed (every test in this codebase, wiring a bare <c>TickManager</c>/
    /// <c>ResearchManager</c>/etc. by hand with no <see cref="Game"/> in sight) keeps resolving exactly as it
    /// always did, and <see cref="Reset"/> clearing <see cref="CurrentGame"/> is what guarantees that: no game,
    /// no behaviour change, ever.
    /// </summary>
    public static class Find
    {
        [ThreadStatic] private static Game? currentGame;

        [ThreadStatic] private static TickManager? tickManager;
        [ThreadStatic] private static SimWorld.Research.ResearchManager? researchManager;
        [ThreadStatic] private static Storyteller? storyteller;
        [ThreadStatic] private static SimWorld.Factions.FactionManager? factionManager;
        [ThreadStatic] private static SimWorld.Letters.LetterStack? letterStack;
        [ThreadStatic] private static SimWorld.Quests.QuestManager? questManager;
        [ThreadStatic] private static SimWorld.Scenario.Scenario? scenario;
        [ThreadStatic] private static SimWorld.Pawns.FamilyManager? familyManager;
        [ThreadStatic] private static SimWorld.Social.SocialInteractionManager? socialInteractionManager;
        [ThreadStatic] private static SimWorld.God.GodManager? godManager;
        [ThreadStatic] private static SimWorld.Social.Ideology.Ideo? ideo;
        [ThreadStatic] private static SimWorld.World.World? world;

        /// <summary>
        /// The live <see cref="Sim.Game"/> on this thread, or null when nothing has started one (RimWorld:
        /// <c>Verse.Current.Game</c>). Every other property on this class checks this first. Set by
        /// <see cref="Game.NewGame"/> and by loading a game (<see cref="Game.ExposeData"/>); cleared by
        /// <see cref="Reset"/>.
        /// </summary>
        public static Game? CurrentGame
        {
            get => currentGame;
            set => currentGame = value;
        }

        public static TickManager TickManager
        {
            get => currentGame != null ? currentGame.TickManager : (tickManager ??= new TickManager());
            set
            {
                if (currentGame != null) currentGame.TickManager = value;
                else tickManager = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        /// <summary>Research progress and the current project (RimWorld: <c>Verse.Find.ResearchManager</c>).</summary>
        public static SimWorld.Research.ResearchManager ResearchManager
        {
            get => currentGame != null ? currentGame.ResearchManager : (researchManager ??= new SimWorld.Research.ResearchManager());
            set
            {
                if (currentGame != null) currentGame.ResearchManager = value;
                else researchManager = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        /// <summary>The active threat director. Assign explicitly with a real StorytellerDef/DifficultyDef when constructing a game.</summary>
        public static Storyteller Storyteller
        {
            get => currentGame != null ? currentGame.Storyteller : (storyteller ??= new Storyteller());
            set
            {
                if (currentGame != null) currentGame.Storyteller = value;
                else storyteller = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        /// <summary>Every civilization and its diplomatic relations (RimWorld: <c>Verse.Find.FactionManager</c>).</summary>
        public static SimWorld.Factions.FactionManager FactionManager
        {
            get => currentGame != null ? currentGame.FactionManager : (factionManager ??= new SimWorld.Factions.FactionManager());
            set
            {
                if (currentGame != null) currentGame.FactionManager = value;
                else factionManager = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        /// <summary>Every letter waiting for the player (RimWorld: <c>Verse.Find.LetterStack</c>).</summary>
        public static SimWorld.Letters.LetterStack LetterStack
        {
            get => currentGame != null ? currentGame.LetterStack : (letterStack ??= new SimWorld.Letters.LetterStack());
            set
            {
                if (currentGame != null) currentGame.LetterStack = value;
                else letterStack = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        /// <summary>Every generated quest (RimWorld: <c>RimWorld.Find.QuestManager</c>).</summary>
        public static SimWorld.Quests.QuestManager QuestManager
        {
            get => currentGame != null ? currentGame.QuestManager : (questManager ??= new SimWorld.Quests.QuestManager());
            set
            {
                if (currentGame != null) currentGame.QuestManager = value;
                else questManager = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        /// <summary>The scenario the running game started from (RimWorld: <c>Verse.Find.Scenario</c>).</summary>
        public static SimWorld.Scenario.Scenario Scenario
        {
            get => currentGame != null ? currentGame.Scenario : (scenario ??= new SimWorld.Scenario.Scenario());
            set
            {
                if (currentGame != null) currentGame.Scenario = value;
                else scenario = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        /// <summary>Every household and the marriage/birth/death-from-age sweep that grows or shrinks them
        /// (SimWorld's own; RimWorld has no equivalent to port).</summary>
        public static SimWorld.Pawns.FamilyManager FamilyManager
        {
            get => currentGame != null ? currentGame.FamilyManager : (familyManager ??= new SimWorld.Pawns.FamilyManager());
            set
            {
                if (currentGame != null) currentGame.FamilyManager = value;
                else familyManager = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        /// <summary>Every household's periodic chitchat/insult/social-fight sweep (RimWorld: <c>Pawn_InteractionsTracker</c>, folded into one population-wide manager — see <see cref="SimWorld.Social.SocialInteractionManager"/>).</summary>
        public static SimWorld.Social.SocialInteractionManager SocialInteractionManager
        {
            get => currentGame != null ? currentGame.SocialInteractionManager : (socialInteractionManager ??= new SimWorld.Social.SocialInteractionManager());
            set
            {
                if (currentGame != null) currentGame.SocialInteractionManager = value;
                else socialInteractionManager = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        /// <summary>Standing edicts and the god view (RimWorld: no equivalent — SimWorld's own translation of
        /// the player-as-overseer into civilization-scale directives, <c>docs/spec/simworld-spec.md</c> §10).</summary>
        public static SimWorld.God.GodManager God
        {
            get => currentGame != null ? currentGame.God : (godManager ??= new SimWorld.God.GodManager());
            set
            {
                if (currentGame != null) currentGame.God = value;
                else godManager = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        /// <summary>
        /// The civilization's ideoligion (<c>social.ideology</c>), or null when it has none. Nullable for the
        /// same reason <see cref="World"/> is: unlike every other service here it has no sensible
        /// auto-created default, because an invented <c>IdeoDef</c> is a belief nobody chose. Every precept
        /// worker already reads null as "nothing applies".
        /// </summary>
        public static SimWorld.Social.Ideology.Ideo? Ideo
        {
            get => currentGame != null ? currentGame.Ideo : ideo;
            set
            {
                if (currentGame != null) currentGame.Ideo = value;
                else ideo = value;
            }
        }

        /// <summary>
        /// The generated planet (RimWorld: <c>Verse.Find.World</c>), or null before one has been generated.
        /// Unlike the other services this has no sensible auto-created default (an empty <c>World</c> has no
        /// grid to hand out), so this is the one property on <see cref="Find"/> that can return null.
        /// </summary>
        public static SimWorld.World.World? World
        {
            get => currentGame != null ? currentGame.World : world;
            set
            {
                if (currentGame != null) currentGame.World = value;
                else world = value;
            }
        }

        /// <summary>
        /// Drops the thread's services — including <see cref="CurrentGame"/> — so the next access starts
        /// fresh (tests). Every service above must be cleared here: a missed one leaks state between tests
        /// that call this expecting a clean slate.
        /// </summary>
        public static void Reset()
        {
            currentGame = null;
            tickManager = null;
            researchManager = null;
            storyteller = null;
            factionManager = null;
            letterStack = null;
            questManager = null;
            scenario = null;
            familyManager = null;
            socialInteractionManager = null;
            godManager = null;
            ideo = null;
            world = null;
        }
    }
}
