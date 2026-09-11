using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Pawns.Generation;
using SimWorld.Pawns.Genes;
using SimWorld.Sim;

namespace SimWorld.Pawns
{
    /// <summary>
    /// Owns every <see cref="Family"/> and runs the population-wide marriage/birth/death-from-age sweep
    /// (exposed as <see cref="Sim.Find.FamilyManager"/>, following the same service-locator shape as
    /// <c>Find.Storyteller</c>/<c>Find.ResearchManager</c>). There is no per-pawn tick hook here by design:
    /// <see cref="DemographyTick"/> gates itself to <see cref="DemographyTuning.DemographyIntervalTicks"/> the
    /// same way <c>Storyteller.StorytellerTick</c> gates to <c>IncidentCycleLengthTicks</c>, then does one
    /// pass over the given population — a rare-tick manager sweep, not per-tick-per-pawn work. Callers (a test,
    /// or eventually a host's <c>TickManager.PostTickers</c>) supply the living population explicitly, the same
    /// way <c>Storyteller.MakeIncidentsForInterval</c> iterates its own registered targets rather than owning a
    /// global pawn registry.
    /// </summary>
    public sealed class FamilyManager : IExposable
    {
        private readonly List<Family> families = new List<Family>();
        private int nextFamilyId;

        public IReadOnlyList<Family> Families => families;

        public Family? GetFamily(int id) => id == Pawn_RelationsTracker.None ? null : families.Find(f => f.id == id);

        /// <summary>
        /// Founds a brand new household for a couple who just married (or a single founder with
        /// <paramref name="founderB"/> null, for symmetry with future immigration-style founding). Never merges
        /// either spouse into the other's existing family — see the class and <see cref="Family"/> docs for why
        /// that is the one rule this whole module exists to enforce. Any prior household either spouse belonged
        /// to loses them from its living count; this new one starts fresh.
        /// </summary>
        public Family FoundHousehold(Pawn founderA, Pawn? founderB, int foundingTick)
        {
            if (founderA == null) throw new ArgumentNullException(nameof(founderA));

            LeaveCurrentFamily(founderA);
            if (founderB != null) LeaveCurrentFamily(founderB);

            int generation = founderA.relations.generation;
            if (founderB != null) generation = Math.Max(generation, founderB.relations.generation);

            var family = new Family
            {
                id = nextFamilyId++,
                foundingTick = foundingTick,
                founderIdA = founderA.thingIDNumber,
                founderIdB = founderB?.thingIDNumber ?? Pawn_RelationsTracker.None,
                generation = generation,
                livingCount = founderB == null ? 1 : 2,
                totalCount = founderB == null ? 1 : 2,
                lastBirthTick = foundingTick - DemographyTuning.MinBirthIntervalTicks,
                surname = PawnBioAndNameGenerator.GenerateSurname(),
            };
            families.Add(family);

            founderA.relations.familyId = family.id;
            founderA.relations.generation = generation;
            if (founderB != null)
            {
                founderB.relations.familyId = family.id;
                founderB.relations.generation = generation;
                founderA.relations.spouseId = founderB.thingIDNumber;
                founderB.relations.spouseId = founderA.thingIDNumber;
            }

            return family;
        }

        private void LeaveCurrentFamily(Pawn pawn)
        {
            Family? old = GetFamily(pawn.relations.familyId);
            if (old != null) old.livingCount = Math.Max(0, old.livingCount - 1);
        }

        /// <summary>
        /// The single wiring point for demography: gated to <see cref="DemographyTuning.DemographyIntervalTicks"/>,
        /// then deaths, marriages and births run in that order (so a pawn who dies of age this interval cannot
        /// also marry or roll a birth in the same pass, and a freshly-married couple cannot roll a birth before
        /// its household exists). <paramref name="population"/> is mutated in place: newborns are appended to
        /// it (see <see cref="ProcessBirths"/>); the dead are marked but never removed, matching
        /// <see cref="Pawn_HealthTracker.Kill"/>'s own contract of leaving the pawn in place.
        /// </summary>
        public void DemographyTick(List<Pawn> population)
        {
            if (population == null) throw new ArgumentNullException(nameof(population));
            if (Find.TickManager.TicksGame % DemographyTuning.DemographyIntervalTicks != 0) return;

            ProcessDeathsFromAge(population);

            // After the deaths, so the oldest living citizen really is living. This is the cadence a
            // civilization-scale "has anyone outlived everyone before them" question wants — once a year,
            // over a whole population — and demography is the only sweep with both. See ChronicleFame for
            // what it does and, at more length, for why it is the one thing that names a living citizen.
            Director.ChronicleFame.ConsiderDoyen(population);

            ProcessMarriages(population);
            ProcessBirths(population);
        }

