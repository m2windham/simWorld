using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SimWorld.Director;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Pawns
{
    /// <summary>Scribe holder for round-tripping a small household alongside the pawns in it, mirroring
    /// <c>PawnHolder</c> in <c>PawnTests.cs</c>.</summary>
    public class DemographyHolder : IExposable
    {
        public FamilyManager? familyManager;
        public List<Pawn>? pawns;

        public void ExposeData()
        {
            Scribe_Deep.Look(ref familyManager, "familyManager");
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Deep);
        }
    }

    public class DemographyTests : ContentTestBase
    {
        public DemographyTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
            Find.FamilyManager = new FamilyManager();
        }

        private static Pawn NewAdult(float age = 25f, Gender? gender = null) =>
            PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedGender: gender, fixedBiologicalAge: age));

        /// <summary>Cranks a couple's mood and food to the maximum and retries the birth roll under a fresh seed
        /// each attempt until one succeeds (expected chance at max mood/food is 0.525 — a handful of tries at
        /// most) so tests that need a concrete newborn do not depend on a single fragile seed.</summary>
        private static Pawn? TryForceBirth(Pawn husband, Pawn wife, Family family, int maxTries = 200)
        {
            for (int i = 0; i < maxTries; i++)
            {
                Rand.Current = new RandomStream(i * 7919 + 13);
                husband.needs.mood!.CurLevel = 1f;
                wife.needs.mood!.CurLevel = 1f;
                husband.needs.food!.CurLevel = husband.needs.food.MaxLevel;
                wife.needs.food!.CurLevel = wife.needs.food.MaxLevel;
                family.lastBirthTick = Find.TickManager.TicksGame - DemographyTuning.MinBirthIntervalTicks;

                var population = new List<Pawn> { husband, wife };
                Find.FamilyManager.ProcessBirths(population);
                if (population.Count == 3) return population[2];
            }
            return null;
        }

        /// <summary>
        /// Advances every living pawn's own age by one demography interval. FamilyManager's sweep is driven by
        /// the shared TickManager clock alone (see DemographyTick's doc), but a real game also ticks each pawn
        /// individually (Pawn.Tick -> ageTracker.AgeTick, wired in Pawn.cs) — literally single-stepping that for
        /// a whole simulated lifespan (millions of ticks) would make these tests far too slow, so multi-year
        /// simulation tests mimic it cheaply with AgeTickMothballed instead (RimWorld's own off-tick-list aging
        /// path — see Pawn_AgeTracker.AgeTickMothballed) rather than skipping it, which would freeze everyone at
        /// their starting age forever.
        /// </summary>
        private static void AdvanceYear(List<Pawn> population, int ticks)
        {
            foreach (Pawn p in population)
            {
                if (!p.Dead) p.ageTracker.AgeTickMothballed(ticks);
            }
        }

        private static float FindDeathAge(Pawn pawn)
        {
            for (float age = 0f; age < 200f; age += 0.1f)
            {
                pawn.ageTracker.DebugSetAge(age);
                if (pawn.ageTracker.ShouldDieOfAge()) return age;
            }
            return -1f;
        }

        // ---- marriage: households, never merges ----

        [Fact]
        public void Marriage_founds_a_new_household_neither_family_absorbs_the_other()
        {
            Pawn fatherA = NewAdult(gender: Gender.Male);
            Pawn motherA = NewAdult(gender: Gender.Female);
            Family familyX = Find.FamilyManager.FoundHousehold(fatherA, motherA, 0);

            Pawn fatherB = NewAdult(gender: Gender.Male);
            Pawn motherB = NewAdult(gender: Gender.Female);
            Family familyY = Find.FamilyManager.FoundHousehold(fatherB, motherB, 0);

            Pawn childA = NewAdult(gender: Gender.Male);
            childA.relations.familyId = familyX.id;
            familyX.livingCount++;
            familyX.totalCount++;

            Pawn childB = NewAdult(gender: Gender.Female);
            childB.relations.familyId = familyY.id;
            familyY.livingCount++;
            familyY.totalCount++;

            int livingXBefore = familyX.livingCount;
            int livingYBefore = familyY.livingCount;

            Family newFamily = Find.FamilyManager.FoundHousehold(childA, childB, 1000);

            Assert.NotEqual(familyX.id, newFamily.id);
            Assert.NotEqual(familyY.id, newFamily.id);
            Assert.Equal(newFamily.id, childA.relations.familyId);
            Assert.Equal(newFamily.id, childB.relations.familyId);
            Assert.Equal(livingXBefore - 1, familyX.livingCount);
            Assert.Equal(livingYBefore - 1, familyY.livingCount);
            Assert.Equal(2, newFamily.livingCount);
            Assert.Equal(2, newFamily.totalCount);
            Assert.Equal(childA.thingIDNumber, newFamily.founderIdA);
            Assert.Equal(childB.thingIDNumber, newFamily.founderIdB);
        }

        [Fact]
        public void Same_family_marriage_never_happens_but_an_outsider_still_pairs()
        {
            Pawn father = NewAdult(gender: Gender.Male);
            Pawn mother = NewAdult(gender: Gender.Female);
            Family family = Find.FamilyManager.FoundHousehold(father, mother, 0);

            Pawn son = NewAdult(gender: Gender.Male);
            son.relations.familyId = family.id;
            son.relations.SetParents(father.thingIDNumber, mother.thingIDNumber);

            Pawn daughter = NewAdult(gender: Gender.Female);
            daughter.relations.familyId = family.id;
            daughter.relations.SetParents(father.thingIDNumber, mother.thingIDNumber);

            Pawn outsider = NewAdult(gender: Gender.Female);

            var population = new List<Pawn> { son, daughter, outsider };
            Find.FamilyManager.ProcessMarriages(population);

            Assert.True(son.relations.IsMarried);
            Assert.Equal(outsider.thingIDNumber, son.relations.spouseId);
            Assert.False(daughter.relations.IsMarried);
        }

        [Fact]
        public void Marriage_requires_adult_age()
        {
            Pawn minor = NewAdult(age: 10f, gender: Gender.Male);
            Pawn adult = NewAdult(age: 25f, gender: Gender.Female);

            var population = new List<Pawn> { minor, adult };
            Find.FamilyManager.ProcessMarriages(population);

            Assert.False(minor.relations.IsMarried);
            Assert.False(adult.relations.IsMarried);
        }

        [Fact]
        public void Marriage_pairing_is_deterministic_by_ascending_id()
        {
            Pawn m1 = NewAdult(gender: Gender.Male);
            Pawn m2 = NewAdult(gender: Gender.Male);
            Pawn f1 = NewAdult(gender: Gender.Female);
            Pawn f2 = NewAdult(gender: Gender.Female);

            var population = new List<Pawn> { m1, m2, f1, f2 };
            Find.FamilyManager.ProcessMarriages(population);

            Assert.Equal(f1.thingIDNumber, m1.relations.spouseId);
            Assert.Equal(f2.thingIDNumber, m2.relations.spouseId);
        }

        [Fact]
        public void Lineage_concentration_stays_bounded_over_many_generations()
        {
            Rand.Current = new RandomStream(4242);
            var population = new List<Pawn>();
            for (int i = 0; i < 6; i++)
            {
                population.Add(NewAdult(age: 20f + i, gender: i % 2 == 0 ? Gender.Male : Gender.Female));
            }

            int tick = 0;
            for (int year = 0; year < 120; year++)
            {
                tick += DemographyTuning.DemographyIntervalTicks;
                AdvanceYear(population, DemographyTuning.DemographyIntervalTicks);
                Find.TickManager.DebugSetTicksGame(tick);
                Find.FamilyManager.DemographyTick(population);
            }

            List<Pawn> living = population.Where(p => !p.Dead).ToList();
            Assert.True(living.Count > 50, "expected multiple generations of growth; got " + living.Count + " living");

            List<int> familyLivingCounts = living
                .Where(p => p.relations.HasFamily)
                .GroupBy(p => p.relations.familyId)
                .Select(g => g.Count())
                .ToList();
            // Many independent households, not a handful — itself evidence weddings are founding new families
            // rather than draining into a few.
            Assert.True(familyLivingCounts.Count > 20, "expected many independent households; got " + familyLivingCounts.Count);

            int maxFamily = familyLivingCounts.Max();
            double share = (double)maxFamily / living.Count;
            // This is the regression test for the one non-negotiable rule: a wedding always founds a new
            // household, never merges a spouse into the other's family. With that rule in place, a run of this
            // scenario (seed 4242, 6 founders, 120 years) measures 1,240 living descendants across 284
            // households with the largest holding 10 (0.8%) — nowhere near Epoch's measured 90% concentration
            // in one lineage by year 20-25 under its merge rule (docs/research/epoch-inspiration.md §5). 15% is
            // a generous ceiling above that measurement, not a tight bound: this asserts "structurally cannot
            // run away", not "always lands near 0.8%".
            Assert.True(share <= 0.15,
                "largest family holds " + share.ToString("P1") + " of the living population (" + maxFamily + "/" + living.Count +
                "); the anti-merge rule should keep this well bounded, unlike Epoch's measured 90% under its merge rule " +
                "(docs/research/epoch-inspiration.md §5).");
        }

        // ---- birth ----

        [Fact]
        public void Only_one_birth_roll_fires_per_couple_per_interval()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                Find.TickManager = new TickManager();
                Rand.Current = new RandomStream(seed);
                Pawn.ResetThingIdCounter();
                NameUseChecker.Clear();
                Find.FamilyManager = new FamilyManager();

                Pawn husband = NewAdult(age: 25f, gender: Gender.Male);
                Pawn wife = NewAdult(age: 25f, gender: Gender.Female);
                Family family = Find.FamilyManager.FoundHousehold(husband, wife, 0);
                husband.needs.mood!.CurLevel = 1f;
                wife.needs.mood!.CurLevel = 1f;
                husband.needs.food!.CurLevel = husband.needs.food.MaxLevel;
                wife.needs.food!.CurLevel = wife.needs.food.MaxLevel;
                family.lastBirthTick = -DemographyTuning.MinBirthIntervalTicks;

                var population = new List<Pawn> { husband, wife };
                Find.TickManager.DebugSetTicksGame(DemographyTuning.DemographyIntervalTicks);
                Find.FamilyManager.ProcessBirths(population);

                Assert.True(population.Count == 2 || population.Count == 3,
                    "seed " + seed + " produced " + population.Count + " pawns from one couple's single interval (expected 2 or 3, never more).");
            }
        }

        [Fact]
        public void Birth_chance_increases_with_mood()
        {
            float low = FamilyManager.ComputeBirthChance(0f, foodSecure: true);
            float mid = FamilyManager.ComputeBirthChance(0.5f, foodSecure: true);
            float high = FamilyManager.ComputeBirthChance(1f, foodSecure: true);

            Assert.True(low < mid);
            Assert.True(mid < high);
        }

        [Fact]
        public void Birth_chance_drops_when_food_insecure()
        {
            float secure = FamilyManager.ComputeBirthChance(0.7f, foodSecure: true);
            float insecure = FamilyManager.ComputeBirthChance(0.7f, foodSecure: false);

            Assert.True(insecure < secure);
        }

        [Fact]
        public void Unmarried_pawns_never_roll_a_birth()
        {
            Pawn a = NewAdult(gender: Gender.Male);
            Pawn b = NewAdult(gender: Gender.Female);

            var population = new List<Pawn> { a, b };
            Find.TickManager.DebugSetTicksGame(DemographyTuning.DemographyIntervalTicks);
            Find.FamilyManager.ProcessBirths(population);

            Assert.Equal(2, population.Count);
        }

        [Fact]
        public void Birth_respects_the_cooldown_after_a_recent_birth()
        {
            Pawn husband = NewAdult(age: 25f, gender: Gender.Male);
            Pawn wife = NewAdult(age: 25f, gender: Gender.Female);
            Family family = Find.FamilyManager.FoundHousehold(husband, wife, 0);
            family.lastBirthTick = 0;

            var population = new List<Pawn> { husband, wife };
            Find.TickManager.DebugSetTicksGame(DemographyTuning.DemographyIntervalTicks); // one year later; cooldown is two
            Find.FamilyManager.ProcessBirths(population);

            Assert.Equal(2, population.Count);
        }

        [Fact]
        public void Newborn_inherits_household_parents_and_generation()
        {
            Pawn husband = NewAdult(age: 25f, gender: Gender.Male);
            Pawn wife = NewAdult(age: 25f, gender: Gender.Female);
            Family family = Find.FamilyManager.FoundHousehold(husband, wife, 0);
            family.generation = 3;
            husband.relations.generation = 3;
            wife.relations.generation = 3;

            Find.TickManager.DebugSetTicksGame(DemographyTuning.DemographyIntervalTicks);
            Pawn? newborn = TryForceBirth(husband, wife, family);

            Assert.NotNull(newborn);
            Assert.Equal(family.id, newborn!.relations.familyId);
            Assert.Equal(4, newborn.relations.generation);
            Assert.Contains(husband.thingIDNumber, newborn.relations.ParentIds);
            Assert.Contains(wife.thingIDNumber, newborn.relations.ParentIds);
            Assert.Contains(newborn.thingIDNumber, husband.relations.childIds);
            Assert.Contains(newborn.thingIDNumber, wife.relations.childIds);
        }

        [Fact]
        public void Family_living_and_total_counts_grow_on_birth()
        {
            Pawn husband = NewAdult(age: 25f, gender: Gender.Male);
            Pawn wife = NewAdult(age: 25f, gender: Gender.Female);
            Family family = Find.FamilyManager.FoundHousehold(husband, wife, 0);
            Assert.Equal(2, family.livingCount);
            Assert.Equal(2, family.totalCount);

            Find.TickManager.DebugSetTicksGame(DemographyTuning.DemographyIntervalTicks);
            Pawn? newborn = TryForceBirth(husband, wife, family);

            Assert.NotNull(newborn);
            Assert.Equal(3, family.livingCount);
            Assert.Equal(3, family.totalCount);
        }

        // ---- death from age ----

        [Fact]
        public void Death_from_age_occurs_near_the_rolled_budget()
        {
            Pawn pawn = NewAdult(age: 20f);

            float deathAge = FindDeathAge(pawn);

            Assert.True(deathAge > 0f, "pawn never registered as due to die of age within 200 simulated years");
            // Human lifeExpectancy = 80, spread = 15 (DemographyTuning.LifespanSpreadYears) -> budget in [65, 95].
            Assert.InRange(deathAge, 65f, 95.25f);
        }

        [Fact]
        public void Hidden_lifespan_budget_has_no_public_accessor()
        {
            string[] forbiddenSubstrings = { "budget", "lifespan", "dieson", "yearsremaining", "deathage", "deathtick" };
            List<MemberInfo> publicMembers = typeof(Pawn_AgeTracker)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.MemberType == MemberTypes.Method || m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field)
                .ToList();

            foreach (MemberInfo member in publicMembers)
            {
                if (member.Name == nameof(Pawn_AgeTracker.ShouldDieOfAge)) continue;

                string lower = member.Name.ToLowerInvariant();
                foreach (string forbidden in forbiddenSubstrings)
                {
                    Assert.False(lower.Contains(forbidden), "public member '" + member.Name + "' looks like it exposes the hidden lifespan budget.");
                }
            }

            MethodInfo? shouldDie = typeof(Pawn_AgeTracker).GetMethod(nameof(Pawn_AgeTracker.ShouldDieOfAge));
            Assert.NotNull(shouldDie);
            Assert.Equal(typeof(bool), shouldDie!.ReturnType);
            Assert.Empty(shouldDie.GetParameters());

            Assert.Null(typeof(Pawn_AgeTracker).GetMethod("AdjustLifespan", BindingFlags.Public | BindingFlags.Instance));
            Assert.Null(typeof(Pawn_AgeTracker).GetMethod("RollLifespanBudget", BindingFlags.Public | BindingFlags.Instance));
        }

        [Fact]
        public void AdjustLifespan_moves_the_death_age_outcome()
        {
            Rand.Current = new RandomStream(2024);
            NameUseChecker.Clear();
            Pawn baseline = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 20f));
            float baselineDeathAge = FindDeathAge(baseline);

            Rand.Current = new RandomStream(2024);
            NameUseChecker.Clear();
            Pawn shortened = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 20f));
            // -600 days = -10 years on GenDate's own calendar (60 days/year, not 365 — see GenDate.cs).
            shortened.ageTracker.AdjustLifespan(-600f, "test: severe chronic illness");
            float shortenedDeathAge = FindDeathAge(shortened);

            Assert.True(shortenedDeathAge < baselineDeathAge,
                "shortening the lifespan budget should move the death age earlier (" + shortenedDeathAge + " vs " + baselineDeathAge + ")");
            Assert.InRange(baselineDeathAge - shortenedDeathAge, 9f, 11f);
        }

        [Fact]
        public void Death_from_age_routes_through_the_health_system()
        {
            Pawn pawn = NewAdult(age: 30f);
            pawn.ageTracker.DebugSetAge(500f);
            Assert.True(pawn.ageTracker.ShouldDieOfAge());

            var population = new List<Pawn> { pawn };
            Find.FamilyManager.ProcessDeathsFromAge(population);

            Assert.True(pawn.Dead);
            Assert.True(pawn.health.Dead);
        }

        [Fact]
        public void Death_from_age_records_DeathCause_on_the_chronicle()
        {
            Find.Storyteller = new Storyteller();
            Pawn pawn = NewAdult(age: 30f);
            pawn.ageTracker.DebugSetAge(500f);

            var population = new List<Pawn> { pawn };
            Find.FamilyManager.ProcessDeathsFromAge(population);

            ChronicleEntry entry = Assert.Single(Find.Storyteller.Chronicle, e => e.deathCause == DeathCause.Age);
            Assert.Equal(DeathCause.Age, entry.deathCause);
            Assert.Equal(pawn.Label, entry.targetLabel);
        }

        [Fact]
        public void Death_chronicle_includes_lifespan_adjustment_reasons_when_present()
        {
            Find.Storyteller = new Storyteller();
            Pawn pawn = NewAdult(age: 30f);
            pawn.ageTracker.AdjustLifespan(-100f, "test: chronic illness");
            pawn.ageTracker.DebugSetAge(500f);

            var population = new List<Pawn> { pawn };
            Find.FamilyManager.ProcessDeathsFromAge(population);

            ChronicleEntry entry = Assert.Single(Find.Storyteller.Chronicle, e => e.deathCause == DeathCause.Age);
            Assert.Contains("chronic illness", entry.targetLabel);
        }

        [Fact]
        public void Death_from_age_decrements_living_count_but_not_total()
        {
            Pawn husband = NewAdult(age: 25f, gender: Gender.Male);
            Pawn wife = NewAdult(age: 25f, gender: Gender.Female);
            Family family = Find.FamilyManager.FoundHousehold(husband, wife, 0);
            Assert.Equal(2, family.livingCount);
            Assert.Equal(2, family.totalCount);

            husband.ageTracker.DebugSetAge(500f);
            var population = new List<Pawn> { husband, wife };
            Find.FamilyManager.ProcessDeathsFromAge(population);

            Assert.Equal(1, family.livingCount);
            Assert.Equal(2, family.totalCount);
        }

        [Fact]
        public void Bereavement_markers_land_on_spouse_parents_and_children_only()
        {
            Pawn dying = NewAdult(age: 60f, gender: Gender.Male);
            Pawn spouse = NewAdult(age: 55f, gender: Gender.Female);
            Find.FamilyManager.FoundHousehold(dying, spouse, 0);

            Pawn parent = NewAdult(age: 85f, gender: Gender.Female);
            Pawn child = NewAdult(age: 20f, gender: Gender.Male);
            Pawn bystander = NewAdult(age: 40f);

            dying.relations.SetParents(parent.thingIDNumber, Pawn_RelationsTracker.None);
            dying.relations.Notify_ChildBorn(child.thingIDNumber);
            child.relations.SetParents(dying.thingIDNumber, spouse.thingIDNumber);

            dying.ageTracker.DebugSetAge(500f);

            var population = new List<Pawn> { dying, spouse, parent, child, bystander };
            Find.FamilyManager.ProcessDeathsFromAge(population);

            Assert.True(spouse.relations.bereavementTick >= 0);
            Assert.Equal(DeathCause.Age, spouse.relations.bereavementCause);
            Assert.True(parent.relations.bereavementTick >= 0);
            Assert.True(child.relations.bereavementTick >= 0);
            Assert.Equal(-1, bystander.relations.bereavementTick);
        }

        [Fact]
        public void A_surviving_spouse_is_freed_to_remarry()
        {
            Pawn dying = NewAdult(age: 60f, gender: Gender.Male);
            Pawn widow = NewAdult(age: 55f, gender: Gender.Female);
            Find.FamilyManager.FoundHousehold(dying, widow, 0);
            Assert.True(widow.relations.IsMarried);

            dying.ageTracker.DebugSetAge(500f);
            var population = new List<Pawn> { dying, widow };
            Find.FamilyManager.ProcessDeathsFromAge(population);

            Assert.False(widow.relations.IsMarried);

            Pawn suitor = NewAdult(gender: Gender.Male);
            population.Add(suitor);
            Find.FamilyManager.ProcessMarriages(population);

            Assert.Equal(suitor.thingIDNumber, widow.relations.spouseId);
        }

        // ---- Scribe, determinism, gating, scope ----

        [Fact]
        public void Scribe_round_trips_households_relations_and_generation()
        {
            Pawn husband = NewAdult(age: 30f, gender: Gender.Male);
            Pawn wife = NewAdult(age: 28f, gender: Gender.Female);
            Family family = Find.FamilyManager.FoundHousehold(husband, wife, 100);
            family.generation = 2;
            husband.relations.generation = 2;
            wife.relations.generation = 2;

            Find.TickManager.DebugSetTicksGame(DemographyTuning.DemographyIntervalTicks);
            Pawn? child = TryForceBirth(husband, wife, family);
            Assert.NotNull(child);

            var holder = new DemographyHolder { familyManager = Find.FamilyManager, pawns = new List<Pawn> { husband, wife, child! } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            Find.FamilyManager = new FamilyManager();
            DemographyHolder loaded = Scribe.Load<DemographyHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Pawn lh = loaded.pawns![0];
            Pawn lw = loaded.pawns[1];
            Pawn lc = loaded.pawns[2];

            Assert.Equal(family.id, lh.relations.familyId);
            Assert.Equal(lw.thingIDNumber, lh.relations.spouseId);
            Assert.Equal(2, lh.relations.generation);
            Assert.Equal(3, lc.relations.generation);
            Assert.Contains(lh.thingIDNumber, lc.relations.ParentIds);
            Assert.Contains(lw.thingIDNumber, lc.relations.ParentIds);

            Family loadedFamily = loaded.familyManager!.Families.First(f => f.id == family.id);
            Assert.Equal(family.generation, loadedFamily.generation);
            Assert.Equal(family.livingCount, loadedFamily.livingCount);
            Assert.Equal(family.totalCount, loadedFamily.totalCount);
            Assert.Equal(family.surname, loadedFamily.surname);
            Assert.NotEmpty(loadedFamily.surname);
        }

        [Fact]
        public void Determinism_same_seed_produces_the_same_population_tree()
        {
            List<(int id, int familyId, int generation, Gender gender, bool dead)> run1 = RunSmallSimulation(777, 6, 60);
            List<(int id, int familyId, int generation, Gender gender, bool dead)> run2 = RunSmallSimulation(777, 6, 60);

            Assert.Equal(run1.Count, run2.Count);
            for (int i = 0; i < run1.Count; i++)
            {
                Assert.Equal(run1[i], run2[i]);
            }
        }

        private static List<(int id, int familyId, int generation, Gender gender, bool dead)> RunSmallSimulation(int seed, int founders, int years)
        {
            Find.TickManager = new TickManager();
            Rand.Current = new RandomStream(seed);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            Find.FamilyManager = new FamilyManager();

            var population = new List<Pawn>();
            for (int i = 0; i < founders; i++)
            {
                population.Add(NewAdult(age: 20f + i % 5, gender: i % 2 == 0 ? Gender.Male : Gender.Female));
            }

            int tick = 0;
            for (int year = 0; year < years; year++)
            {
                tick += DemographyTuning.DemographyIntervalTicks;
                AdvanceYear(population, DemographyTuning.DemographyIntervalTicks);
                Find.TickManager.DebugSetTicksGame(tick);
                Find.FamilyManager.DemographyTick(population);
            }

            return population
                .Select(p => (p.thingIDNumber, p.relations.familyId, p.relations.generation, p.gender, p.Dead))
                .ToList();
        }

        [Fact]
        public void DemographyTick_only_acts_on_its_interval_boundary()
        {
            Pawn a = NewAdult(gender: Gender.Male);
            Pawn b = NewAdult(gender: Gender.Female);
            var population = new List<Pawn> { a, b };

            Find.TickManager.DebugSetTicksGame(DemographyTuning.DemographyIntervalTicks - 1);
            Find.FamilyManager.DemographyTick(population);
            Assert.False(a.relations.IsMarried);

            Find.TickManager.DebugSetTicksGame(DemographyTuning.DemographyIntervalTicks);
            Find.FamilyManager.DemographyTick(population);
            Assert.True(a.relations.IsMarried);
        }

        [Fact]
        public void Animals_are_excluded_from_marriage_and_death_from_age()
        {
            var dogA = new Pawn(Husky, "RexA");
            var dogB = new Pawn(Husky, "RexB");
            dogA.ageTracker.DebugSetAge(3f);
            dogB.ageTracker.DebugSetAge(3f);

            var population = new List<Pawn> { dogA, dogB };
            Find.TickManager.DebugSetTicksGame(DemographyTuning.DemographyIntervalTicks);
            Find.FamilyManager.DemographyTick(population);

            Assert.False(dogA.relations.IsMarried);
            Assert.Equal(2, population.Count);

            dogA.ageTracker.DebugSetAge(500f);
            Assert.False(dogA.ageTracker.ShouldDieOfAge());
        }

        [Fact]
        public void Already_married_pawns_are_never_reconsidered_for_marriage()
        {
            Pawn husband = NewAdult(gender: Gender.Male);
            Pawn wife = NewAdult(gender: Gender.Female);
            Find.FamilyManager.FoundHousehold(husband, wife, 0);

            Pawn thirdWheel = NewAdult(gender: Gender.Female);
            var population = new List<Pawn> { husband, wife, thirdWheel };
            Find.FamilyManager.ProcessMarriages(population);

            Assert.Equal(wife.thingIDNumber, husband.relations.spouseId);
            Assert.False(thirdWheel.relations.IsMarried);
        }

        [Fact]
        public void FoundHousehold_supports_a_single_founder_for_future_immigration_style_founding()
        {
            Pawn founder = NewAdult(gender: Gender.Male);

            Family family = Find.FamilyManager.FoundHousehold(founder, null, 500);

            Assert.Equal(1, family.livingCount);
            Assert.Equal(1, family.totalCount);
            Assert.Equal(founder.thingIDNumber, family.founderIdA);
            Assert.Equal(Pawn_RelationsTracker.None, family.founderIdB);
            Assert.Equal(family.id, founder.relations.familyId);
            Assert.False(founder.relations.IsMarried);
        }
        // ---- lifespan modifiers: the budget is a living thing, not a verdict ----

        [Fact]
        public void Medical_research_lengthens_the_lives_of_those_born_after_it()
        {
            // Same seed either side, so the only difference is what the civilization knows at the moment of birth.
            float Average(bool withMedicine)
            {
                Find.ResearchManager = new global::SimWorld.Research.ResearchManager();
                if (withMedicine)
                {
                    foreach (var project in global::SimWorld.Defs.DefDatabase<global::SimWorld.Research.ResearchProjectDef>.AllDefsListForReading)
                    {
                        if (project.tags != null && project.tags.Contains(DemographyTuning.MedicineTrackTag))
                        {
                            Find.ResearchManager.FinishProject(project);
                        }
                    }
                }
                Rand.Current = new RandomStream(90210);
                float total = 0f;
                const int n = 60;
                for (int i = 0; i < n; i++)
                {
                    Pawn p = global::SimWorld.Pawns.Generation.PawnGenerator.GeneratePawn(
                        new global::SimWorld.Pawns.Generation.PawnGenerationRequest(PawnKindDefOf.Colonist));
                    total += p.ageTracker.DebugDeathAgeYears;
                }
                return total / n;
            }

            float without = Average(false);
            float with = Average(true);
            Assert.True(with > without, $"medicine should lengthen life: {with} vs {without}");
            Assert.InRange(with - without, 1f, DemographyTuning.MedicineLifespanBonusYears + 1f);
        }

        [Fact]
        public void Starving_spends_the_lifespan_budget()
        {
            Pawn p = global::SimWorld.Pawns.Generation.PawnGenerator.GeneratePawn(
                new global::SimWorld.Pawns.Generation.PawnGenerationRequest(PawnKindDefOf.Colonist));
            float before = p.ageTracker.DebugDeathAgeYears;

            for (int i = 0; i < 20; i++) p.Notify_StarvationInterval(true);
            float starved = p.ageTracker.DebugDeathAgeYears;
            Assert.True(starved < before, "hunger should shorten the budget");

            // Being fed stops the bleeding; it does not give the years back.
            for (int i = 0; i < 20; i++) p.Notify_StarvationInterval(false);
            Assert.Equal(starved, p.ageTracker.DebugDeathAgeYears, 3);
        }

        // ---- demography.migration: arrivals ----

        private static void ForceNeutralNeeds(Pawn p)
        {
            if (p.needs?.mood != null) p.needs.mood.CurLevel = p.needs.mood.MaxLevel * 0.5f;
            if (p.needs?.food != null) p.needs.food.CurLevel = p.needs.food.MaxLevel * 0.5f;
        }

        private static void ForceGoodNeeds(Pawn p)
        {
            if (p.needs?.mood != null) p.needs.mood.CurLevel = p.needs.mood.MaxLevel;
            if (p.needs?.food != null) p.needs.food.CurLevel = p.needs.food.MaxLevel;
        }

        private static void ForceDistressedNeeds(Pawn p)
        {
            if (p.needs?.mood != null) p.needs.mood.CurLevel = 0f;
            if (p.needs?.food != null) p.needs.food.CurLevel = 0f;
        }

        [Fact]
        public void Arrival_chance_increases_with_quality_and_era()
        {
            float low = MigrationManager.ArrivalChance(0f, null);
            float neutral = MigrationManager.ArrivalChance(0.5f, null);
            float high = MigrationManager.ArrivalChance(1f, null);
            Assert.True(low < neutral);
            Assert.True(neutral < high);

            var early = new global::SimWorld.Research.EraDef { defName = "TestEraEarly", order = 0 };
            var later = new global::SimWorld.Research.EraDef { defName = "TestEraLater", order = 10 };
            Assert.True(MigrationManager.ArrivalChance(0.5f, early) < MigrationManager.ArrivalChance(0.5f, later));
        }

        [Fact]
        public void Settlement_quality_is_neutral_with_no_living_population()
        {
            Assert.Equal(0.5f, MigrationManager.SettlementQuality(new List<Pawn>()));

            var deadOnly = new List<Pawn> { NewAdult() };
            deadOnly[0].health.Kill(null, null);
            Assert.Equal(0.5f, MigrationManager.SettlementQuality(deadOnly));
        }

        [Fact]
        public void Settlement_quality_averages_mood_and_food_across_the_living()
        {
            Pawn happy = NewAdult();
            ForceGoodNeeds(happy);
            Pawn miserable = NewAdult();
            ForceDistressedNeeds(miserable);

            float quality = MigrationManager.SettlementQuality(new List<Pawn> { happy, miserable });
            Assert.InRange(quality, 0.4f, 0.6f); // one maxed, one zeroed -> roughly the midpoint
        }

        [Fact]
        public void Migration_only_acts_on_its_interval_boundary()
        {
            var population = new List<Pawn> { NewAdult() };
            ForceGoodNeeds(population[0]);

            Find.TickManager.DebugSetTicksGame(MigrationTuning.MigrationIntervalTicks - 1);
            bool arrivedOffBoundary = MigrationManager.MigrationTick(population, PawnKindDefOf.Colonist);
            Assert.False(arrivedOffBoundary);
            Assert.Single(population);
        }

        [Fact]
        public void An_arriving_migrant_founds_a_household_and_is_promoted_to_full()
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            var population = new List<Pawn> { NewAdult() };
            ForceGoodNeeds(population[0]);
            Find.TickManager.DebugSetTicksGame(MigrationTuning.MigrationIntervalTicks);

            Pawn? migrant = null;
            for (int seed = 0; seed < 500 && migrant == null; seed++)
            {
                Rand.Current = new RandomStream(seed * 104729 + 1);
                var attempt = new List<Pawn>(population);
                if (MigrationManager.ProcessArrivals(attempt, PawnKindDefOf.Colonist))
                {
                    migrant = attempt[attempt.Count - 1];
                }
            }

            Assert.NotNull(migrant);
            Assert.True(migrant!.relations.HasFamily);
            Assert.False(migrant.relations.IsMarried); // a solo founder, not paired
            Assert.Equal(PawnTier.Full, migrant.tier.Tier);
            Assert.True(migrant.ageTracker.AgeBiologicalYearsFloat >= DemographyTuning.MinMarriageAgeYears);
            Assert.Contains(Find.Storyteller.Chronicle, e => e.incidentDefName.StartsWith("Migration:") && e.incidentDefName.Contains("founds a household"));
        }

        [Fact]
        public void Migration_never_produces_more_than_one_arrival_per_interval()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                Rand.Current = new RandomStream(seed);
                var population = new List<Pawn> { NewAdult() };
                ForceGoodNeeds(population[0]);
                Find.TickManager.DebugSetTicksGame(MigrationTuning.MigrationIntervalTicks);
                MigrationManager.ProcessArrivals(population, PawnKindDefOf.Colonist);
                Assert.True(population.Count == 1 || population.Count == 2,
                    "seed " + seed + " produced " + population.Count + " pawns from a single interval's arrival roll.");
            }
        }

        // ---- demography.migration: departures ----

        [Fact]
        public void A_distressed_household_can_depart_together_and_shrinks_its_familys_living_count()
        {
            Pawn husband = NewAdult(gender: Gender.Male);
            Pawn wife = NewAdult(gender: Gender.Female);
            Family family = Find.FamilyManager.FoundHousehold(husband, wife, 0);
            ForceDistressedNeeds(husband);
            ForceDistressedNeeds(wife);

            var population = new List<Pawn> { husband, wife };
            int departed = 0;
            for (int seed = 0; seed < 200 && departed == 0; seed++)
            {
                Rand.Current = new RandomStream(seed * 7919 + 3);
                departed = MigrationManager.ProcessDepartures(population);
            }

            Assert.Equal(1, departed);
            Assert.Empty(population);
            Assert.Equal(0, family.livingCount);
            Assert.Equal(2, family.totalCount); // history is not erased, only the living roster shrinks
        }

        [Fact]
        public void A_household_above_the_distress_threshold_never_departs()
        {
            Pawn husband = NewAdult(gender: Gender.Male);
            Pawn wife = NewAdult(gender: Gender.Female);
            Find.FamilyManager.FoundHousehold(husband, wife, 0);
            ForceGoodNeeds(husband);
            ForceGoodNeeds(wife);

            var population = new List<Pawn> { husband, wife };
            for (int seed = 0; seed < 200; seed++)
            {
                Rand.Current = new RandomStream(seed);
                MigrationManager.ProcessDepartures(population);
            }

            Assert.Equal(2, population.Count);
        }

        // ---- demography.migration: Settlement-aware overload and its floor ----

        [Fact]
        public void Settlement_migration_grows_statistical_population_only_when_quality_is_above_neutral()
        {
            var settlement = new global::SimWorld.World.Settlement(
                global::SimWorld.World.WorldObjectDefOf.Settlement, tile: 1, faction: null, name: "Riverbend", foundingTick: 0);
            settlement.AddStatisticalPeople(10_000);
            Find.TickManager.DebugSetTicksGame(MigrationTuning.MigrationIntervalTicks);

            // No live citizens at all -> SettlementQuality reads neutral (0.5) -> signedQuality is 0 -> no change
            // in either direction. Neutral is the hinge, not a floor.
            MigrationManager.MigrationTick(settlement, PawnKindDefOf.Colonist);
            Assert.Equal(10_000, settlement.StatisticalPopulation);
        }

        [Fact]
        public void Settlement_migration_sheds_statistical_population_when_citizens_are_suffering()
        {
            // Somewhere worth leaving has to be as expressible as somewhere worth going. This could not be
            // asserted while Settlement's only population mutator refused a negative count: net migration was
            // floored at zero, so an unlivable settlement merely stopped attracting anyone.
            var settlement = new global::SimWorld.World.Settlement(
                global::SimWorld.World.WorldObjectDefOf.Settlement, tile: 3, faction: null, name: "Ashfall", foundingTick: 0);
            Pawn resident = NewAdult();
            ForceDistressedNeeds(resident);
            settlement.AddCitizen(resident);
            settlement.AddStatisticalPeople(10_000);
            Find.TickManager.DebugSetTicksGame(MigrationTuning.MigrationIntervalTicks);

            MigrationManager.MigrationTick(settlement, PawnKindDefOf.Colonist);

            Assert.True(settlement.StatisticalPopulation < 10_000,
                "a settlement nobody wants to live in should lose people, not merely fail to gain them.");
        }

        [Fact]
        public void A_statistical_cohort_never_goes_below_empty()
        {
            var settlement = new global::SimWorld.World.Settlement(
                global::SimWorld.World.WorldObjectDefOf.Settlement, tile: 4, faction: null, name: "Lastditch", foundingTick: 0);
            settlement.AddStatisticalPeople(5);

            Assert.Equal(5, settlement.RemoveStatisticalPeople(500));
            Assert.Equal(0, settlement.StatisticalPopulation);
            Assert.Equal(0, settlement.RemoveStatisticalPeople(1));
        }

        [Fact]
        public void Settlement_migration_grows_statistical_population_when_citizens_are_thriving()
        {
            var settlement = new global::SimWorld.World.Settlement(
                global::SimWorld.World.WorldObjectDefOf.Settlement, tile: 2, faction: null, name: "Goldenvale", foundingTick: 0);
            Pawn resident = NewAdult();
            ForceGoodNeeds(resident);
            settlement.AddCitizen(resident);
            settlement.AddStatisticalPeople(10_000);
            Find.TickManager.DebugSetTicksGame(MigrationTuning.MigrationIntervalTicks);

            MigrationManager.MigrationTick(settlement, PawnKindDefOf.Colonist);

            Assert.True(settlement.StatisticalPopulation > 10_000,
                "a thriving settlement should attract statistical growth beyond the natural-growth mechanism.");
        }

        // ---- demography.migration: pacing over a simulated span ----

        [Fact]
        public void Migration_contributes_a_bounded_share_of_growth_over_a_simulated_century()
        {
            // A neutral-conditions, ten-founder population, run 100 years with both demography and migration
            // ticking every interval. This pins the trend the brief asks for ("a band... over a simulated
            // span"), not a literal: migration should add a real, visible number of arrivals over a century
            // (it is meant to matter) without swamping births as the dominant channel of growth (it is meant
            // to be a second channel, not the whole story) — see MigrationTuning.ArrivalBaseChancePerInterval's
            // own doc for why it was sized against DemographyTuning.BaseBirthChancePerInterval.
            Rand.Current = new RandomStream(24601);
            var population = new List<Pawn>();
            for (int i = 0; i < 10; i++)
            {
                Pawn p = NewAdult(age: 20f + i, gender: i % 2 == 0 ? Gender.Male : Gender.Female);
                ForceNeutralNeeds(p);
                population.Add(p);
            }

            int tick = 0;
            int arrivals = 0;
            for (int year = 0; year < 100; year++)
            {
                tick += DemographyTuning.DemographyIntervalTicks;
                AdvanceYear(population, DemographyTuning.DemographyIntervalTicks);
                Find.TickManager.DebugSetTicksGame(tick);
                Find.FamilyManager.DemographyTick(population);
                foreach (Pawn p in population) if (!p.Dead) ForceNeutralNeeds(p);
                if (MigrationManager.MigrationTick(population, PawnKindDefOf.Colonist)) arrivals++;
            }

            int living = population.Count(p => !p.Dead);
            Assert.True(living > 10, "expected demography plus migration to grow the population; got " + living);
            // At neutral quality (0.5) the per-interval arrival chance is ArrivalBaseChancePerInterval * 1.0 =
            // 0.25, so ~25 arrivals is the expectation over 100 independent yearly rolls; a generous band around
            // that (not a tight one — this is a Bernoulli trial, not a fixed count) catches a formula regression
            // without being a coin-flip-fragile test.
            Assert.InRange(arrivals, 5, 50);
        }
    }
}
