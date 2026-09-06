using System;
using System.Globalization;
using SimWorld.Defs;
using SimWorld.MindState;
using SimWorld.Needs;
using SimWorld.Sim;

namespace SimWorld.Pawns
{
    /// <summary>
    /// A person or creature (RimWorld: <c>Verse.Pawn</c>). Every citizen is one of these — the trackers hung
    /// off it are the per-agent systems (needs, story, mind; health arrives with system 7). This is the
    /// agent skeleton the remaining systems attach to; map presence, jobs and combat come with their modules.
    /// </summary>
    public class Pawn : IExposable, ILoadReferenceable, ITickable
    {
        private static int nextThingId;

        public ThingDef def = null!;
        public int thingIDNumber = -1;
        public string? name;

        public Pawn_NeedsTracker needs = null!;
        public Pawn_StoryTracker story = null!;
        public Pawn_MindState mindState = null!;

        /// <summary>Environment sampler for seeker needs (beauty, comfort, outdoors, room); the map supplies it later.</summary>
        public IEnvironmentSampler? environment;

        private bool destroyed;

        public Pawn()
        {
        }

        public Pawn(ThingDef def, string? name = null)
        {
            this.def = def ?? throw new ArgumentNullException(nameof(def));
            if (def.race == null) throw new ArgumentException("ThingDef " + def.defName + " has no race properties.", nameof(def));
            this.name = name;
            thingIDNumber = AllocateThingId();
            InitializeTrackers();
            needs.AddOrRemoveNeedsAsAppropriate();
        }

        public static int AllocateThingId() => nextThingId++;

        /// <summary>Tests and loaders that mint ids elsewhere reset the counter with this.</summary>
        public static void ResetThingIdCounter(int next = 0) => nextThingId = next;

        public RaceProperties RaceProps => def.race!;

        public string Label => name ?? def.label ?? def.defName;

        public string ThingID => def.defName + thingIDNumber.ToString(CultureInfo.InvariantCulture);

        // ---- state other systems set; kept as plain flags until those systems land ----

        /// <summary>Set by the job system while the pawn is in bed asleep.</summary>
        public bool Asleep { get; set; }

        /// <summary>Set by the health system when the pawn cannot act.</summary>
        public bool Downed { get; set; }

        public bool Dead { get; set; }

        /// <summary>In a caravan or a pod: needs freeze.</summary>
        public bool Suspended { get; set; }

        public bool Destroyed => destroyed;

        public bool InMentalState => mindState.mentalStateHandler.InMentalState;

        public MentalStateDef? MentalStateDef => mindState.mentalStateHandler.CurStateDef;

        public bool Awake() => !Asleep && !Dead;

        public float BodySize => RaceProps.baseBodySize;

        /// <summary>Species hunger rate × health modifiers (health hooks in when system 7 lands).</summary>
        public virtual float HungerRate => RaceProps.baseHungerRate * HungerRateFactorFromHealth;

        public virtual float HungerRateFactorFromHealth => 1f;

        public virtual float RestFallFactorFromHealth => 1f;

        public virtual float RestRateMultiplier => 1f;

        /// <summary>Mood level under which minor breaks become possible; traits and stats adjust it later.</summary>
        public virtual float MentalBreakThreshold => RaceProps.mentalBreakThreshold;

        /// <summary>Health-system hook: true when the pawn carries the given hediff. System 7 overrides.</summary>
        public virtual bool HasHediff(Def hediffDef) => false;

        /// <summary>Health-system hook for starvation damage; system 7 wires malnutrition to it.</summary>
        public virtual void Notify_StarvationInterval(bool starving)
        {
        }

        public virtual void Notify_TraitsChanged()
        {
        }

        /// <summary>Spreads per-pawn periodic work across ticks (RimWorld: <c>Gen.IsHashIntervalTick</c>).</summary>
        public bool IsHashIntervalTick(int interval)
        {
            return GenMath.PositiveMod(Find.TickManager.TicksGame + HashOffsetTicks(), interval) == 0;
        }

        public int HashOffsetTicks() => thingIDNumber * 3;

        public void Destroy()
        {
            destroyed = true;
        }

        protected virtual void InitializeTrackers()
        {
            needs ??= new Pawn_NeedsTracker(this);
            story ??= new Pawn_StoryTracker(this);
            mindState ??= new Pawn_MindState(this);
        }

        // ---- ITickable ----

        int ITickable.TickId => thingIDNumber;
        TickerType ITickable.TickerType => def.tickerType;

        public virtual void Tick()
        {
            if (Dead || Suspended) return;
            needs.NeedsTrackerTick();
            mindState.MindStateTick();
        }

        public virtual void TickRare()
        {
        }

        public virtual void TickLong()
        {
        }

        // ---- Scribe ----

        public string GetUniqueLoadID() => "Thing_" + ThingID;

        public virtual void ExposeData()
        {
            ThingDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref thingIDNumber, "id", -1);
            Scribe_Values.Look(ref name, "name");
            bool asleep = Asleep, downed = Downed, dead = Dead, suspended = Suspended;
            Scribe_Values.Look(ref asleep, "asleep");
            Scribe_Values.Look(ref downed, "downed");
            Scribe_Values.Look(ref dead, "dead");
            Scribe_Values.Look(ref suspended, "suspended");
            Asleep = asleep; Downed = downed; Dead = dead; Suspended = suspended;

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                InitializeTrackers();
                if (thingIDNumber >= nextThingId) nextThingId = thingIDNumber + 1;
            }
            Pawn_NeedsTracker? n = needs;
            Scribe_Deep.Look(ref n, "needs", this);
            needs = n ?? new Pawn_NeedsTracker(this);
            Pawn_StoryTracker? s = story;
            Scribe_Deep.Look(ref s, "story", this);
            story = s ?? new Pawn_StoryTracker(this);
            Pawn_MindState? m = mindState;
            Scribe_Deep.Look(ref m, "mindState", this);
            mindState = m ?? new Pawn_MindState(this);
        }

        public override string ToString() => Label + " (" + ThingID + ")";
    }

    /// <summary>What the surroundings offer a seeker need; the map supplies real values later.</summary>
    public interface IEnvironmentSampler
    {
        /// <summary>Instant level in [0, 1] for an environment-driven need, or null to use the need's default.</summary>
        float? InstantLevelFor(NeedDef need, Pawn pawn);
    }
}