        /// <summary>
        /// Pairs eligible unmarried adults by an explicit, deterministic rule: scan the eligible pool in
        /// ascending pawn-id order (RimWorld ids are already stable/deterministic,
        /// <see cref="Thing.thingIDNumber"/>), and for each still-unmatched pawn take the first still-unmatched,
        /// opposite-gender candidate later in that same order whose family does not already match its own — the
        /// incest guard. Every match founds a new household via <see cref="FoundHousehold"/>; nothing here ever
        /// merges a spouse into the other's family.
        /// </summary>
        public void ProcessMarriages(List<Pawn> population)
        {
            List<Pawn> eligible = population
                .Where(p => !p.Dead && p.RaceProps.Humanlike && !p.relations.IsMarried &&
                            p.ageTracker.AgeBiologicalYearsFloat >= DemographyTuning.MinMarriageAgeYears)
                .OrderBy(p => p.thingIDNumber)
                .ToList();

            var matched = new HashSet<int>();
            for (int i = 0; i < eligible.Count; i++)
            {
                Pawn a = eligible[i];
                if (matched.Contains(a.thingIDNumber)) continue;

                for (int j = i + 1; j < eligible.Count; j++)
                {
                    Pawn b = eligible[j];
                    if (matched.Contains(b.thingIDNumber)) continue;
                    if (a.gender == b.gender) continue;
                    if (SameFamily(a, b)) continue;

                    FoundHousehold(a, b, Find.TickManager.TicksGame);
                    matched.Add(a.thingIDNumber);
                    matched.Add(b.thingIDNumber);
                    break;
                }
            }
        }

        private static bool SameFamily(Pawn a, Pawn b) =>
            a.relations.HasFamily && a.relations.familyId == b.relations.familyId;

        /// <summary>
        /// Rolls one birth per married couple: iterating the population in id order and only acting when
        /// <c>pawn.thingIDNumber &lt; spouseId</c> guarantees the couple is considered exactly once (Epoch's own
        /// de-duplication trick, §5: "only the lower-id spouse's iteration fires"). A newborn is generated
        /// through the existing <see cref="PawnGenerator.GenerateNewborn"/> path (which already records its own
        /// "Born" life event), then wired into the household: family id, both parent ids, and generation one
        /// past the family's own. Newborns are appended to <paramref name="population"/> so a later call in the
        /// same simulation sees them.
        /// </summary>
        public void ProcessBirths(List<Pawn> population)
        {
            Dictionary<int, Pawn> byId = population.ToDictionary(p => p.thingIDNumber);
            List<Pawn> ordered = population.OrderBy(p => p.thingIDNumber).ToList();

            foreach (Pawn a in ordered)
            {
                if (a.Dead || !a.RaceProps.Humanlike) continue;
                int spouseId = a.relations.spouseId;
                if (spouseId == Pawn_RelationsTracker.None) continue;
                if (a.thingIDNumber > spouseId) continue; // the other spouse's own turn already handled (or will) this couple

                if (!byId.TryGetValue(spouseId, out Pawn? b) || b.Dead) continue;

                Family? family = GetFamily(a.relations.familyId);
                if (family == null || family.id != b.relations.familyId) continue;

                if (!EligibleForBirth(a) || !EligibleForBirth(b)) continue;
                if (Find.TickManager.TicksGame - family.lastBirthTick < DemographyTuning.MinBirthIntervalTicks) continue;

                float mood = AverageMood(a, b);
                bool foodSecure = MinFoodLevel(a, b) >= DemographyTuning.FoodSecurityThreshold;
                float chance = ComputeBirthChance(mood, foodSecure);
                if (!Rand.Chance(chance)) continue;

                Pawn newborn = DoBirth(family, a, b);
                population.Add(newborn);
                byId[newborn.thingIDNumber] = newborn;
            }
        }

