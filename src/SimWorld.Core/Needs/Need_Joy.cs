using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Needs
{
    public enum JoyCategory
    {
        Empty,
        VeryLow,
        Low,
        Satisfied,
        High,
        Extreme,
    }

    /// <summary>A flavour of recreation (RimWorld: <c>RimWorld.JoyKindDef</c>); tolerance builds per kind.</summary>
    public class JoyKindDef : Def
    {
        /// <summary>Kinds that don't build tolerance (e.g. none in vanilla; here for content).</summary>
        public bool neverBuildsTolerance;
    }

    /// <summary>
    /// Per-kind tolerance that dampens repeated recreation (RimWorld: <c>RimWorld.JoyToleranceSet</c>).
    /// Each unit of joy gained adds 0.65 tolerance; tolerance decays 0.0003 per interval (~0.12/day).
    /// </summary>
    public class JoyToleranceSet : IExposable
    {
        public const float ToleranceGainPerJoy = 0.65f;
        public const float ToleranceDecayPerInterval = 0.0003f;

        private Dictionary<JoyKindDef, float> tolerances = new Dictionary<JoyKindDef, float>();

        public float this[JoyKindDef kind] => tolerances.TryGetValue(kind, out float t) ? t : 0f;

        public float JoyFactorFromTolerance(JoyKindDef kind) => 1f - this[kind];

        public bool BoredOf(JoyKindDef kind) => this[kind] >= 0.5f;

        public void Notify_JoyGained(float amount, JoyKindDef kind)
        {
            if (kind == null) throw new ArgumentNullException(nameof(kind));
            if (kind.neverBuildsTolerance) return;
            tolerances[kind] = Math.Min(1f, this[kind] + amount * ToleranceGainPerJoy);
        }

        public void NeedInterval()
        {
            NeedIntervalBulk(1f);
        }

        /// <summary>Decays every tolerance by <paramref name="intervals"/> intervals' worth in one pass —
        /// O(#kinds held), never O(intervals), so an Interval-tier citizen's coarse tick costs the same
        /// regardless of how long the elapsed span is. Fractional on purpose: a coarse tick is 2000 ticks and
        /// an interval is 150, so a whole number of intervals would throw away 50 ticks of decay every single
        /// coarse tick (see <see cref="Need_Joy.NeedIntervalBulk"/>).</summary>
        public void NeedIntervalBulk(float intervals)
        {
            if (intervals <= 0f || tolerances.Count == 0) return;
            float decay = ToleranceDecayPerInterval * intervals;
            var keys = new List<JoyKindDef>(tolerances.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                float next = Math.Max(0f, tolerances[keys[i]] - decay);
                if (next <= 0f) tolerances.Remove(keys[i]);
                else tolerances[keys[i]] = next;
            }
        }

        public void ExposeData()
        {
            Dictionary<JoyKindDef, float>? d = tolerances;
            Scribe_Collections.Look(ref d, "tolerances", LookMode.Def, LookMode.Value);
            tolerances = d ?? new Dictionary<JoyKindDef, float>();
        }
    }

    /// <summary>
    /// Recreation (RimWorld: <c>RimWorld.Need_Joy</c>). Decays a fixed amount per interval scaled by category;
    /// gains are dampened by tolerance for the kind of activity.
    /// </summary>
    public class Need_Joy : Need
    {
        public const float ThreshVeryLow = 0.15f;
        public const float ThreshLow = 0.3f;
        public const float ThreshSatisfied = 0.7f;
        public const float ThreshHigh = 0.85f;
        public const float BaseFallPerInterval = 0.0015f;

        public JoyToleranceSet tolerances = new JoyToleranceSet();

        public Need_Joy(Pawn pawn) : base(pawn)
        {
        }

        public JoyCategory CurCategory
        {
            get
            {
                float level = CurLevel;
                if (level < 0.01f) return JoyCategory.Empty;
                if (level < ThreshVeryLow) return JoyCategory.VeryLow;
                if (level < ThreshLow) return JoyCategory.Low;
                if (level < ThreshSatisfied) return JoyCategory.Satisfied;
                if (level < ThreshHigh) return JoyCategory.High;
                return JoyCategory.Extreme;
            }
        }

        /// <summary>
        /// Decay eases in the two low bands so deprivation is gradual, and is flat everywhere else
        /// (RimWorld: <c>Need_Joy.FallPerInterval</c>, whose switch reads
        /// <c>Empty =&gt; 0.0015f, VeryLow =&gt; 0.0006f, Low =&gt; 0.00105f, Satisfied/High/Extreme =&gt; 0.0015f</c>
        /// — i.e. the base rate, ×0.4 and ×0.7).
        /// <para/>
        /// This used to read <c>Empty =&gt; 0</c>, <c>High =&gt; ×1.4</c> and <c>Extreme =&gt; ×2</c>, which is
        /// nobody's numbers: a well-entertained pawn lost recreation nearly twice as fast as RimWorld's does.
        /// </summary>
        public float FallPerInterval
        {
            get
            {
                switch (CurCategory)
                {
                    case JoyCategory.VeryLow: return BaseFallPerInterval * 0.4f;
                    case JoyCategory.Low: return BaseFallPerInterval * 0.7f;
                    default: return BaseFallPerInterval;
                }
            }
        }

        public void GainJoy(float amount, JoyKindDef kind)
        {
            if (kind == null) throw new ArgumentNullException(nameof(kind));
            if (amount <= 0f) return;
            amount *= tolerances.JoyFactorFromTolerance(kind);
            tolerances.Notify_JoyGained(amount, kind);
            CurLevel += amount;
        }

        public override void NeedInterval()
        {
            if (!IsFrozen)
            {
                CurLevel -= FallPerInterval;
            }
            tolerances.NeedInterval();
        }

        /// <summary>O(1) bulk equivalent (see the base class doc): <see cref="FallPerInterval"/> is already
        /// scaled to one <see cref="Need.IntervalTicks"/> slice rather than a per-tick rate, so this multiplies
        /// by the number of slices in the span instead of raw ticks, holding the current joy-category rate
        /// constant across it.
        /// <para/>
        /// <b>Fractional slices, deliberately.</b> This used to take <c>elapsedTicks / IntervalTicks</c> as an
        /// integer and return early below one whole slice, which silently dropped the remainder every time:
        /// the Interval tier calls this once per Long tick (2000 ticks = 13.33 slices) and then advances its
        /// clock by the full 2000, so 50 ticks of decay went missing on every single coarse tick and joy fell
        /// 2.5% slower for every citizen nobody was watching — the base class's first rule for this method.
        /// </summary>
        public override void NeedIntervalBulk(int elapsedTicks)
        {
            if (elapsedTicks <= 0) return;
            float intervals = elapsedTicks / (float)IntervalTicks;
            if (!IsFrozen)
            {
                CurLevel -= FallPerInterval * intervals;
            }
            tolerances.NeedIntervalBulk(intervals);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            JoyToleranceSet? t = tolerances;
            Scribe_Deep.Look(ref t, "tolerances");
            tolerances = t ?? new JoyToleranceSet();
        }
    }
}
