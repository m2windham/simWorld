using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.World;

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
    /// target, reporting <see cref="IncidentTargetTagDefOf.Map_PlayerHome"/> as its only tag.
    ///
    /// <para/><b>A civilization of several settlements, not one colony.</b> RimWorld has an
    /// <c>IIncidentTarget</c> per map, and a colony is one map, so "the target" and "the place" are the same
    /// object. A civilization is not: it is several settlements that grow, get founded and can be abandoned,
    /// and the storyteller is telling one story about all of them. So the target stays one — one
    /// <see cref="StoryState"/>, one refire memory, one adaptation curve, because "a raid this decade" is a
    /// fact about the civilization rather than about a town — while the roster, the seat and the wealth it
    /// reports are <em>derived</em> from <see cref="Settlements"/> rather than kept in sync by hand, and an
    /// incident that has to happen somewhere picks that somewhere with
    /// <see cref="ChooseTargetSettlement"/>.
    ///
    /// <para/>The hand-set <see cref="pawns"/> list and <see cref="PlayerWealthForStoryteller"/> setter stay,
    /// and stay authoritative when no settlements are attached: a test, or any caller that wants to pose a
    /// civilization without building one, is still served exactly as before.
    /// </summary>
    public sealed class CivilizationTarget : IIncidentTarget, IExposable
    {
        private StoryState storyState = new StoryState();
        private float wealth;
        private int tile;
        private int constantRandSeed = 1;

        /// <summary>Hand-set roster, used when no <see cref="Settlements"/> are attached. Not itself saved
        /// here — pawn ownership belongs to the settlements.</summary>
        public readonly List<Pawn> pawns = new List<Pawn>();

        private readonly List<Settlement> settlements = new List<Settlement>();

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

        /// <summary>
        /// The civilization's settlements, newest membership wins. Live objects owned by the
        /// <see cref="global::SimWorld.World.World"/>, so they are not saved here — <see cref="Sim.Game"/>
        /// re-attaches them on the same interval the storyteller reads them on, exactly as it already
        /// re-collects the roster.
        /// </summary>
        public IReadOnlyList<Settlement> Settlements => settlements;

        /// <summary>Replaces the attached settlements. Empty restores the hand-set behaviour.</summary>
        public void SetSettlements(IEnumerable<Settlement>? civSettlements)
        {
            settlements.Clear();
            if (civSettlements == null) return;
            foreach (Settlement s in civSettlements)
            {
                if (s != null) settlements.Add(s);
            }
        }

        /// <summary>
        /// The civilization's seat: the settlement it was founded from, which is the oldest one, ties broken
        /// by tile so the answer never depends on list order. Falls back to the settable
        /// <see cref="Tile"/> when no settlements are attached.
        /// </summary>
        public Settlement? Seat
        {
            get
            {
                Settlement? seat = null;
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement s = settlements[i];
                    if (seat == null
                        || s.foundingTick < seat.foundingTick
                        || (s.foundingTick == seat.foundingTick && s.tile < seat.tile))
                    {
                        seat = s;
                    }
                }
                return seat;
            }
        }

        /// <summary>The seat's tile once there are settlements; the settable value until then.</summary>
        public int Tile { get => Seat?.tile ?? tile; set => tile = value; }

        /// <summary>
        /// Market value of everything the civilization's settlements hold. This is the wealth term
        /// <see cref="StorytellerUtility"/> has always wanted and never had — <c>EraDef.threatPointsFactor</c>
        /// was written as an explicit stand-in for it, and stays, because scaling threat by the age a
        /// civilization has reached is SimWorld's own idea rather than a substitute now that stores are real.
        /// Population is deliberately not counted here: RimWorld's threat curve already reads the roster
        /// separately, and adding people to wealth would charge for them twice. Falls back to the settable
        /// value when no settlements are attached.
        /// </summary>
        public float PlayerWealthForStoryteller
        {
            get
            {
                if (settlements.Count == 0) return wealth;
                float total = 0f;
                for (int i = 0; i < settlements.Count; i++)
                {
                    foreach (KeyValuePair<ThingDef, int> entry in settlements[i].Stores)
                    {
                        total += TradeUtility.BaseMarketValue(entry.Key) * entry.Value;
                    }
                }
                return total;
            }
            set => wealth = value;
        }

        public IEnumerable<Pawn> PlayerPawnsForStoryteller
        {
            get
            {
                if (settlements.Count == 0) return pawns;
                var all = new List<Pawn>();
                for (int i = 0; i < settlements.Count; i++)
                {
                    all.AddRange(settlements[i].Citizens);
                }
                return all;
            }
        }

        /// <summary>
        /// Where an incident that has to happen somewhere happens. Weighted by
        /// <see cref="Settlement.TotalPopulation"/>: a raid is likelier to fall on the capital than on a
        /// hamlet, which is both the obvious model and the one that keeps a civilization's growth visible in
        /// what happens to it. Every settlement stays reachable — an empty one is still weighted 1 rather
        /// than 0, so a place cannot become permanently invisible to the narrator by losing its people.
        /// Returns null only when the civilization has no settlements at all.
        /// </summary>
        public Settlement? ChooseTargetSettlement(RandomStream rand)
        {
            if (rand == null) throw new System.ArgumentNullException(nameof(rand));
            if (settlements.Count == 0) return null;
            if (settlements.Count == 1) return settlements[0];

            long total = 0;
            for (int i = 0; i < settlements.Count; i++) total += WeightOf(settlements[i]);

            long roll = rand.RangeInclusive(1, (int)System.Math.Min(total, int.MaxValue));
            long running = 0;
            for (int i = 0; i < settlements.Count; i++)
            {
                running += WeightOf(settlements[i]);
                if (roll <= running) return settlements[i];
            }
            return settlements[settlements.Count - 1];
        }

        private static int WeightOf(Settlement s) => s.TotalPopulation > 0 ? s.TotalPopulation : 1;

        /// <summary>
        /// The map an incident should land on: the entered interior of the settlement it picked, when that
        /// settlement has one, and otherwise the settable <see cref="Map"/> hook. A settlement nobody has
        /// entered has no interior map — generating one just to stage an off-screen incident would be the
        /// tail wagging the dog, so the incident happens to the civilization without a map, exactly as it
        /// did before settlements existed.
        /// </summary>
        public Map.Map? MapFor(Settlement? settlement) => settlement?.InteriorMap ?? Map;

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
