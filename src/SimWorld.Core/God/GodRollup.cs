using System;
using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Work;
using SimWorld.World;

namespace SimWorld.God
{
    /// <summary>
    /// A cheap aggregate of civilization state for the god view (<c>docs/spec/simworld-spec.md</c> §10: "the
    /// god view reads rollups — public mood, population health, industry — rather than opening every person").
    /// RimWorld has no equivalent (a RimWorld colony is small enough the player just looks at it); this is
    /// SimWorld's own translation, sized for populations §11.3's tiering makes possible.
    /// <para/>
    /// <b>Fed by a real <see cref="Settlement"/></b> (<see cref="Recompute(Settlement)"/>) or several, for a
    /// whole civilization (<see cref="Recompute(IReadOnlyList{Settlement})"/>) — or, for a test or a caller
    /// that already has one assembled, a bare population (<see cref="Recompute(IReadOnlyList{Pawn})"/>). A
    /// settlement's <see cref="Settlement.Citizens"/> (real <c>Pawn</c> objects, Full/Interval only) is walked
    /// exactly like the bare-population overload; its <see cref="Settlement.StatisticalPopulation"/> — a count
    /// with no <c>Pawn</c> per person, by that tier's own design — is folded in without walking anyone, via
    /// <see cref="AccumulateStatisticalCohort"/>. See that method's own doc, and each mean property's, for
    /// exactly how a cohort with no members to walk contributes to a mean.
    /// <para/>
    /// <b>Tier-aware by construction, not by an if-skip</b>: a <see cref="PawnTier.Statistical"/> citizen's
    /// contribution to <see cref="MeanHealth"/> comes from <see cref="Pawn_TierTracker.SampledHealthFraction"/>
    /// when a real <c>Pawn</c> exists for them, or from the same cohort-sampling idiom applied at settlement
    /// scale when it doesn't — its real <c>Pawn_HealthTracker</c>/hediff set is never read, and a settlement's
    /// bare Statistical count is never walked. Opening every citizen's hediffs to answer "how healthy is the
    /// civilization" would defeat the entire point of §11.3's tiering; mood and the food readout need no such
    /// split for a real <c>Pawn</c> because <see cref="Pawn_TierTracker.CoarseTick"/> already keeps a
    /// Statistical citizen's real <c>Need</c> objects current via cohort sampling (see that class) — reading
    /// them here is already cheap and already real, whatever tier produced the number.
    /// </summary>
    public sealed class GodRollup
    {
        /// <summary>Sentinel meaning "never recomputed" — the same convention <c>SituationalThoughtHandler</c>
        /// uses for its own recompute cadence.</summary>
        private const int NeverRecomputed = int.MinValue;

        /// <summary>
        /// Arbitrary, mutually-distinct tags folded into a settlement's cohort seed so each statistic samples
        /// independently from the same seed base — the same role <c>Pawn_TierTracker</c>'s own
        /// <c>0x4EA17BEEu</c> health tag plays for one citizen's sample; no meaning beyond distinctness.
        /// </summary>
        private const int MoodSampleTag = 1;
        private const int HealthSampleTag = 2;
        private const int FoodSampleTag = 3;
        private const int IndustrySampleTag = 4;

        private int lastRecomputeTick = NeverRecomputed;

        public int TotalPopulation { get; private set; }
        public int FullCount { get; private set; }
        public int IntervalCount { get; private set; }
        public int StatisticalCount { get; private set; }

        /// <summary>
        /// Population-weighted mean of <c>Need_Mood.CurLevelPercentage</c>, 0-1, over <see cref="TotalPopulation"/>
        /// — never just the citizens with a live <c>Pawn</c>. A Full/Interval citizen contributes their own real
        /// need level; each settlement's Statistical cohort contributes one cohort-sampled value (see
        /// <see cref="AccumulateStatisticalCohort"/>), weighted by its own population count, exactly as if every
        /// member of that cohort shared it — the "fold the cohort in with a stated sampled value" choice
        /// <c>docs/status.json</c>'s <c>god.settlement-population</c> item calls for, pinned by
        /// <c>GodTests.A_settlements_statistical_cohort_is_folded_into_the_means_by_a_weighted_sample</c>.
        /// </summary>
        public float MeanMood { get; private set; }

