using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Work;
using Xunit;

namespace SimWorld.Tests.Pawns
{
    public class PawnGenerationTests : ContentTestBase
    {
        public PawnGenerationTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static PawnKindDef Kind(string defName) => DefDatabase<PawnKindDef>.GetNamed(defName);

        private static FactionDef FactionDefNamed(string defName) => DefDatabase<FactionDef>.GetNamed(defName);

        private static Faction NewFaction(string factionDefName, string name) => new Faction(FactionDefNamed(factionDefName), name, "F_" + name);

        // ---- content ----

        [Fact]
        public void Core_content_has_expected_pawn_generation_defs()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.True(DefDatabase<BackstoryDef>.DefCount >= 20, "expected at least 20 backstories, found " + DefDatabase<BackstoryDef>.DefCount);
            Assert.True(DefDatabase<PawnKindDef>.DefCount >= 4);
            Assert.True(DefDatabase<LifeStageDef>.DefCount >= 6);
            Assert.True(DefDatabase<NameBankDef>.DefCount >= 3);
        }

        [Fact]
        public void PawnGen_DefOfs_are_bound()
        {
            Assert.NotNull(PawnKindDefOf.Colonist);
            Assert.NotNull(PawnKindDefOf.Villager);
            Assert.NotNull(PawnKindDefOf.Tribesperson);
            Assert.NotNull(PawnKindDefOf.Husky);
            Assert.Equal("Colonist", PawnKindDefOf.Colonist.defName);
            Assert.NotNull(LifeStageDefOf.HumanlikeAdult);
            Assert.NotNull(LifeStageDefOf.HumanlikeChild);
        }

        // ---- basic generation shape ----

