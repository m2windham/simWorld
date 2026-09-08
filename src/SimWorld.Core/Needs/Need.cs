using System;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Needs
{
    /// <summary>
    /// A 0–1 gauge that drifts over time and drives behaviour (RimWorld: <c>RimWorld.Need</c>).
    /// <see cref="NeedInterval"/> runs every 150 ticks per pawn, staggered by the pawn's hash.
    /// </summary>
    public class Need : IExposable
    {
        public const int IntervalTicks = 150;

        protected readonly Pawn pawn;
        public NeedDef def = null!;
        protected float curLevelInt;

        public Need(Pawn pawn)
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

        /// <summary>Called every <see cref="IntervalTicks"/> ticks.</summary>
        public virtual void NeedInterval()
        {
            if (!IsFrozen && def.fallPerDay > 0f)
            {
                CurLevel -= def.fallPerDay / GenDate.TicksPerDay * IntervalTicks;
            }
        }

        /// <summary>
        /// Bulk-equivalent of <see cref="NeedInterval"/> for a pawn ticking at Interval tier
        /// (<c>docs/spec/simworld-spec.md</c> §11.3): "the full state exists... advances only on the rare/long
        /// buckets", never per tick. The naive approach — replaying <see cref="NeedInterval"/> once per whole
        /// <see cref="IntervalTicks"/> slice in <paramref name="elapsedTicks"/> — is exact but defeats the
        /// tier's own performance point: at the Long-tick cadence (2000 ticks) that is ~13 calls every coarse
        /// tick, which measured out to needs costing Interval-tier citizens nearly as much as continuous
        /// per-tick simulation (`docs/perf/baseline.md` §2 already shows needs at 12.5% of Full's per-pawn
        /// cost; replaying it wholesale reproduces that same cost instead of amortizing it). This default
        /// instead applies <paramref name="elapsedTicks"/> in one step, holding <c>def.fallPerDay</c> constant
        /// across the whole span — exact for the base class, since nothing here depends on the need's own
        /// current level. A subclass whose fall/rise rate depends on its own level or category (<see cref="Need_Food"/>,
        /// <see cref="Need_Rest"/>, <see cref="Need_Joy"/>, <see cref="Need_Seeker"/>) overrides this to hold
        /// its own *current* rate constant across the span instead — an approximation that only drifts from
        /// true per-tick simulation across a span long enough to cross a rate/category boundary, and Interval
        /// tier's own Long-tick cadence keeps that span short in practice (see <see cref="Pawns.Pawn_TierTracker.CoarseTick"/>).
        /// Not a sourced numeric method, only the right shape, per <c>CLAUDE.md</c>'s rule for un-sourced numbers.
        /// </summary>
        public virtual void NeedIntervalBulk(int elapsedTicks)
        {
            if (elapsedTicks <= 0) return;
            if (!IsFrozen && def.fallPerDay > 0f)
            {
                CurLevel -= def.fallPerDay / GenDate.TicksPerDay * elapsedTicks;
            }
        }

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
