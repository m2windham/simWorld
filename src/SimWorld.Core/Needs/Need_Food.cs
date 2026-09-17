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
        /// <para/><b>The starvation clock is honest now, at the third attempt, and the two that were reverted
        /// are worth keeping on the record because each one found the next constraint.</b> The defect was
        /// that starvation was reported once per bulk call rather than once per <see cref="IntervalTicks"/>
        /// slice, so a citizen at Interval accrued — and healed — <c>Malnutrition</c> about 13x slower than
        /// the same citizen at Full, breaking this method's own contract and spec §11.1's rule for the
        /// abstract clock with it.
        ///
        /// <para/><b>Attempt 1 named a precondition: there was nothing to eat.</b> Correcting the clock while
        /// no abstract producer existed emptied every unopened settlement's founding rations inside a
        /// fortnight, because every producer in this port — foraging, farming, cooking, hunting — is map-side.
        /// <c>God.AttentionBudgetTests</c>' century fell from ~1,500 citizens to ~880.
        ///
        /// <para/><b>Attempt 2 met that precondition and still failed, which is what made it useful.</b>
        /// <c>Economy.SettlementSubsistence</c> landed and an unwatched settlement now carries days of food
        /// and zero <c>Malnutrition</c> over a run — yet the century's five seeds came out
        /// <b>884 / 771 / 759 / 796 / 732</b>. Same failure, one batch later, with the stated blocker gone.
        /// So the blocker was never food. <b>It was the span.</b>
        /// <c>Pawns.Pawn_TierTracker</c>'s catch-up called this once with the whole time since a citizen was
        /// last brought current, and <b>nothing can eat inside a bulk call</b> — <c>Economy.SettlementLarder</c>
        /// feeds on its own gated pass, so a span longer than that pass is hunger with no opportunity to
        /// answer it, however full the ledger is. In <c>AttentionBudgetTests</c> that span is a <i>year</i>,
        /// and one call charged 24,000 slices at once: severity <b>6.8</b> against a lethal 1.
        ///
        /// <para/><b>Attempt 3 bounded the span where the span is decided</b>, which is
        /// <see cref="Pawns.TieringTuning.MaxNeedCatchUpTicks"/> — one long tick, the cadence the abstract
        /// economy actually runs at. A clamp *here* would have made the same measurement come out right while
        /// inventing tiering policy in a need class, which is why attempts 1 and 2 both refused to take it,
        /// and why it is still the wrong place. With the span bounded, the scaling below is safe and the
        /// century comes back: <b>1523 / 868 / 1154 / 1155 / 1010</b>, every seed grown, the Full tier pinned
        /// flat at its budget.
        ///
        /// <para/>Note what is <i>not</i> bounded, because the asymmetry is the whole idea: age takes the full
        /// span. Nobody owed a citizen an opportunity to grow older. They were owed one to eat.</summary>
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
                // Scaled, so a span lands where the per-interval path would have. Safe now only because
                // Pawns.TieringTuning.MaxNeedCatchUpTicks bounds what a catch-up may hand us: unbounded, this
                // same line charged 24,000 slices in one call and killed a century of citizens outright.
                pawn.Notify_StarvationInterval(Starving, elapsedTicks / (float)IntervalTicks);
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
