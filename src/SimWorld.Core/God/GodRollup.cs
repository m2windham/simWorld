using System;
using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.God
{
    /// <summary>
    /// A cheap aggregate of civilization state for the god view (<c>docs/spec/simworld-spec.md</c> §10: "the
    /// god view reads rollups — public mood, population health, industry — rather than opening every person").
    /// RimWorld has no equivalent (a RimWorld colony is small enough the player just looks at it); this is
    /// SimWorld's own translation, sized for populations §11.3's tiering makes possible.
    /// <para/>
    /// <b>Population is supplied by the caller</b> as an <see cref="IReadOnlyList{T}"/> of <see cref="Pawn"/> —
    /// this class owns no registry of its own. A <c>Settlement</c> entity is being built in a parallel lane
    /// right now and is not in this worktree; once merged, a settlement's own population is what should feed
    /// <see cref="Recompute"/> instead of a caller-assembled list. Until then, a test (or a future game loop)
    /// passes the living population directly, the same contract <c>FamilyManager.DemographyTick</c> already
    /// uses for its own population-wide sweep.
    /// <para/>
    /// <b>Tier-aware by construction, not by an if-skip</b>: a <see cref="PawnTier.Statistical"/> citizen's
    /// contribution to <see cref="MeanHealth"/> comes from <see cref="Pawn_TierTracker.SampledHealthFraction"/>
    /// — its real <c>Pawn_HealthTracker</c>/hediff set is never read. Opening every citizen's hediff set to
    /// answer "how healthy is the civilization" would defeat the entire point of §11.3's tiering (a
    /// Statistical citizen exists so that civilization-scale questions do not cost per-citizen depth); mood and
    /// the food readout need no such split because <see cref="Pawn_TierTracker.CoarseTick"/> already keeps a
    /// Statistical citizen's real <c>Need</c> objects current via cohort sampling (see that class), so reading
    /// them here is already cheap and already real, whatever tier produced the number.
    /// </summary>
    public sealed class GodRollup
    {
        /// <summary>Sentinel meaning "never recomputed" — the same convention <c>SituationalThoughtHandler</c>
        /// uses for its own recompute cadence.</summary>
        private const int NeverRecomputed = int.MinValue;

        private int lastRecomputeTick = NeverRecomputed;

        public int TotalPopulation { get; private set; }
        public int FullCount { get; private set; }
        public int IntervalCount { get; private set; }
        public int StatisticalCount { get; private set; }

        /// <summary>Mean of <c>Need_Mood.CurLevelPercentage</c> across every counted citizen, 0-1.</summary>
        public float MeanMood { get; private set; }

        /// <summary>
        /// Mean fraction-of-health across every counted citizen, 0-1 — real
        /// <c>Pawn_HealthTracker.summaryHealth.SummaryHealthPercent</c> for Full/Interval, the cohort-sampled
        /// <see cref="Pawn_TierTracker.SampledHealthFraction"/> for Statistical. See the class doc.
        /// </summary>
        public float MeanHealth { get; private set; }

        /// <summary>Mean of <c>Need_Food.CurLevelPercentage</c> — the "food" half of the food/industry readout.</summary>
        public float MeanFoodNeed { get; private set; }

        /// <summary>
        /// The "industry" half of the food/industry readout: mean level across the production skills
        /// (Construction, Mining, Crafting) — a proxy for how much a civilization's citizens could build if put
        /// to it, in the absence of a real production/output ledger. A Settlement entity's own stores and
        /// throughput (once merged — see the class doc) is the honest long-term source for this number; this
        /// is the cheapest real stand-in available from citizens alone.
        /// </summary>
        public float MeanIndustrySkill { get; private set; }

        public EraDef? CurrentEra { get; private set; }

        public float EraProgress { get; private set; }

        /// <summary>Recomputes unconditionally and stamps the recompute tick. Prefer <see cref="RecomputeIfNeeded"/>
        /// from a caller that ticks repeatedly; call this directly only when a fresh number is needed right now
        /// (a test, or the instant the god view opens).</summary>
        public void Recompute(IReadOnlyList<Pawn> population)
        {
            if (population == null) throw new ArgumentNullException(nameof(population));

            int total = 0, full = 0, interval = 0, statistical = 0;
            float moodSum = 0f, healthSum = 0f, foodSum = 0f, industrySum = 0f;
            int moodCount = 0, foodCount = 0;

            for (int i = 0; i < population.Count; i++)
            {
                Pawn? pawn = population[i];
                if (pawn == null || pawn.Dead || !pawn.RaceProps.Humanlike) continue;
                total++;

                switch (pawn.tier.Tier)
                {
                    case PawnTier.Full: full++; break;
                    case PawnTier.Interval: interval++; break;
                    case PawnTier.Statistical: statistical++; break;
                }

                if (pawn.needs.mood != null)
                {
                    moodSum += pawn.needs.mood.CurLevelPercentage;
                    moodCount++;
                }
                if (pawn.needs.food != null)
                {
                    foodSum += pawn.needs.food.CurLevelPercentage;
                    foodCount++;
                }

                // The one line in this whole class that must never become `pawn.health.summaryHealth...`
                // unconditionally — see the class doc and GodTests' own test proving it.
                healthSum += pawn.tier.Tier == PawnTier.Statistical
                    ? pawn.tier.SampledHealthFraction
                    : pawn.health.summaryHealth.SummaryHealthPercent;

                industrySum += MeanIndustrySkillOf(pawn);
            }

            TotalPopulation = total;
            FullCount = full;
            IntervalCount = interval;
            StatisticalCount = statistical;
            MeanMood = moodCount > 0 ? moodSum / moodCount : 0f;
            MeanHealth = total > 0 ? healthSum / total : 1f;
            MeanFoodNeed = foodCount > 0 ? foodSum / foodCount : 0f;
            MeanIndustrySkill = total > 0 ? industrySum / total : 0f;

            CurrentEra = Find.ResearchManager.CurrentEra;
            EraProgress = CurrentEra?.Progress ?? 0f;

            lastRecomputeTick = Find.TickManager.TicksGame;
        }

        /// <summary>Recomputes only once <paramref name="cadenceTicks"/> have passed since the last recompute
        /// (or never having recomputed at all) — the "cached result... not per tick per reader" half of the
        /// brief. Pass <see cref="GodTuning.GodTickIntervalTicks"/> for the god layer's own cadence.</summary>
        public void RecomputeIfNeeded(IReadOnlyList<Pawn> population, int cadenceTicks)
        {
            int now = Find.TickManager.TicksGame;
            if (lastRecomputeTick != NeverRecomputed && now - lastRecomputeTick < cadenceTicks) return;
            Recompute(population);
        }

        /// <summary>Forces the next <see cref="RecomputeIfNeeded"/> call to recompute regardless of cadence —
        /// the explicit "dirty" half of the brief, the same idiom as
        /// <c>SituationalThoughtHandler.Notify_SituationalThoughtsDirty</c>.</summary>
        public void Notify_Dirty() => lastRecomputeTick = NeverRecomputed;

        private static float MeanIndustrySkillOf(Pawn pawn)
        {
            SkillRecord? construction = pawn.skills?.GetSkill(SkillDefOf.Construction);
            SkillRecord? mining = pawn.skills?.GetSkill(SkillDefOf.Mining);
            SkillRecord? crafting = pawn.skills?.GetSkill(SkillDefOf.Crafting);

            int n = 0;
            float sum = 0f;
            if (construction != null) { sum += construction.Level; n++; }
            if (mining != null) { sum += mining.Level; n++; }
            if (crafting != null) { sum += crafting.Level; n++; }
            return n > 0 ? sum / n : 0f;
        }
    }
}