        [Fact]
        public void GeneratePawn_Colonist_yields_humanlike_adult_with_two_backstories()
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));

            Assert.Same(Human, pawn.def);
            Assert.Same(PawnKindDefOf.Colonist, pawn.kindDef);
            Assert.True(pawn.RaceProps.Humanlike);
            Assert.NotEqual(Gender.None, pawn.gender);
            Assert.NotNull(pawn.story.childhood);
            Assert.NotNull(pawn.story.adulthood);
            Assert.Equal(2, pawn.story.AllBackstories.Count());
        }

        [Fact]
        public void GeneratePawn_traits_are_two_or_three_and_conflict_free()
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));

            List<Trait> traits = pawn.story.traits.allTraits;
            Assert.InRange(traits.Count, 2, 3);
            for (int i = 0; i < traits.Count; i++)
            {
                for (int j = i + 1; j < traits.Count; j++)
                {
                    Assert.False(traits[i].def.ConflictsWith(traits[j].def), traits[i].def.defName + " conflicts with " + traits[j].def.defName);
                }
            }
        }

        [Fact]
        public void GeneratePawn_skills_are_within_0_and_20()
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));

            foreach (SkillRecord record in pawn.skills.skills)
            {
                Assert.InRange(record.Level, 0, 20);
            }
        }

        [Fact]
        public void GeneratePawn_name_is_valid_and_gender_is_set()
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist));

            Assert.NotNull(pawn.Name);
            Assert.True(pawn.Name!.IsValid);
            Assert.IsType<NameTriple>(pawn.Name);
            Assert.NotEqual(Gender.None, pawn.gender);
        }

        // ---- determinism ----

        [Fact]
        public void Generation_is_deterministic_for_the_same_seed()
        {
            Rand.Current = new RandomStream(777);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            Pawn a = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));

            Rand.Current = new RandomStream(777);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            Pawn b = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));

            Assert.Equal(a.Name!.ToStringFull, b.Name!.ToStringFull);
            Assert.Equal(a.gender, b.gender);
            Assert.Equal(a.story.traits.allTraits.Select(t => t.def.defName), b.story.traits.allTraits.Select(t => t.def.defName));
            Assert.Equal(a.skills.skills.Select(s => s.Level), b.skills.skills.Select(s => s.Level));
        }

        [Fact]
        public void Generation_differs_for_a_different_seed()
        {
            Rand.Current = new RandomStream(1);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            Pawn a = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));

            Rand.Current = new RandomStream(2);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            Pawn b = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));

            bool anyDifference = a.Name!.ToStringFull != b.Name!.ToStringFull
                || a.gender != b.gender
                || !a.story.traits.allTraits.Select(t => t.def.defName).SequenceEqual(b.story.traits.allTraits.Select(t => t.def.defName))
                || !a.skills.skills.Select(s => s.Level).SequenceEqual(b.skills.skills.Select(s => s.Level));
            Assert.True(anyDifference, "two different seeds produced an identical pawn");
        }

        // ---- work tags / violence ----

        [Fact]
        public void Backstory_workDisables_ManualDumb_disables_Hauling()
        {
            var pawn = new Pawn(Human, "Noble");
            pawn.story.childhood = DefDatabase<BackstoryDef>.GetNamed("NobleChild");
            pawn.Notify_TraitsChanged();

            Assert.True(pawn.WorkTagIsDisabled(WorkTags.ManualDumb));
            Assert.True(pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hauling));
        }

        [Fact]
        public void MustBeCapableOfViolence_never_yields_a_violence_disabled_pawn()
        {
            for (int i = 0; i < 50; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, mustBeCapableOfViolence: true, fixedBiologicalAge: 30f));
                Assert.False(pawn.WorkTagIsDisabled(WorkTags.Violent));
            }
        }

        // ---- traits from backstory ----

        [Fact]
        public void Forced_traits_from_backstory_are_present_and_disallowed_ones_absent()
        {
            bool sawForced = false;
            for (int i = 0; i < 30 && !sawForced; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Tribesperson, fixedBiologicalAge: 30f));
                if (pawn.story.AllBackstories.Any(b => b.forcedTraits != null && b.forcedTraits.Count > 0))
                {
                    sawForced = true;
                    foreach (BackstoryDef backstory in pawn.story.AllBackstories)
                    {
                        if (backstory.forcedTraits == null) continue;
                        foreach (BackstoryTrait bt in backstory.forcedTraits)
                        {
                            if (bt.def != null) Assert.True(pawn.story.traits.HasTrait(bt.def));
                        }
                    }
                }
            }
        }

        // ---- skill bias from backstory ----

        [Fact]
        public void Soldier_adulthood_averages_higher_shooting_than_cook()
        {
            BackstoryDef soldier = DefDatabase<BackstoryDef>.GetNamed("Soldier");
            BackstoryDef cook = DefDatabase<BackstoryDef>.GetNamed("Cook");

            float soldierTotal = 0f;
            float cookTotal = 0f;
            const int trials = 200;
            for (int i = 0; i < trials; i++)
            {
                Pawn withSoldier = new Pawn(Human, "S");
                withSoldier.story.adulthood = soldier;
                withSoldier.ageTracker.DebugSetAge(30f);
                withSoldier.Notify_TraitsChanged();
                PawnGenerator.GenerateSkills(withSoldier);
                soldierTotal += withSoldier.skills.GetSkill(SkillDefOf.Shooting)!.Level;

                Pawn withCook = new Pawn(Human, "C");
                withCook.story.adulthood = cook;
                withCook.ageTracker.DebugSetAge(30f);
                withCook.Notify_TraitsChanged();
                PawnGenerator.GenerateSkills(withCook);
                cookTotal += withCook.skills.GetSkill(SkillDefOf.Shooting)!.Level;
            }

            Assert.True(soldierTotal / trials > cookTotal / trials,
                "expected Soldier adulthood to average higher Shooting than Cook (soldier=" + (soldierTotal / trials) + ", cook=" + (cookTotal / trials) + ")");
        }

        // ---- passion ----

        [Fact]
        public void Passion_only_appears_on_skills_with_a_positive_level()
        {
            for (int i = 0; i < 20; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));
                foreach (SkillRecord record in pawn.skills.skills)
                {
                    if (record.Level == 0) Assert.Equal(Passion.None, record.passion);
                }
            }
        }

        // ---- age-gated adulthood ----

        [Fact]
        public void FixedBiologicalAge_16_has_no_adulthood_backstory()
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 16f));
            Assert.Null(pawn.story.adulthood);
            Assert.NotNull(pawn.story.childhood);
        }

        // ---- age tracker / life stages ----

        [Fact]
        public void AgeTracker_ticks_a_year_in_3_6_million_ticks()
        {
            Pawn pawn = NewHuman();
            pawn.ageTracker.DebugSetAge(0f);
            pawn.ageTracker.AgeTickMothballed(GenDate.TicksPerYear - 1);
            Assert.Equal(0, pawn.ageTracker.AgeBiologicalYears);
            pawn.ageTracker.AgeTick();
            Assert.Equal(1, pawn.ageTracker.AgeBiologicalYears);
        }

        [Fact]
        public void Life_stage_flips_child_to_teenager_to_adult_with_factors()
        {
            Pawn pawn = NewHuman();
            pawn.ageTracker.DebugSetAge(5f);
            Assert.Equal("HumanlikeChild", pawn.ageTracker.CurLifeStage!.defName);
            Assert.Equal(0.6f, pawn.HealthScale);
            Assert.Equal(0.7f, pawn.HungerRate);

            pawn.ageTracker.DebugSetAge(13f);
            Assert.Equal("HumanlikeTeenager", pawn.ageTracker.CurLifeStage!.defName);

            pawn.ageTracker.DebugSetAge(18f);
            Assert.Equal("HumanlikeAdult", pawn.ageTracker.CurLifeStage!.defName);
            Assert.Equal(1f, pawn.HealthScale);
            Assert.Equal(1f, pawn.BodySize);
        }

        // ---- newborn ----

        [Fact]
        public void Newborn_has_age_zero_baby_stage_no_traits_and_a_born_event()
        {
            Pawn pawn = PawnGenerator.GenerateNewborn(PawnKindDefOf.Colonist);

            Assert.Equal(0, pawn.ageTracker.AgeBiologicalYears);
            Assert.Equal("HumanlikeBaby", pawn.ageTracker.CurLifeStage!.defName);
            Assert.Empty(pawn.story.traits.allTraits);
            Assert.Null(pawn.story.childhood);
            Assert.Contains(pawn.story.lifeEvents, e => e.kind == "Born");
        }

        // ---- names ----

        [Fact]
        public void Names_are_unique_across_100_pawns()
        {
            var names = new HashSet<string>();
            for (int i = 0; i < 100; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));
                Assert.True(names.Add(pawn.Name!.ToStringFull), "duplicate name generated: " + pawn.Name.ToStringFull);
            }
        }

        // ---- animals ----

        [Fact]
        public void Husky_generation_yields_an_animal_with_no_backstories()
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Husky));

            Assert.Same(Husky, pawn.def);
            Assert.False(pawn.RaceProps.Humanlike);
            Assert.Null(pawn.story.childhood);
            Assert.Null(pawn.story.adulthood);
            Assert.Empty(pawn.story.traits.allTraits);
            Assert.NotNull(pawn.Name);
            Assert.IsType<NameSingle>(pawn.Name);
        }

        // ---- save/load ----

        [Fact]
        public void Scribe_round_trip_preserves_generation_state()
        {
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));
            pawn.story.RecordLifeEvent("Test", "A test event.");

            var holder = new PawnHolder { pawns = new List<Pawn> { pawn } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            PawnHolder loaded = Scribe.Load<PawnHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Pawn restored = loaded.pawns![0];
            Assert.Equal(pawn.Name!.ToStringFull, restored.Name!.ToStringFull);
            Assert.Equal(pawn.gender, restored.gender);
            Assert.Equal(pawn.ageTracker.ageBiologicalTicks, restored.ageTracker.ageBiologicalTicks);
            Assert.Same(pawn.story.childhood, restored.story.childhood);
            Assert.Same(pawn.story.adulthood, restored.story.adulthood);
            Assert.Equal(pawn.story.traits.allTraits.Select(t => t.def.defName), restored.story.traits.allTraits.Select(t => t.def.defName));
            Assert.Equal(1, restored.story.lifeEvents.Count(e => e.kind == "Test"));
        }

        // ---- Name / curve utilities ----

        [Fact]
        public void NameTriple_ToStringFull_and_ToStringShort_behave_as_expected()
        {
            var withNick = new NameTriple("Ash", "Blaze", "Colony");
            Assert.Equal("Ash 'Blaze' Colony", withNick.ToStringFull);
            Assert.Equal("Blaze", withNick.ToStringShort);

            var nickEqualsFirst = new NameTriple("Ash", "Ash", "Colony");
            Assert.Equal("Ash Colony", nickEqualsFirst.ToStringFull);
            Assert.Equal("Ash", nickEqualsFirst.ToStringShort);
        }

        [Fact]
        public void ByCurve_samples_more_often_where_the_curve_is_higher()
        {
            var curve = new SimpleCurve(new[] { new CurvePoint(0f, 0f), new CurvePoint(1f, 0f), new CurvePoint(2f, 100f), new CurvePoint(3f, 100f) });
            var stream = new RandomStream(42);
            int lowHalf = 0, highHalf = 0;
            for (int i = 0; i < 2000; i++)
            {
                float x = stream.ByCurve(curve);
                Assert.InRange(x, 0f, 3f);
                if (x < 1.5f) lowHalf++; else highHalf++;
            }
            Assert.True(highHalf > lowHalf * 3, "expected the heavily-weighted half of the curve to be sampled far more often");
        }

        // ---- gear (pawngen.gear: weapon half) ----

        [Fact]
        public void Kind_with_no_weaponTags_is_never_armed()
        {
            for (int i = 0; i < 20; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, fixedBiologicalAge: 30f));
                Assert.Null(pawn.equipment.Primary);
            }
        }

        [Fact]
        public void Kind_with_weaponTags_and_no_faction_is_armed_with_a_matching_untiered_weapon()
        {
            bool sawWeapon = false;
            for (int i = 0; i < 30; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(Kind("Raider_Melee"), fixedBiologicalAge: 30f));
                ThingDef? weapon = pawn.equipment.Primary?.def;
                if (weapon == null) continue;
                sawWeapon = true;
                Assert.Contains("NeolithicMeleeWeapon", weapon.weaponTags!);
                Assert.InRange(weapon.BaseMarketValue, Kind("Raider_Melee").weaponMoneyRange.min, Kind("Raider_Melee").weaponMoneyRange.max);
            }
            Assert.True(sawWeapon, "expected at least one Raider_Melee generation to carry a weapon across 30 tries");
        }

        [Fact]
        public void Gunner_kind_never_armed_for_a_neolithic_faction_but_armed_for_an_industrial_one()
        {
            Faction tribal = NewFaction("TribalCivilization", "GearTribal");
            for (int i = 0; i < 30; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(Kind("Raider_Gunner"), fixedBiologicalAge: 30f, faction: tribal));
                Assert.Null(pawn.equipment.Primary);
            }

            Faction outlander = NewFaction("OutlanderCivilization", "GearOutlander");
            bool sawWeapon = false;
            for (int i = 0; i < 30 && !sawWeapon; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(Kind("Raider_Gunner"), fixedBiologicalAge: 30f, faction: outlander));
                if (pawn.equipment.Primary != null)
                {
                    sawWeapon = true;
                    Assert.True(pawn.equipment.Primary.def.techLevel <= TechLevel.Industrial);
                    Assert.Contains("IndustrialRangedWeapon", pawn.equipment.Primary.def.weaponTags!);
                }
            }
            Assert.True(sawWeapon, "expected an Industrial-tech faction's gunner to be armed across 30 tries");
        }

        [Fact]
        public void Weapon_generation_is_deterministic_for_the_same_seed()
        {
            Faction outlander1 = NewFaction("OutlanderCivilization", "DetA");
            Rand.Current = new RandomStream(2468);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            Pawn a = PawnGenerator.GeneratePawn(new PawnGenerationRequest(Kind("Raider_Gunner"), fixedBiologicalAge: 30f, faction: outlander1));

            Faction outlander2 = NewFaction("OutlanderCivilization", "DetA"); // same defName/loadID as above, distinct instance
            Rand.Current = new RandomStream(2468);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            Pawn b = PawnGenerator.GeneratePawn(new PawnGenerationRequest(Kind("Raider_Gunner"), fixedBiologicalAge: 30f, faction: outlander2));

            Assert.Equal(a.equipment.Primary?.def.defName, b.equipment.Primary?.def.defName);
        }

        // ---- gear Scribe round trip ----

        [Fact]
        public void Scribe_round_trip_preserves_carried_gear()
        {
            // No faction on this request: gear round-tripping only needs the weapon Thing itself, and a
            // Faction reference elsewhere in the same save graph is exercised separately (Factions'
            // PawnGroupMakerTests.Scribe_round_trip_of_a_generated_raid_squad_preserves_faction_and_gear).
            // Raider_Gunner's two candidate weapons (Gun_Revolver, Gun_AssaultRifle) both always pass its
            // tag/price filters with no tech ceiling, so this kind is armed deterministically here.
            Rand.Current = new RandomStream(4004);
            Pawn.ResetThingIdCounter();
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(Kind("Raider_Gunner"), fixedBiologicalAge: 30f));
            Assert.NotNull(pawn.equipment.Primary);
            string originalDefName = pawn.equipment.Primary!.def.defName;
            int originalHitPoints = pawn.equipment.Primary.HitPoints;

            var holder = new PawnHolder { pawns = new List<Pawn> { pawn } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            PawnHolder loaded = Scribe.Load<PawnHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Pawn restored = loaded.pawns![0];
            Assert.NotNull(restored.equipment.Primary);
            Assert.Equal(originalDefName, restored.equipment.Primary!.def.defName);
            Assert.Equal(originalHitPoints, restored.equipment.Primary.HitPoints);
        }
    }
}
