using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>A tag content uses to say which kind of place an incident may target (RimWorld: <c>Verse.IncidentTargetTagDef</c>).</summary>
    public class IncidentTargetTagDef : Def
    {
    }

    /// <summary>
    /// Anything the storyteller can throw an incident at (RimWorld: <c>Verse.IIncidentTarget</c>, implemented by
    /// <c>Map</c>/<c>Caravan</c>/<c>World</c>). SimWorld has no maps or factions yet, so
    /// <see cref="CivilizationTarget"/> is the only implementation: the player's whole civilization stands in for
    /// "the colony". Extends <see cref="ILoadReferenceable"/> so a queued incident can hold a reference to its
    /// target across a save (see <see cref="QueuedIncident"/>).
    /// </summary>
    public interface IIncidentTarget : ILoadReferenceable
    {
        StoryState StoryState { get; }

        int Tile { get; }

        IEnumerable<IncidentTargetTagDef> IncidentTargetTags();

        float PlayerWealthForStoryteller { get; }

        IEnumerable<Pawn> PlayerPawnsForStoryteller { get; }

        int ConstantRandSeed { get; }
    }

    /// <summary>
    /// Per-target storyteller memory (RimWorld: <c>RimWorld.StoryState</c>): when each <see cref="IncidentDef"/>
    /// last fired, and when a big threat last happened (raid-beacon-free "quiet time" tracking).
    /// </summary>
    public sealed class StoryState : IExposable
    {
        private Dictionary<IncidentDef, int>? lastFireTicks;
        private int lastThreatBigTick = -1;

        /// <summary>Tick of the most recent ThreatBig incident against this target, or -1 if none yet.</summary>
        public int LastThreatBigTick => lastThreatBigTick;

        public bool HasFired(IncidentDef def) => lastFireTicks != null && lastFireTicks.ContainsKey(def);

        /// <summary>Tick the def last fired, or -1 if it never has.</summary>
        public int LastFireTick(IncidentDef def) =>
            lastFireTicks != null && lastFireTicks.TryGetValue(def, out int tick) ? tick : -1;

        public void Notify_IncidentFired(FiringIncident firingIncident)
        {
            if (firingIncident == null) return;
            lastFireTicks ??= new Dictionary<IncidentDef, int>();
            lastFireTicks[firingIncident.def] = Find.TickManager.TicksGame;
            if (firingIncident.def.category == IncidentCategoryDefOf.ThreatBig)
            {
                lastThreatBigTick = Find.TickManager.TicksGame;
            }
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref lastFireTicks, "lastFireTicks", LookMode.Def, LookMode.Value);
            Scribe_Values.Look(ref lastThreatBigTick, "lastThreatBigTick", -1);
        }
    }

    /// <summary>
    /// SimWorld translation of RimWorld's map/colony: the player's whole civilization as a single incident
    /// target. Holds the pawn roster and wealth figure other systems (population, economy) are expected to
    /// keep current; until factions and maps exist, every incident targets this one object, reporting
    /// <see cref="IncidentTargetTagDefOf.Map_PlayerHome"/> as its only tag.
    /// </summary>
    public sealed class CivilizationTarget : IIncidentTarget, IExposable
    {
        private StoryState storyState = new StoryState();
        private float wealth;
        private int tile;
        private int constantRandSeed = 1;

        /// <summary>Test/other-system-settable roster. Not itself saved here — pawn ownership belongs to the
        /// population/roster system that eventually replaces this list.</summary>
        public readonly List<Pawn> pawns = new List<Pawn>();

        private readonly List<IncidentTargetTagDef> targetTags = new List<IncidentTargetTagDef>();

        /// <summary>
        /// The physical map raiders/visitors would arrive on, when one exists. Nothing in the codebase wires a
        /// <see cref="global::SimWorld.Map.Map"/> to the civilization-scale incident target yet — local maps
        /// (<c>MapGen.MapGenerator</c>) are generated per settlement, on demand, and never handed back to
        /// <see cref="Storyteller"/> or its targets. This is the settable hook for whichever future work does
        /// that wiring; every game today leaves it null, and <see cref="IncidentWorker_RaidEnemy"/>'s class doc
        /// explains exactly what that means for where a generated raid squad ends up. Not saved: a live map
        /// reference is re-established the same way it would be set in the first place, not round-tripped.
        /// </summary>
        public Map.Map? Map { get; set; }

        /// <summary>Parameterless overload so the save system can reconstruct one by reflection; registers with no storyteller.</summary>
        public CivilizationTarget() : this(null)
        {
        }

        /// <param name="storyteller">When given, this target registers itself so the storyteller's interval
        /// tick considers it (RimWorld: a Map does this when it's added to the game).</param>
        public CivilizationTarget(Storyteller? storyteller)
        {
            targetTags.Add(IncidentTargetTagDefOf.Map_PlayerHome);
            storyteller?.RegisterTarget(this);
        }

        public StoryState StoryState => storyState;

        public int Tile { get => tile; set => tile = value; }

        public float PlayerWealthForStoryteller { get => wealth; set => wealth = value; }

        public IEnumerable<Pawn> PlayerPawnsForStoryteller => pawns;

        public int ConstantRandSeed { get => constantRandSeed; set => constantRandSeed = value; }

        public IEnumerable<IncidentTargetTagDef> IncidentTargetTags() => targetTags;

        /// <summary>There is exactly one civilization, so one fixed id is enough.</summary>
        public string GetUniqueLoadID() => "Civilization";

        public void ExposeData()
        {
            Scribe_Values.Look(ref wealth, "wealth");
            Scribe_Values.Look(ref tile, "tile");
            Scribe_Values.Look(ref constantRandSeed, "constantRandSeed", 1);
            StoryState? s = storyState;
            Scribe_Deep.Look(ref s, "storyState");
            storyState = s ?? new StoryState();
        }
    }
}
