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
            NeedIntervalBulk(1);
        }

        /// <summary>Decays every tolerance by <paramref name="slices"/> intervals' worth in one pass — O(#kinds
        /// held), never O(slices), so an Interval-tier citizen's coarse tick costs the same regardless of how
        /// many <see cref="Need.IntervalTicks"/>-sized slices the elapsed span holds.</summary>
        public void NeedIntervalBulk(int slices)
        {
            if (slices <= 0 || tolerances.Count == 0) return;
            float decay = ToleranceDecayPerInterval * slices;
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

        /// <summary>Decay eases when joy is already low so deprivation is gradual.</summary>
        public float FallPerInterval
        {
            get
            {
                switch (CurCategory)
                {
                    case JoyCategory.Empty: return 0f;
                    case JoyCategory.VeryLow: return BaseFallPerInterval * 0.4f;
                    case JoyCategory.Low: return BaseFallPerInterval * 0.7f;
                    case JoyCategory.Satisfied: return BaseFallPerInterval;
                    case JoyCategory.High: return BaseFallPerInterval * 1.4f;
                    default: return BaseFallPerInterval * 2f;
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
        /// by the slice count instead of raw ticks, holding the current joy-category rate constant across the
        /// span.</summary>
        public override void NeedIntervalBulk(int elapsedTicks)
        {
            if (elapsedTicks <= 0) return;
            int slices = elapsedTicks / IntervalTicks;
            if (slices <= 0) return;
            if (!IsFrozen)
            {
                CurLevel -= FallPerInterval * slices;
            }
            tolerances.NeedIntervalBulk(slices);
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
