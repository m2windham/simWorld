using System;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Needs
{
    /// <summary>
    /// A 0–1 gauge that drifts over time and drives behaviour (RimWorld: <c>RimWorld.Need</c>).
    /// <see cref="NeedInterval"/> runs every 150 ticks per pawn, staggered by the pawn's hash.
    ///
    /// <para/><b>There is no generic decay here, and there was never meant to be one.</b> This class used to
    /// carry a base <see cref="NeedInterval"/> that decayed by <c>def.fallPerDay</c>, guarded by
    /// <c>if (!IsFrozen &amp;&amp; def.fallPerDay &gt; 0f)</c>. Both halves of that guard were dead: no shipped
    /// <see cref="NeedDef"/> sets <c>fallPerDay</c>, and no shipped <c>needClass</c> reaches the base method
    /// anyway — all eight override it. In RimWorld the method is <c>public abstract void NeedInterval();</c>
    /// and <c>fallPerDay</c> is read by exactly one need class, <c>Need_Chemical</c> (see
    /// <see cref="NeedDef.fallPerDay"/>), which this port has not built. The invented fallback therefore
    /// modelled nothing, and its <c>&gt; 0f</c> guard made "this need has no generic rate" and "nobody filled
    /// the rate in" the same silent no-op. Declaring both interval methods abstract restores RimWorld's shape
    /// and makes the omission impossible: a new need cannot inherit a decay that quietly does nothing, it has
    /// to say what it does.
    /// </summary>
    public abstract class Need : IExposable
    {
        public const int IntervalTicks = 150;

        protected readonly Pawn pawn;
        public NeedDef def = null!;
        protected float curLevelInt;

        protected Need(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public Pawn Pawn => pawn;

        public virtual float MaxLevel => 1f;

        public float CurLevel
        {
            get => curLevelInt;
            set => curLevelInt = GenMath.Clamp(value, 0f, MaxLevel);
        }

        public float CurLevelPercentage
        {
            get => CurLevel / MaxLevel;
            set => CurLevel = value * MaxLevel;
        }

        /// <summary>Where the gauge wants to be right now; -1 when the need has no target.</summary>
        public virtual float CurInstantLevel => -1f;

        public virtual bool ShowOnNeedList => def.showOnNeedList;

        public string LabelCap => def.LabelCap;

        /// <summary>
        /// While true the need holds where it is (RimWorld: <c>Need.IsFrozen</c>). Three of RimWorld's four
        /// reasons are ported: the pawn is suspended (in a pod or otherwise out of play), the need freezes
        /// while its owner sleeps (<see cref="NeedDef.freezeWhileSleeping"/> — Joy and the environment needs),
        /// or it freezes during a mental break (<see cref="NeedDef.freezeInMentalState"/>, which no shipped
        /// need sets).
        /// <para/>
        /// <b>Deliberately not ported: RimWorld's <c>IsPawnInteractableOrVisible</c> clause</b>, which freezes
        /// every need of a pawn that is neither spawned, nor a caravan member, nor travelling in a pod. Here
        /// an unspawned pawn still gets hungry, which is what this port's off-map citizens (the Interval and
        /// Statistical tiers, <c>docs/spec/simworld-spec.md</c> §11.3) need in order to keep being simulated
        /// at all — RimWorld has no such tier, it simply stops simulating a pawn that leaves the map.
        /// </summary>
        public virtual bool IsFrozen =>
            pawn.Suspended
            || (def.freezeWhileSleeping && !pawn.Awake())
            || (def.freezeInMentalState && pawn.InMentalState)
            || IsFrozenExtra;

        protected virtual bool IsFrozenExtra => false;

        public virtual void SetInitialLevel()
        {
            CurLevel = def.baseLevel;
        }

        /// <summary>
        /// Called every <see cref="IntervalTicks"/> ticks. Abstract exactly as RimWorld's is: a need's rate is
        /// its own class's business, and there is no inherited default that can silently do nothing (see the
        /// class remarks).
        /// </summary>
        public abstract void NeedInterval();

        /// <summary>
        /// Bulk-equivalent of <see cref="NeedInterval"/> for a pawn ticking at Interval tier
        /// (<c>docs/spec/simworld-spec.md</c> §11.3): "the full state exists... advances only on the rare/long
        /// buckets", never per tick. The naive approach — replaying <see cref="NeedInterval"/> once per whole
        /// <see cref="IntervalTicks"/> slice in <paramref name="elapsedTicks"/> — is exact but defeats the
        /// tier's own performance point: at the Long-tick cadence (2000 ticks) that is ~13 calls every coarse
        /// tick, which measured out to needs costing Interval-tier citizens nearly as much as continuous
        /// per-tick simulation (`docs/perf/baseline.md` §2 already shows needs at 12.5% of Full's per-pawn
        /// cost; replaying it wholesale reproduces that same cost instead of amortizing it).
        /// <para/>
        /// Every implementation instead applies the whole elapsed span in one step, holding its <i>current</i>
        /// rate — hunger/rest/joy category, or seeker target — constant across it: an approximation that only
        /// drifts from true per-tick simulation across a span long enough to cross a rate boundary, and
        /// Interval tier's own Long-tick cadence keeps that span short in practice (see
        /// <see cref="Pawns.Pawn_TierTracker.CoarseTick"/>). Two rules every implementation owes its caller,
        /// both pinned by <c>NeedsTests</c>:
        /// <list type="bullet">
        /// <item><description>It must consume the <i>whole</i> span, including any part-interval remainder —
        /// the caller advances its own clock by <paramref name="elapsedTicks"/> regardless, so whatever an
        /// implementation rounds away is lost for good and the need drifts permanently slower than its
        /// Full-tier twin.</description></item>
        /// <item><description>Over a span its rate is constant across, it must land where the per-interval
        /// path would have.</description></item>
        /// </list>
        /// Abstract for the same reason as <see cref="NeedInterval"/>: an inherited no-op here is a need that
        /// silently stops existing for every citizen the player is not looking at.
        /// </summary>
        public abstract void NeedIntervalBulk(int elapsedTicks);

        public virtual void ExposeData()
        {
            NeedDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref curLevelInt, "curLevel");
        }

        public override string ToString() => (def?.defName ?? GetType().Name) + " " + CurLevelPercentage.ToString("P0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