        /// <summary>
        /// Population-weighted mean fraction-of-health, 0-1, over <see cref="TotalPopulation"/> — real
        /// <c>Pawn_HealthTracker.summaryHealth.SummaryHealthPercent</c> for a Full/Interval citizen,
        /// <see cref="Pawn_TierTracker.SampledHealthFraction"/> for a Statistical citizen with a live
        /// <c>Pawn</c>, and one settlement-wide cohort sample (see <see cref="AccumulateStatisticalCohort"/>)
        /// weighted by count for a settlement's bare Statistical population, which has no <c>Pawn</c> to read
        /// either number from. See the class doc.
        /// </summary>
        public float MeanHealth { get; private set; }

        /// <summary>Population-weighted mean of <c>Need_Food.CurLevelPercentage</c> — the "food" half of the
        /// food/industry readout — over <see cref="TotalPopulation"/>, folding in each settlement's Statistical
        /// cohort by the same weighted sample as <see cref="MeanMood"/>.</summary>
        public float MeanFoodNeed { get; private set; }

        /// <summary>
        /// The "industry" half of the food/industry readout: population-weighted mean level across the
        /// production skills (Construction, Mining, Crafting) — a proxy for how much a civilization's citizens
        /// could build if put to it, in the absence of a real production/output ledger. A Settlement entity's
        /// own stores and throughput (once merged — see the class doc) is the honest long-term source for this
        /// number; this is the cheapest real stand-in available from citizens alone. A settlement's Statistical
        /// cohort has no <c>SkillRecord</c> to read, so it contributes a cohort-sampled skill level from
        /// <see cref="GodTuning.StatisticalIndustrySkillSampleMin"/>/<see cref="GodTuning.StatisticalIndustrySkillSampleMax"/>
        /// — SimWorld's own band, not sourced from RimWorld (see that constant's own doc for why it is
        /// deliberately low), weighted by count exactly like the other three means.
        /// </summary>
        public float MeanIndustrySkill { get; private set; }

        public EraDef? CurrentEra { get; private set; }

        public float EraProgress { get; private set; }

        /// <summary>Recomputes unconditionally and stamps the recompute tick. Prefer <see cref="RecomputeIfNeeded(IReadOnlyList{Pawn},int)"/>
        /// from a caller that ticks repeatedly; call this directly only when a fresh number is needed right now
        /// (a test, or the instant the god view opens).</summary>
        public void Recompute(IReadOnlyList<Pawn> population)
        {
            if (population == null) throw new ArgumentNullException(nameof(population));

            var acc = new Accumulator();
            for (int i = 0; i < population.Count; i++)
            {
                AccumulatePawn(population[i], ref acc);
            }
            Finalize(ref acc);
        }

