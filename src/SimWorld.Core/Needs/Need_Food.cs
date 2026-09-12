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

        /// <summary>
        /// How much nutrition this stomach holds (RimWorld: <c>Need_Food.MaxLevel</c> is
        /// <c>pawn.BodySize * pawn.ageTracker.CurLifeStage.foodMaxFactor</c>). Body size alone is not the
        /// answer: a life stage scales the two independently, so a puppy that is 0.4 of a dog by size eats
        /// 0.5 of a dog's meal, and the factor is what says so.
        ///
        /// <para/><b>Read live, never cached, and that is the growth edge.</b> The level a pawn carries is
        /// absolute nutrition; the percentage every hunger category is decided from is that level over this
        /// maximum. So when a pup becomes a dog the same nutrition inside it becomes a smaller fraction of a
        /// bigger stomach and it gets hungry — which is the behaviour, not a rounding artefact. Shrinking the
        /// other way (a race whose later stage eats less, or a debug age change) would leave the level above
        /// the new ceiling, so <see cref="Pawn_NeedsTracker.Notify_LifeStageStarted"/> re-clamps every need
        /// at the crossing rather than letting a percentage over 100% escape.
        /// </summary>
        public override float MaxLevel => pawn.BodySize * (pawn.ageTracker?.CurLifeStage?.foodMaxFactor ?? 1f);

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
        /// cross a hunger-category threshold mid-span.
        ///
        /// <para/><b>Known defect, measured twice and deliberately left standing twice: starvation is reported
        /// once per bulk call rather than once per <see cref="IntervalTicks"/> slice.</b> The per-tick path
        /// above calls <see cref="Pawn.Notify_StarvationInterval"/> every 150 ticks; this calls it once for a
        /// whole elapsed span, which at Interval tier's Long-tick cadence (2,000 ticks = 13⅓ slices) makes
        /// <c>Malnutrition</c> accrue — and heal — about 13× slower than for the same citizen at Full. That
        /// breaks this method's own contract ("over a span its rate is constant across, it must land where the
        /// per-interval path would have") and spec §11.1's rule for the abstract clock with it. The fix is two
        /// lines: scale by <c>elapsedTicks / (float)IntervalTicks</c> in one pass, the shape
        /// <see cref="JoyToleranceSet.NeedIntervalBulk"/> already uses.
        ///
        /// <para/><b>Attempt 1 (reverted): "correcting the clock without correcting the food kills the
        /// world."</b> At the honest rate a citizen with nothing to eat dies of hunger in 8.9 days
        /// (<c>HealthTuning.MalnutritionSeverityPerInterval</c>, 0.113/day, lethal at 1), and off a map the
        /// only food there is is <c>Settlement.Stores</c> (<c>Economy.SettlementLarder</c>) — which at the
        /// time <b>nothing at civilization scale produced into</b>: every producer in this port (foraging,
        /// farming, cooking, hunting) is map-side, and the one abstract producer, <c>Crafting.Guild</c>, ships
        /// a recipe that cuts stone. Every settlement nobody had opened would have emptied its founding
        /// rations and died within a fortnight. That attempt named its own precondition — abstract production
        /// — and recorded the consequence it measured: <c>God.AttentionBudgetTests</c>' century of demography
        /// falling from ~1,500 citizens to ~880.
        ///
        /// <para/><b>Attempt 2 (reverted): the precondition it named is now met, and the century still
        /// falls.</b> <c>Economy.SettlementSubsistence</c> landed — a settlement with no map grows food into
        /// its own ledger on the long tick, and an unwatched settlement now carries days of food and zero
        /// <c>Malnutrition</c> over a run. Re-applied and re-measured against that same test, the century's
        /// five seeds came out <b>884 / 771 / 759 / 796 / 732</b>, against the ~1,500 its premise needs and
        /// against the five it passes with (the premise is "some century outgrows the Full-tier budget several
        /// times over"). Same failure, one batch later, with the stated blocker removed.
        ///
        /// <para/><b>So the blocker was never food, and naming it correctly is the whole value of this second
        /// attempt.</b> It is the <i>span</i>: <c>Pawns.Pawn_TierTracker.ApplyElapsed</c> calls this once with
        /// the whole time since a citizen was last brought current, and <b>nothing can eat inside a bulk
        /// call</b> — <c>Economy.SettlementLarder</c> feeds on its own gated pass, so a span longer than that
        /// pass is hunger with no opportunity to answer it, however full the ledger is. In a running game the
        /// span is one Long tick (2,000 ticks), because a coarse-tier citizen sits on the Long tick list and
        /// the larder runs on that same cadence, and the correction would be harmless there. In
        /// <c>AttentionBudgetTests</c> the span is a <b>year</b> — that test advances the clock a year at a
        /// time with <c>DebugSetTicksGame</c> and ages citizens with <c>AgeTickMothballed</c> without ticking
        /// anybody, so the economy never runs — and one call then charges 24,000 slices at once: severity
        /// <b>6.8</b> against a lethal 1. Every citizen the budget demotes and later re-promotes dies of a
        /// hunger the simulation never gave them a chance to answer.
        ///
        /// <para/><b>What has to land first, stated so the third attempt does not have to rediscover it.</b>
        /// Either (a) the catch-up span is bounded — <c>ApplyElapsed</c> either walks a long gap in
        /// larder-sized steps or declines to charge starvation for time in which the citizen was not being
        /// simulated at all, which is a change at the <c>Pawns</c>/<c>Economy</c> seam; or (b)
        /// <c>AttentionBudgetTests</c>' century advances through the tick loop rather than around it, which is
        /// a change to that test. A cap inside this method would make the test pass and would be inventing
        /// tiering policy in a need class, so it is named here and not taken.</summary>
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
