using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Needs
{
    public enum HungerCategory
    {
        Fed,
        Hungry,
        UrgentlyHungry,
        Starving,
    }

    /// <summary>
    /// Nutrition (RimWorld: <c>RimWorld.Need_Food</c>). Falls 1.6/day at the species hunger rate, slower once
    /// hungry; capacity scales with body size. Starvation damage is a health-system hook.
    /// </summary>
    public class Need_Food : Need
    {
        /// <summary>1.6 nutrition per day at hunger rate 1.</summary>
        public const float BaseFoodFallPerTick = 1.6f / GenDate.TicksPerDay;

        private int lastNonStarvingTick = -99999;

        public Need_Food(Pawn pawn) : base(pawn)
        {
        }

        public override float MaxLevel => pawn.BodySize;

        public float NutritionWanted => MaxLevel - CurLevel;

        public float PercentageThreshHungry => pawn.RaceProps.foodLevelPercentageWantEat;

        public float PercentageThreshUrgentlyHungry => pawn.RaceProps.foodLevelPercentageWantEat * 0.5f;

        public HungerCategory CurCategory
        {
            get
            {
                float pct = CurLevelPercentage;
                if (pct <= 0f) return HungerCategory.Starving;
                if (pct < PercentageThreshUrgentlyHungry) return HungerCategory.UrgentlyHungry;
                if (pct < PercentageThreshHungry) return HungerCategory.Hungry;
                return HungerCategory.Fed;
            }
        }

        public bool Starving => CurCategory == HungerCategory.Starving;

        public int TicksStarving => Starving ? Find.TickManager.TicksGame - lastNonStarvingTick : 0;

        public float FoodFallPerTick => FoodFallPerTickAssumingCategory(CurCategory);

        /// <summary>Hunger slows as the stomach empties: ×0.5 hungry, ×0.25 urgently, ×0.15 starving.</summary>
        public float FoodFallPerTickAssumingCategory(HungerCategory category)
        {
            float rate = BaseFoodFallPerTick * pawn.HungerRate;
            switch (category)
            {
                case HungerCategory.Fed: return rate;
                case HungerCategory.Hungry: return rate * 0.5f;
                case HungerCategory.UrgentlyHungry: return rate * 0.25f;
                default: return rate * 0.15f;
            }
        }

        public override void SetInitialLevel()
        {
            CurLevelPercentage = 0.8f;
        }

        public override void NeedInterval()
        {
            if (!IsFrozen)
            {
                CurLevel -= FoodFallPerTick * IntervalTicks;
            }
            if (!Starving)
            {
                lastNonStarvingTick = Find.TickManager.TicksGame;
            }
            if (!IsFrozen)
            {
                pawn.Notify_StarvationInterval(Starving);
            }
        }

        /// <summary>O(1) bulk equivalent of <see cref="NeedInterval"/> (see the base class doc): holds the
        /// *current* category's fall rate constant across <paramref name="elapsedTicks"/> rather than
        /// replaying category transitions tick by tick — an approximation only across a span long enough to
        /// cross a hunger-category threshold mid-span.</summary>
        public override void NeedIntervalBulk(int elapsedTicks)
        {
            if (elapsedTicks <= 0) return;
            if (!IsFrozen)
            {
                CurLevel -= FoodFallPerTick * elapsedTicks;
            }
            if (!Starving)
            {
                lastNonStarvingTick = Find.TickManager.TicksGame;
            }
            if (!IsFrozen)
            {
                pawn.Notify_StarvationInterval(Starving);
            }
        }

        /// <summary>Eats <paramref name="nutrition"/> units; overflow is wasted.</summary>
        public void Eat(float nutrition)
        {
            CurLevel += nutrition;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastNonStarvingTick, "lastNonStarvingTick", -99999);
        }
    }
}
