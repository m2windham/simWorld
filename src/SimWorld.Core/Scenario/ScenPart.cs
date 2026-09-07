using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;

namespace SimWorld.Scenario
{
    /// <summary>Which pawns a generation-time hook applies to (RimWorld: <c>RimWorld.PawnGenerationContext</c>).</summary>
    public enum PawnGenerationContext
    {
        All,
        PlayerStarter,
        NonPlayer,
    }

    /// <summary>
    /// What a <see cref="ScenPart"/> can read and change while a game is starting (RimWorld: scattered across
    /// <c>Verse.Find.GameInitData</c>/<c>Game</c>/<c>Faction</c> — SimWorld gathers the slice this system needs
    /// behind one seam so <see cref="ScenPart.PostGameStart"/> doesn't reach into systems this port doesn't own).
    /// <see cref="StartingPawns"/> is populated by whichever pawn-generation step runs before
    /// <see cref="Scenario.PostGameStart"/> — this system only reads and modifies that roster, never creates it.
    /// </summary>
    public interface IScenarioContext
    {
        ResearchManager ResearchManager { get; }

        Letters.LetterStack LetterStack { get; }

        List<Pawn> StartingPawns { get; }

        FactionDef? PlayerFactionDef { get; set; }

        /// <summary>Records a starting item; Things/maps don't exist yet, so this only remembers what was asked
        /// for (see <see cref="ScenarioContext.StartingThings"/>) for whichever system spawns the actual Thing.</summary>
        void AddStartingThing(ThingDef thingDef, int count);
    }

    /// <summary>One <see cref="IScenarioContext.AddStartingThing"/> call, recorded for later.</summary>
    public readonly struct StartingThingRecord
    {
        public readonly ThingDef thingDef;
        public readonly int count;

        public StartingThingRecord(ThingDef thingDef, int count)
        {
            this.thingDef = thingDef;
            this.count = count;
        }
    }

    /// <summary>Default/test <see cref="IScenarioContext"/>: plain in-memory storage, defaulting its manager
    /// references to the ambient <see cref="Find"/> ones so a caller only needs to override what a test cares about.</summary>
    public sealed class ScenarioContext : IScenarioContext
    {
        public ResearchManager ResearchManager { get; set; }

        public Letters.LetterStack LetterStack { get; set; }

        public List<Pawn> StartingPawns { get; } = new List<Pawn>();

        public FactionDef? PlayerFactionDef { get; set; }

        private readonly List<StartingThingRecord> startingThings = new List<StartingThingRecord>();

        public IReadOnlyList<StartingThingRecord> StartingThings => startingThings;

        public ScenarioContext(ResearchManager? researchManager = null, Letters.LetterStack? letterStack = null)
        {
            ResearchManager = researchManager ?? Find.ResearchManager;
            LetterStack = letterStack ?? Find.LetterStack;
        }

        public void AddStartingThing(ThingDef thingDef, int count) => startingThings.Add(new StartingThingRecord(thingDef, count));
    }

    /// <summary>
    /// One piece of a <see cref="Scenario"/>'s setup (RimWorld: <c>Verse.ScenPart</c>): configures the starting
    /// pawns, hands out starting items or research, sets the player's faction, and so on. Built from content
    /// via <see cref="ScenPartDef.scenPartClass"/> (a <c>Class</c> attribute inside a scenario's <c>parts</c> list).
    /// </summary>
    public abstract class ScenPart : IExposable
    {
        public ScenPartDef def = null!;

        /// <summary>One line of the scenario's read-out (RimWorld: <c>Verse.ScenPart.Summary</c>). Empty means
        /// this part contributes nothing visible.</summary>
        public virtual string Summary(Scenario scen) => "";

        /// <summary>Called once, right after world generation, before any game-start hook.</summary>
        public virtual void PostWorldGenerate(SimWorld.World.World? world)
        {
        }

        /// <summary>Called once a new game is starting, after <see cref="StartingPawns"/> exist.</summary>
        public virtual void PostGameStart(IScenarioContext ctx)
        {
        }

        /// <summary>Hook for a future pawn-generation step to let every part react to a newly generated pawn.
        /// Nothing calls this yet (SimWorld addition, kept for the shape RimWorld's own <c>Notify_PawnGenerated</c>
        /// gives every ScenPart).</summary>
        public virtual void Notify_PawnGenerated(Pawn pawn, PawnGenerationContext context)
        {
        }

        public virtual IEnumerable<string> ConfigErrors()
        {
            yield break;
        }

        public virtual void ExposeData()
        {
            ScenPartDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
        }
    }
}
