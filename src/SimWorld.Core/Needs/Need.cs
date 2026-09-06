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
