using System;
using SimWorld.Health;

namespace SimWorld.Stats
{
    /// <summary>
    /// One capacity's contribution to a stat's final value (RimWorld: <c>RimWorld.PawnCapacityFactor</c>), e.g.
    /// <c>RestRateMultiplier</c>'s BloodPumping/Metabolism/Breathing at weight 0.3 each. See
    /// <see cref="StatWorker.GetValueUnfinalized"/> for how <see cref="StatDef.capacityFactors"/> is applied.
    /// </summary>
    public class PawnCapacityFactor
    {
        public PawnCapacityDef capacity = null!;
        public float weight = 1f;
        public float max = 9999f;
        public bool useReciprocal;
        public float allowedDefect;

        private const float MaxReciprocalFactor = 5f;

        /// <summary>Ported 1:1 from RimWorld's <c>PawnCapacityFactor.GetFactor</c>.</summary>
        public float GetFactor(float capacityEfficiency)
        {
            float num = capacityEfficiency;
            if (allowedDefect != 0f && num < 1f)
            {
                num = GenMath.InverseLerp(0f, 1f - allowedDefect, num);
            }
            if (num > max)
            {
                num = max;
            }
            if (useReciprocal)
            {
                num = Math.Abs(num) < 0.001f ? MaxReciprocalFactor : Math.Min(1f / num, MaxReciprocalFactor);
            }
            return num;
        }
    }
}
