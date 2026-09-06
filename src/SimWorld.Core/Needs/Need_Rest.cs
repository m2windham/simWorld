using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Needs
{
    public enum RestCategory
    {
        Rested,
        Tired,
        VeryTired,
        Exhausted,
    }

    /// <summary>
    /// Sleep (RimWorld: <c>RimWorld.Need_Rest</c>). Falls while awake at a rate that eases as the pawn tires,
    /// rises while asleep by bed effectiveness. Time spent at zero drives exhaustion collapse.
    /// </summary>
    public class Need_Rest : Need
    {
        public const float ThreshTired = 0.28f;
        public const float ThreshVeryTired = 0.14f;
        public const float BaseRestGainPerTick = 3.8E-05f;
        public const float BaseRestFallPerTick = 1.58333332E-05f;

        private int ticksAtZero;

        /// <summary>Effectiveness of the current bed; 1 for the ground. Set by the sleeping job.</summary>
        public float lastRestEffectiveness = 1f;

        public Need_Rest(Pawn pawn) : base(pawn)
        {
        }

        public RestCategory CurCategory
        {
            get
            {
                float level = CurLevel;
                if (level < 0.01f) return RestCategory.Exhausted;
                if (level < ThreshVeryTired) return RestCategory.VeryTired;
                if (level < ThreshTired) return RestCategory.Tired;
                return RestCategory.Rested;
            }
        }

        public int TicksAtZero => ticksAtZero;

        public bool Resting => pawn.Asleep;

        public float RestFallPerTick => BaseRestFallPerTick * RestFallFactor * pawn.RestFallFactorFromHealth;

        /// <summary>Tired pawns lose rest more slowly: ×0.7 tired, ×0.3 very tired, ×0.6 exhausted.</summary>
        public float RestFallFactor
        {
            get
            {
                switch (CurCategory)
                {
                    case RestCategory.Rested: return 1f;
                    case RestCategory.Tired: return 0.7f;
                    case RestCategory.VeryTired: return 0.3f;
                    default: return 0.6f;
                }
            }
        }

        public float RestGainPerTick => BaseRestGainPerTick * lastRestEffectiveness * pawn.RestRateMultiplier;

        public override void SetInitialLevel()
        {
            CurLevel = Rand.Range(0.9f, 1f);
        }

        public override void NeedInterval()
        {
            if (!IsFrozen)
            {
                if (Resting)
                {
                    CurLevel += RestGainPerTick * IntervalTicks;
                }
                else
                {
                    CurLevel -= RestFallPerTick * IntervalTicks;
                }
            }
            if (CurLevel < 0.0001f)
            {
                ticksAtZero += IntervalTicks;
            }
            else
            {
                ticksAtZero = 0;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksAtZero, "ticksAtZero");
            Scribe_Values.Look(ref lastRestEffectiveness, "lastRestEffectiveness", 1f);
        }
    }
}
