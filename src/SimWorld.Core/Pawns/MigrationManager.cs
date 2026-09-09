using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Pawns.Generation;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Pawns
{
    /// <summary>
    /// People arriving at a settlement and founding households, and departing one that has stopped being worth
    /// living in (system: demography, tracker item <c>demography.migration</c>). <see cref="FamilyManager.FoundHousehold"/>
    /// already accepts a single founder "for future immigration-style founding"
    /// (<c>DemographyTests.FoundHousehold_supports_a_single_founder_for_future_immigration_style_founding</c>) —
    /// this is that future: it generates the arriving migrant and calls the existing hook rather than adding a
    /// second founding path.
    /// <para/>
    /// <b>Rate responds to something real.</b> Mirrors <see cref="FamilyManager.ComputeBirthChance"/>'s own
    /// shape (base × (0.5 + quality) × a situational factor) rather than inventing an unrelated formula:
    /// <see cref="SettlementQuality"/> reads the same per-citizen <c>Need_Mood</c>/<c>Need_Food</c> signal births
    /// already read, and <see cref="ArrivalChance"/> additionally scales with the civilization's current era
    /// (<see cref="EraDef.order"/>) — a more advanced settlement has more to offer. Both are the specific "food,
    /// mood, or the era" signals the brief names.
    /// <para/>
    /// <b>Never conjures thousands of <c>Pawn</c>s.</b> A settlement's bare <c>StatisticalPopulation</c> is a
    /// count, not a roster (<c>docs/spec/simworld-spec.md</c> §11.3) — <see cref="MigrationTick(Settlement,PawnKindDef)"/>
    /// grows that count directly (see <see cref="GrowStatisticalCohortByMigration"/>), the same closed-form shape
    /// <c>Settlement.GrowStatisticalCohort</c> already uses for births-minus-deaths, and only ever materialises a
    /// real <c>Pawn</c> for the rarer, individually-founded-household half of arrivals.
    /// <para/>
    /// <b>Departures: a real, working mechanism with one honest limitation.</b> <see cref="ProcessDepartures"/>
    /// removes a distressed family's living members directly from a caller-supplied <see cref="List{Pawn}"/> —
    /// the same "operate on the list the caller owns" contract <see cref="FamilyManager.DemographyTick"/> already
    /// uses, which is what lets a caller with the actual backing list (a live settlement's own roster, mutated
    /// through it) see a real population decline. The <see cref="Settlement"/>-aware overload
    /// (<see cref="MigrationTick(Settlement,PawnKindDef)"/>) cannot offer that same removal for either
    /// population slice: <c>Settlement.Citizens</c> is exposed read-only with no public way to remove a citizen,
    /// and <c>Settlement.StatisticalPopulation</c> is a private field only <c>Settlement</c>'s own code can
    /// shrink (its one public mutator, <c>AddStatisticalPeople</c>, rejects a negative count outright) — and
    /// <c>World/**</c> is out of this lane's bounds to add one. So the settlement overload floors net-negative
    /// migration at zero: an unlivable settlement simply stops attracting anyone rather than shedding population.
    /// This is a real, stated gap, not a silent one — see the module's own report.
    /// </summary>
    public static class MigrationManager
    {
        /// <summary>
        /// The single wiring point for the caller-owned-list shape: gated to
        /// <see cref="MigrationTuning.MigrationIntervalTicks"/>, then departures before arrivals (mirroring
        /// <see cref="FamilyManager.DemographyTick"/>'s own "deaths, then marriages, then births" ordering — a
        /// family that just left this interval should not, in the same pass, be reconsidered for anything else).
        /// Returns whether a new household arrived, for a caller or test that only cares about that half.
        /// </summary>
        public static bool MigrationTick(List<Pawn> population, PawnKindDef migrantKind, EraDef? era = null, string? placeLabel = null)
        {
            if (population == null) throw new ArgumentNullException(nameof(population));
            if (Find.TickManager.TicksGame % MigrationTuning.MigrationIntervalTicks != 0) return false;

            ProcessDepartures(population, placeLabel);
            return ProcessArrivals(population, migrantKind, era, placeLabel);
        }

        /// <summary>
        /// Rolls one chance for a single new household to arrive this interval (never more than one per call —
        /// a settlement-scale event, not one roll per existing citizen, which would make a large population
        /// attract migrants faster purely by being large). On success, generates one adult migrant
        /// (<see cref="GenerateMigrant"/>), founds their household via the existing
        /// <see cref="FamilyManager.FoundHousehold"/> hook, appends them to <paramref name="population"/>, and
        /// promotes them to <see cref="PawnTier.Full"/> — a migrant founding a household on arrival is exactly
        /// the "founder" case <see cref="Pawn_TierTracker.Notify_RoleChanged"/>'s own doc already names as a
        /// promotion trigger, and (unlike every marriage in a population, which would promote far too many
        /// people to stay meaningful) an immigrant arrival is rare enough that flagging it does not erode the
        /// tiering system's whole cost saving.
        /// </summary>
        public static bool ProcessArrivals(List<Pawn> population, PawnKindDef migrantKind, EraDef? era = null, string? placeLabel = null)
        {
            if (population == null) throw new ArgumentNullException(nameof(population));
            if (migrantKind == null) throw new ArgumentNullException(nameof(migrantKind));

            float quality = SettlementQuality(population);
            float chance = ArrivalChance(quality, era);
            if (!Rand.Chance(chance)) return false;

            Pawn migrant = GenerateMigrant(migrantKind);
            Find.FamilyManager.FoundHousehold(migrant, null, Find.TickManager.TicksGame);
            population.Add(migrant);
            migrant.tier.Notify_RoleChanged(true);

            Find.Storyteller.RecordChronicle(
                "Migration: " + migrant.Label + " arrives" + Suffix(placeLabel) + " and founds a household.");
            return true;
        }

        /// <summary>
        /// Groups <paramref name="population"/>'s living, humanlike, family-having members by household
        /// (<see cref="Pawn_RelationsTracker.familyId"/> — this catches a single migrant founder too, since
        /// <see cref="FamilyManager.FoundHousehold"/> assigns a family id even to a household of one) and, for
        /// each family whose own <see cref="SettlementQuality"/> average sits below
        /// <see cref="MigrationTuning.DepartureQualityThreshold"/>, rolls
        /// <see cref="MigrationTuning.BaseDepartureChancePerInterval"/>; on success removes every living member
        /// of that family from <paramref name="population"/> together (a household leaves as a unit, not member
        /// by member) and shrinks the family's own <c>livingCount</c> to match, the same bookkeeping
        /// <see cref="FamilyManager.HandleDeath"/> already does for a death. Returns how many households departed.
        /// </summary>
        public static int ProcessDepartures(List<Pawn> population, string? placeLabel = null)
        {
            if (population == null) throw new ArgumentNullException(nameof(population));

            List<IGrouping<int, Pawn>> households = population
                .Where(p => !p.Dead && p.RaceProps.Humanlike && p.relations.HasFamily)
                .GroupBy(p => p.relations.familyId)
                .ToList();

            int departed = 0;
            foreach (IGrouping<int, Pawn> household in households)
            {
                List<Pawn> members = household.ToList();
                float quality = SettlementQuality(members);
                if (quality >= MigrationTuning.DepartureQualityThreshold) continue;
                if (!Rand.Chance(MigrationTuning.BaseDepartureChancePerInterval)) continue;

                foreach (Pawn p in members) population.Remove(p);

                Family? family = Find.FamilyManager.GetFamily(household.Key);
                if (family != null) family.livingCount = Math.Max(0, family.livingCount - members.Count);

                string names = string.Join(", ", members.Select(p => p.Label));
                Find.Storyteller.RecordChronicle(
                    "Migration: " + names + " leave" + Suffix(placeLabel) + ", unable to make a life there.");
                departed++;
            }
            return departed;
        }

        /// <summary>
        /// Population-weighted average of <c>Need_Mood</c>/<c>Need_Food</c> current level (0-1) over every
        /// living humanlike in <paramref name="population"/> — the exact same per-pawn signal
        /// <see cref="FamilyManager.ComputeBirthChance"/>'s callers already read (<see cref="FamilyManager"/>'s
        /// own <c>AverageMood</c>/<c>FoodLevel</c> helpers), reused at settlement/household scale rather than
        /// invented fresh. 0.5 (neutral) when nobody living qualifies — an empty settlement or a lone Statistical
        /// cohort should read as neither attractive nor distressed, not as a false floor or ceiling.
        /// </summary>
        public static float SettlementQuality(IReadOnlyList<Pawn> population)
        {
            if (population == null) throw new ArgumentNullException(nameof(population));

            float moodSum = 0f, foodSum = 0f;
            int n = 0;
            for (int i = 0; i < population.Count; i++)
            {
                Pawn p = population[i];
                if (p == null || p.Dead || !p.RaceProps.Humanlike) continue;
                moodSum += p.needs?.mood?.CurLevelPercentage ?? 0.5f;
                foodSum += p.needs?.food?.CurLevelPercentage ?? 0.5f;
                n++;
            }
            return n == 0 ? 0.5f : (moodSum / n + foodSum / n) / 2f;
        }

        /// <summary>The birth-chance formula's own shape (<see cref="FamilyManager.ComputeBirthChance"/>: base ×
        /// (0.5 + quality)) with an era multiplier layered on — see <see cref="MigrationTuning.EraArrivalBonusPerOrder"/>.
        /// Internal (not private) so it is directly testable without a fully assembled population.</summary>
        internal static float ArrivalChance(float quality, EraDef? era)
        {
            float eraFactor = era != null ? 1f + MigrationTuning.EraArrivalBonusPerOrder * era.order : 1f;
            return MigrationTuning.ArrivalBaseChancePerInterval * (0.5f + quality) * eraFactor;
        }

        private static Pawn GenerateMigrant(PawnKindDef kind)
        {
            float age = Rand.Range(MigrationTuning.MinMigrantAgeYears, MigrationTuning.MaxMigrantAgeYears);
            return PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, fixedBiologicalAge: age));
        }

        private static string Suffix(string? placeLabel) => string.IsNullOrEmpty(placeLabel) ? "" : " at " + placeLabel;

        // ---- Settlement-aware convenience (arrivals only — see the class doc for why) ----

        /// <summary>
        /// Settlement-aware convenience wrapper: reads quality and era straight off <paramref name="settlement"/>
        /// and <c>Find.ResearchManager</c>, founds an arriving household onto <see cref="Settlement.Citizens"/>
        /// via the existing public <c>AddCitizen</c>, and grows <see cref="Settlement.StatisticalPopulation"/>
        /// by the same closed-form idiom <c>Settlement.GrowStatisticalCohort</c> already uses for births-minus-
        /// deaths. Arrivals only — see the class doc's "one honest limitation" for why departures cannot be
        /// expressed through a bare <see cref="Settlement"/> reference and need the population-list overload
        /// instead.
        /// </summary>
        public static bool MigrationTick(Settlement settlement, PawnKindDef migrantKind)
        {
            if (settlement == null) throw new ArgumentNullException(nameof(settlement));
            if (migrantKind == null) throw new ArgumentNullException(nameof(migrantKind));
            if (Find.TickManager.TicksGame % MigrationTuning.MigrationIntervalTicks != 0) return false;

            EraDef? era = Find.ResearchManager.CurrentEra;
            float quality = SettlementQuality(settlement.Citizens);

            bool arrived = false;
            if (Rand.Chance(ArrivalChance(quality, era)))
            {
                Pawn migrant = GenerateMigrant(migrantKind);
                Find.FamilyManager.FoundHousehold(migrant, null, Find.TickManager.TicksGame);
                settlement.AddCitizen(migrant);
                migrant.tier.Notify_RoleChanged(true);
                Find.Storyteller.RecordChronicle(
                    "Migration: " + migrant.Label + " arrives at " + settlement.name + " and founds a household.");
                arrived = true;
            }

            GrowStatisticalCohortByMigration(settlement, quality, era);
            return arrived;
        }

        /// <summary>
        /// Grows <see cref="Settlement.StatisticalPopulation"/> by <see cref="MigrationTuning.StatisticalNetMigrationRatePerYearAtMaxQuality"/>
        /// scaled by how far above neutral (0.5) <paramref name="quality"/> sits and by the era factor
        /// <see cref="ArrivalChance"/> already computes — floored at zero rather than negative; see the class
        /// doc's "one honest limitation".
        /// </summary>
        private static void GrowStatisticalCohortByMigration(Settlement settlement, float quality, EraDef? era)
        {
            int current = settlement.StatisticalPopulation;
            if (current <= 0) return; // nothing to model a rate against, mirroring GrowStatisticalCohort's own early-out

            float eraFactor = era != null ? 1f + MigrationTuning.EraArrivalBonusPerOrder * era.order : 1f;
            float signedQuality = (quality - 0.5f) * 2f; // maps [0,1] quality to [-1,1] around neutral
            float rate = MigrationTuning.StatisticalNetMigrationRatePerYearAtMaxQuality * signedQuality * eraFactor;
            if (rate <= 0f) return;

            int grown = (int)Math.Round(current * rate, MidpointRounding.AwayFromZero);
            if (grown > 0) settlement.AddStatisticalPeople(grown);
        }
    }
}