        /// <summary>Recomputes from one settlement: its <see cref="Settlement.Citizens"/> walked like any other
        /// population, its <see cref="Settlement.StatisticalPopulation"/> folded in without walking anyone —
        /// see the class doc and <see cref="AccumulateStatisticalCohort"/>.</summary>
        public void Recompute(Settlement settlement)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));

            var acc = new Accumulator();
            AccumulateSettlement(settlement, ref acc);
            Finalize(ref acc);
        }

        /// <summary>
        /// Recomputes across every settlement of a civilization: each one's <see cref="Settlement.Citizens"/>
        /// walked, each one's <see cref="Settlement.StatisticalPopulation"/> folded in by one cohort sample —
        /// cost is O(settlements) + O(total Full/Interval citizens), never O(total Statistical population), so
        /// a civilization of many 40,000-person settlements costs the same per-settlement work a single one
        /// does. See <c>GodTests.Recomputing_over_a_large_statistical_settlement_enumerates_no_statistical_citizen</c>.
        /// </summary>
        public void Recompute(IReadOnlyList<Settlement> settlements)
        {
            if (settlements == null) throw new ArgumentNullException(nameof(settlements));

            var acc = new Accumulator();
            for (int i = 0; i < settlements.Count; i++)
            {
                AccumulateSettlement(settlements[i], ref acc);
            }
            Finalize(ref acc);
        }

        /// <summary>Recomputes only once <paramref name="cadenceTicks"/> have passed since the last recompute
        /// (or never having recomputed at all) — the "cached result... not per tick per reader" half of the
        /// brief. Pass <see cref="GodTuning.GodTickIntervalTicks"/> for the god layer's own cadence.</summary>
        public void RecomputeIfNeeded(IReadOnlyList<Pawn> population, int cadenceTicks)
        {
            if (!DueForRecompute(cadenceTicks)) return;
            Recompute(population);
        }

        /// <summary>Cadence-gated counterpart of <see cref="Recompute(Settlement)"/> — see <see cref="RecomputeIfNeeded(IReadOnlyList{Pawn},int)"/>.</summary>
        public void RecomputeIfNeeded(Settlement settlement, int cadenceTicks)
        {
            if (!DueForRecompute(cadenceTicks)) return;
            Recompute(settlement);
        }

        /// <summary>Cadence-gated counterpart of <see cref="Recompute(IReadOnlyList{Settlement})"/> — see <see cref="RecomputeIfNeeded(IReadOnlyList{Pawn},int)"/>.</summary>
        public void RecomputeIfNeeded(IReadOnlyList<Settlement> settlements, int cadenceTicks)
        {
            if (!DueForRecompute(cadenceTicks)) return;
            Recompute(settlements);
        }

        private bool DueForRecompute(int cadenceTicks)
        {
            int now = Find.TickManager.TicksGame;
            return lastRecomputeTick == NeverRecomputed || now - lastRecomputeTick >= cadenceTicks;
        }

        /// <summary>Forces the next <see cref="RecomputeIfNeeded(IReadOnlyList{Pawn},int)"/> call to recompute
        /// regardless of cadence — the explicit "dirty" half of the brief, the same idiom as
        /// <c>SituationalThoughtHandler.Notify_SituationalThoughtsDirty</c>.</summary>
        public void Notify_Dirty() => lastRecomputeTick = NeverRecomputed;

        // ---- accumulation ----

        /// <summary>Running totals built up over one <see cref="Recompute"/> call, then written to the public
        /// properties in one place (<see cref="Finalize"/>) — kept a plain mutable struct, passed by
        /// <c>ref</c>, so accumulating never allocates regardless of how many settlements or pawns feed it.</summary>
        private struct Accumulator
        {
            public int Total, Full, Interval, Statistical;
            public float MoodSum, MoodWeight;
            public float HealthSum; // weight is always Total: every counted citizen has a health number.
            public float FoodSum, FoodWeight;
            public float IndustrySum; // weight is always Total: every counted citizen has an industry number.
        }

        private static void AccumulatePawn(Pawn? pawn, ref Accumulator acc)
        {
            if (pawn == null || pawn.Dead || !pawn.RaceProps.Humanlike) return;
            acc.Total++;

            switch (pawn.tier.Tier)
            {
                case PawnTier.Full: acc.Full++; break;
                case PawnTier.Interval: acc.Interval++; break;
                case PawnTier.Statistical: acc.Statistical++; break;
            }

            if (pawn.needs.mood != null)
            {
                acc.MoodSum += pawn.needs.mood.CurLevelPercentage;
                acc.MoodWeight += 1f;
            }
            if (pawn.needs.food != null)
            {
                acc.FoodSum += pawn.needs.food.CurLevelPercentage;
                acc.FoodWeight += 1f;
            }

            // The one line in this whole class that must never become `pawn.health.summaryHealth...`
            // unconditionally — see the class doc and GodTests' own test proving it.
            acc.HealthSum += pawn.tier.Tier == PawnTier.Statistical
                ? pawn.tier.SampledHealthFraction
                : pawn.health.summaryHealth.SummaryHealthPercent;

            acc.IndustrySum += MeanIndustrySkillOf(pawn);
        }

        private static void AccumulateSettlement(Settlement settlement, ref Accumulator acc)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));

            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                AccumulatePawn(citizens[i], ref acc);
            }

            AccumulateStatisticalCohort(settlement, settlement.StatisticalPopulation, ref acc);
        }

        /// <summary>
        /// Folds a settlement's bare <see cref="Settlement.StatisticalPopulation"/> count into the accumulator
        /// with <b>no per-person work</b> — there is no <c>Pawn</c> per Statistical citizen to walk, by that
        /// tier's own design (spec §11.3), so this is the "hard part" <c>docs/status.json</c>'s
        /// <c>god.settlement-population</c> item calls out: a cohort with no members still has to count and
        /// still has to mean.
        /// <para/>
        /// <b>Counts</b> add <paramref name="count"/> directly to <see cref="Accumulator.Total"/> and
        /// <see cref="Accumulator.Statistical"/> — a 40,000-person settlement reports a population of 40,000,
        /// never the handful with a live <c>Pawn</c> object.
        /// <para/>
        /// <b>Means</b> reuse <c>Pawn_TierTracker.ApplyCohortSample</c>'s own idiom one level up: one
        /// deterministic <see cref="RandomStream.RangeSeeded(float,float,int)"/> draw per statistic, seeded
        /// from the settlement's identity (<see cref="GenText.StableStringHash"/> of its name, folded with its
        /// world tile — both Scribed, so the seed is save-stable) and the current tick, not from
        /// <paramref name="count"/> or any per-person id — drawing 40,000 times here would be exactly the
        /// enumeration this tier exists to avoid. That one sample is then weighted by <paramref name="count"/>
        /// in the running mean, which is mathematically "every member of this cohort shares this sampled
        /// value" — a stated, deliberate approximation (the class doc's "fold the cohort in with a stated
        /// sampled value" choice), not a silent average over only the modelled citizens.
        /// <para/>
        /// A no-op when <paramref name="count"/> is zero — no sample drawn, nothing added, so an empty cohort
        /// never nudges a mean toward the sample band for a population that does not exist.
        /// </summary>
        private static void AccumulateStatisticalCohort(Settlement settlement, int count, ref Accumulator acc)
        {
            if (count <= 0) return;

            acc.Total += count;
            acc.Statistical += count;

            int now = Find.TickManager.TicksGame;
            int seedBase = MurmurHash.Combine(GenText.StableStringHash(settlement.name), settlement.tile, now);

            float mood = RandomStream.RangeSeeded(
                TieringTuning.StatisticalNeedSampleMin, TieringTuning.StatisticalNeedSampleMax,
                MurmurHash.Combine(seedBase, MoodSampleTag));
            float food = RandomStream.RangeSeeded(
                TieringTuning.StatisticalNeedSampleMin, TieringTuning.StatisticalNeedSampleMax,
                MurmurHash.Combine(seedBase, FoodSampleTag));
            float health = RandomStream.RangeSeeded(
                TieringTuning.StatisticalHealthFractionMin, TieringTuning.StatisticalHealthFractionMax,
                MurmurHash.Combine(seedBase, HealthSampleTag));
            float industry = RandomStream.RangeSeeded(
                GodTuning.StatisticalIndustrySkillSampleMin, GodTuning.StatisticalIndustrySkillSampleMax,
                MurmurHash.Combine(seedBase, IndustrySampleTag));

            acc.MoodSum += mood * count;
            acc.MoodWeight += count;
            acc.FoodSum += food * count;
            acc.FoodWeight += count;
            acc.HealthSum += health * count;
            acc.IndustrySum += industry * count;
        }

        private void Finalize(ref Accumulator acc)
        {
            TotalPopulation = acc.Total;
            FullCount = acc.Full;
            IntervalCount = acc.Interval;
            StatisticalCount = acc.Statistical;
            MeanMood = acc.MoodWeight > 0f ? acc.MoodSum / acc.MoodWeight : 0f;
            MeanHealth = acc.Total > 0 ? acc.HealthSum / acc.Total : 1f;
            MeanFoodNeed = acc.FoodWeight > 0f ? acc.FoodSum / acc.FoodWeight : 0f;
            MeanIndustrySkill = acc.Total > 0 ? acc.IndustrySum / acc.Total : 0f;

            CurrentEra = Find.ResearchManager.CurrentEra;
            EraProgress = CurrentEra?.Progress ?? 0f;

            lastRecomputeTick = Find.TickManager.TicksGame;
        }

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