        private static bool EligibleForBirth(Pawn p)
        {
            float age = p.ageTracker.AgeBiologicalYearsFloat;
            return age >= DemographyTuning.MinMarriageAgeYears && age <= DemographyTuning.MaxFertilityAgeYears;
        }

        private static float AverageMood(Pawn a, Pawn b) =>
            (MoodLevel(a) + MoodLevel(b)) / 2f;

        private static float MoodLevel(Pawn p) => p.needs?.mood?.CurLevelPercentage ?? 0.5f;

        private static float MinFoodLevel(Pawn a, Pawn b) => Math.Min(FoodLevel(a), FoodLevel(b));

        private static float FoodLevel(Pawn p) => p.needs?.food?.CurLevelPercentage ?? 1f;

        /// <summary>
        /// The birth-probability formula, shaped after Epoch's own (base × mood × food-security ×
        /// [doctrine, later]) per <c>docs/research/epoch-inspiration.md</c> §5, but with SimWorld's own
        /// constants (<see cref="DemographyTuning"/>) on SimWorld's own clock. <paramref name="averageMood"/>
        /// is expected in [0, 1]; the (0.5 + mood) term means a maximally unhappy couple still has half the
        /// base chance rather than zero — mood discourages, it does not sterilize. Internal (not private) so
        /// it is directly testable without needing two fully-fledged married pawns.
        /// </summary>
        internal static float ComputeBirthChance(float averageMood, bool foodSecure)
        {
            float foodFactor = foodSecure ? 1f : DemographyTuning.FoodInsecureBirthFactor;
            return DemographyTuning.BaseBirthChancePerInterval * (0.5f + averageMood) * foodFactor;
        }

        private static Pawn DoBirth(Family family, Pawn parentA, Pawn parentB)
        {
            PawnKindDef kind = parentA.kindDef ?? parentB.kindDef ?? PawnKindDefOf.Colonist;
            Pawn newborn = PawnGenerator.GenerateNewborn(kind);

            // pawngen.genes: endogenes only, never xenogenes — see GeneInheritanceUtility's own doc for the
            // rule and why it is SimWorld's own rather than a sourced RimWorld one. A no-op (and zero extra
            // Rand calls) for the overwhelming majority of today's population, which carries no genes at all.
            GeneInheritanceUtility.InheritEndogenesFrom(newborn, parentA, parentB);

            newborn.relations.familyId = family.id;
            newborn.relations.SetParents(parentA.thingIDNumber, parentB.thingIDNumber);
            newborn.relations.generation = family.generation + 1;

            parentA.relations.Notify_ChildBorn(newborn.thingIDNumber);
            parentB.relations.Notify_ChildBorn(newborn.thingIDNumber);

            family.totalCount++;
            family.livingCount++;
            family.lastBirthTick = Find.TickManager.TicksGame;

            Find.Storyteller.RecordChronicle("Birth: " + newborn.Label + " joins family " + family.id + ".");

            return newborn;
        }

        /// <summary>Sweeps <paramref name="population"/> for pawns whose hidden lifespan budget has been
        /// reached (<see cref="Pawn_AgeTracker.ShouldDieOfAge"/>) and kills them with <see cref="DeathCause.Age"/>.
        /// Only humanlike pawns are considered — SimWorld has not built age-based death for animals yet.</summary>
        public void ProcessDeathsFromAge(List<Pawn> population)
        {
            Dictionary<int, Pawn>? byId = null;
            foreach (Pawn p in population)
            {
                if (p.Dead || !p.RaceProps.Humanlike || !p.ageTracker.ShouldDieOfAge()) continue;
                byId ??= population.ToDictionary(x => x.thingIDNumber);
                HandleDeath(p, DeathCause.Age, byId);
            }
        }

        /// <summary>
        /// The reusable half of death handling every cause funnels through: kill the pawn via the existing
        /// health system (never a parallel path), shrink its household's living count, mark spouse/parents/
        /// children bereaved, and record the cause on the chronicle. <see cref="ProcessDeathsFromAge"/> is the
        /// only caller wired up in this module; future systems (starvation, disease, injury, combat) call this
        /// the same way once they roll their own <see cref="DeathCause"/>.
        /// </summary>
        public void HandleDeath(Pawn pawn, DeathCause cause, IReadOnlyDictionary<int, Pawn> population)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (population == null) throw new ArgumentNullException(nameof(population));

            pawn.health.Kill(null, null);

            Family? family = GetFamily(pawn.relations.familyId);
            if (family != null) family.livingCount = Math.Max(0, family.livingCount - 1);

            int spouseId = pawn.relations.spouseId;
            MarkBereaved(spouseId, cause, population);
            MarkBereaved(pawn.relations.parentIdA, cause, population);
            MarkBereaved(pawn.relations.parentIdB, cause, population);
            foreach (int childId in pawn.relations.childIds) MarkBereaved(childId, cause, population);

            // A widow/widower is freed to remarry (ProcessMarriages only ever considers spouseId == None
            // eligible) rather than staying permanently unmarriageable — real households re-form after a death,
            // and over a long enough run leaving this out would quietly stall population growth.
            if (spouseId != Pawn_RelationsTracker.None && population.TryGetValue(spouseId, out Pawn? spouse) && spouse != null && !spouse.Dead)
            {
                spouse.relations.spouseId = Pawn_RelationsTracker.None;
            }

            // Find.Storyteller is Layer 5 (Director); reached only through the Find service locator, the same
            // sanctioned cross-layer path Find.cs itself uses — this file never references SimWorld.Director.
            string detail = cause == DeathCause.Age ? pawn.ageTracker.DescribeLifespanForChronicle() : "";
            Find.Storyteller.RecordDeath(pawn, cause, detail);
        }

        /// <summary>
        /// One of the four tier-promotion triggers (<c>docs/spec/simworld-spec.md</c> §11.3): "a relationship
        /// attaches them to someone already promoted". Walks <paramref name="promoted"/>'s spouse, both parents
        /// and every child — one hop only, not the whole lineage; a promoted leader's grandparent's spouse's
        /// distant cousin is not "attached" to them, it is a coincidence of descent — and marks each pawn that
        /// resolves in <paramref name="population"/> as related-to-promoted, which promotes it to Full
        /// (<see cref="Pawn_TierTracker.Notify_RelatedToPromoted"/>) if it is not there already. Idempotent:
        /// calling this again after nothing has changed re-sends the same notification, which is a no-op.
        /// </summary>
        public static void PromoteRelativesOf(Pawn promoted, IReadOnlyDictionary<int, Pawn> population)
        {
            if (promoted == null) throw new ArgumentNullException(nameof(promoted));
            if (population == null) throw new ArgumentNullException(nameof(population));

            MarkRelated(promoted.relations.spouseId, population);
            MarkRelated(promoted.relations.parentIdA, population);
            MarkRelated(promoted.relations.parentIdB, population);
            foreach (int childId in promoted.relations.childIds) MarkRelated(childId, population);
        }

        private static void MarkRelated(int id, IReadOnlyDictionary<int, Pawn> population)
        {
            if (id == Pawn_RelationsTracker.None) return;
            if (population.TryGetValue(id, out Pawn? relative) && relative != null)
            {
                relative.tier.Notify_RelatedToPromoted(true);
            }
        }

        private static void MarkBereaved(int id, DeathCause cause, IReadOnlyDictionary<int, Pawn> population)
        {
            if (id == Pawn_RelationsTracker.None) return;
            if (population.TryGetValue(id, out Pawn? relative) && relative != null && !relative.Dead)
            {
                relative.relations.Notify_Bereaved(cause);
            }
        }

        public void ExposeData()
        {
            List<Family>? list = new List<Family>(families);
            Scribe_Collections.Look(ref list, "families", LookMode.Deep);
            families.Clear();
            if (list != null) families.AddRange(list);
            Scribe_Values.Look(ref nextFamilyId, "nextFamilyId");
        }
    }
}
