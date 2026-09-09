using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Tests.Pawns;
using SimWorld.Work;
using Xunit;

namespace SimWorld.Tests.Work
{
    public class WorkTests : ContentTestBase
    {
        public WorkTests(CoreContentFixture content) : base(content)
        {
        }

        private static TraitDef MakeTraitDef(string defName, WorkTags disabledWorkTags)
        {
            return new TraitDef
            {
                defName = defName,
                label = defName,
                degreeDatas = { new TraitDegreeData { degree = 0, label = defName } },
                disabledWorkTags = disabledWorkTags,
            };
        }

        private static int IndexOf(IReadOnlyList<WorkGiverDef> list, string defName) =>
            list.ToList().FindIndex(g => g.defName == defName);

        // ---- content ----

        [Fact]
        public void Core_work_content_loads_with_expected_counts()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.Equal(12, DefDatabase<SkillDef>.DefCount);
            Assert.Equal(20, DefDatabase<WorkTypeDef>.DefCount);
            // 28: the capture loop (system 12: Factions) added WardenAttemptRecruit and wired WardenFeed —
            // see src/SimWorld.Core/Data/Core/Defs/WorkGiverDefs/WorkGivers.xml. AI/** owns that content this
            // round; Work/** does not, so only this literal count moved.
            Assert.Equal(28, DefDatabase<WorkGiverDef>.DefCount);
        }

        [Fact]
        public void DefOfs_are_bound()
        {
            Assert.NotNull(SkillDefOf.Shooting);
            Assert.NotNull(SkillDefOf.Intellectual);
            Assert.Equal("Shooting", SkillDefOf.Shooting.defName);
            Assert.NotNull(WorkTypeDefOf.Firefighter);
            Assert.NotNull(WorkTypeDefOf.Research);
            Assert.Equal("Research", WorkTypeDefOf.Research.defName);
        }

        // ---- xp curve ----

        [Fact]
        public void Xp_curve_matches_RimWorld_tuning()
        {
            Assert.Equal(1000, SkillRecord.XpRequiredToLevelUpFrom(0));
            Assert.Equal(10000, SkillRecord.XpRequiredToLevelUpFrom(9));
            Assert.Equal(30000, SkillRecord.XpRequiredToLevelUpFrom(19));

            for (int level = 0; level < 19; level++)
            {
                Assert.True(SkillRecord.XpRequiredToLevelUpFrom(level) < SkillRecord.XpRequiredToLevelUpFrom(level + 1));
            }
        }

        // ---- passion / learn rate ----

        [Fact]
        public void Passion_factors_scale_indirect_learning()
        {
            // 500 raw XP stays well under level 0's 1000 XP requirement at every passion, so none of these
            // level up and mask the factor being asserted.
            Pawn none = NewHuman("None");
            SkillRecord noneRecord = none.skills.GetSkill(SkillDefOf.Crafting)!;
            noneRecord.passion = Passion.None;
            noneRecord.Learn(500f);
            Assert.Equal(500f * 0.35f, noneRecord.xpSinceLastLevel);

            Pawn minor = NewHuman("Minor");
            SkillRecord minorRecord = minor.skills.GetSkill(SkillDefOf.Crafting)!;
            minorRecord.passion = Passion.Minor;
            minorRecord.Learn(500f);
            Assert.Equal(500f, minorRecord.xpSinceLastLevel);

            Pawn major = NewHuman("Major");
            SkillRecord majorRecord = major.skills.GetSkill(SkillDefOf.Crafting)!;
            majorRecord.passion = Passion.Major;
            majorRecord.Learn(500f);
            Assert.Equal(750f, majorRecord.xpSinceLastLevel);
        }

        // ---- level up / down ----

        [Fact]
        public void Level_up_crosses_boundary_with_carry_over()
        {
            Pawn p = NewHuman();
            SkillRecord record = p.skills.GetSkill(SkillDefOf.Mining)!;
            record.Learn(1050f, ignoreLearnRate: true);
            Assert.Equal(1, record.levelInt);
            Assert.Equal(50f, record.xpSinceLastLevel);
        }

        [Fact]
        public void Level_down_on_negative_xp()
        {
            Pawn p = NewHuman();
            SkillRecord record = p.skills.GetSkill(SkillDefOf.Mining)!;
            record.Level = 5;
            record.Learn(-100f, direct: true, ignoreLearnRate: true);
            Assert.Equal(4, record.levelInt);
            Assert.Equal(-100f + SkillRecord.XpRequiredToLevelUpFrom(4), record.xpSinceLastLevel);
        }

        [Fact]
        public void Level_setter_clamps_to_valid_range()
        {
            Pawn p = NewHuman();
            SkillRecord record = p.skills.GetSkill(SkillDefOf.Mining)!;
            record.Level = 99;
            Assert.Equal(SkillRecord.MaxLevel, record.levelInt);
            record.Level = -5;
            Assert.Equal(SkillRecord.MinLevel, record.levelInt);
        }

        [Fact]
        public void Learn_clamps_xp_at_the_top_of_level_20()
        {
            Pawn p = NewHuman();
            SkillRecord record = p.skills.GetSkill(SkillDefOf.Mining)!;
            record.Level = 20;
            record.Learn(1_000_000f, ignoreLearnRate: true);
            Assert.Equal(20, record.levelInt);
            Assert.Equal(SkillRecord.XpRequiredToLevelUpFrom(20) - 1f, record.xpSinceLastLevel);
        }

        [Fact]
        public void Learn_clamps_xp_at_the_bottom_of_level_0()
        {
            Pawn p = NewHuman();
            SkillRecord record = p.skills.GetSkill(SkillDefOf.Mining)!;
            record.Level = 0;
            record.Learn(-1_000_000f, direct: true, ignoreLearnRate: true);
            Assert.Equal(0, record.levelInt);
            Assert.Equal(0f, record.xpSinceLastLevel);
        }

        // ---- saturation / daily reset ----

        [Fact]
        public void Saturation_throttles_learning_and_resets_at_midnight()
        {
            Pawn p = NewHuman();
            SkillRecord record = p.skills.GetSkill(SkillDefOf.Crafting)!;
            record.passion = Passion.Major;

            p.skills.SkillsTick(); // establishes the day baseline without decaying/leveling anything

            record.Learn(3000f);
            Assert.Equal(3000f * 1.5f, record.xpSinceMidnight);
            Assert.True(record.LearningSaturatedToday);

            record.Learn(1000f);
            Assert.Equal(3000f * 1.5f + 1000f * 1.5f * SkillRecord.SaturatedLearningPercentage, record.xpSinceMidnight);

            Find.TickManager.DebugSetTicksGame(GenDate.TicksPerDay);
            p.skills.SkillsTick();
            Assert.Equal(0f, record.xpSinceMidnight);
        }

        // ---- decay ----

        [Fact]
        public void Decay_only_applies_from_level_10_up()
        {
            Pawn p = NewHuman();
            SkillRecord record = p.skills.GetSkill(SkillDefOf.Mining)!;

            record.Level = 9;
            record.xpSinceLastLevel = 100f;
            record.Interval();
            Assert.Equal(100f, record.xpSinceLastLevel);
            Assert.Equal(9, record.levelInt);

            record.Level = 10;
            record.xpSinceLastLevel = 5000f;
            record.Interval();
            Assert.Equal(10, record.levelInt);
            Assert.InRange(record.xpSinceLastLevel, 4999.85f, 4999.95f);

            record.Level = 20;
            record.xpSinceLastLevel = 5000f;
            record.Interval();
            Assert.Equal(20, record.levelInt);
            Assert.InRange(record.xpSinceLastLevel, 4991.9f, 4992.1f);
        }

        // ---- disabling ----

        [Fact]
        public void Disabled_skill_ignores_learn_and_reports_level_zero()
        {
            Pawn p = NewHuman();
            TraitDef violentBan = MakeTraitDef("TestNoViolence", WorkTags.Violent);
            p.story.traits.GainTrait(new Trait(violentBan, 0));

            SkillRecord shooting = p.skills.GetSkill(SkillDefOf.Shooting)!;
            SkillRecord melee = p.skills.GetSkill(SkillDefOf.Melee)!;
            Assert.True(shooting.TotallyDisabled);
            Assert.True(melee.TotallyDisabled);

            shooting.levelInt = 10;
            Assert.Equal(0, shooting.Level);

            shooting.Learn(1000f);
            Assert.Equal(0f, shooting.xpSinceLastLevel);

            Assert.True(p.WorkTypeIsDisabled(WorkTypeDefOf.Hunting));
            Assert.False(p.WorkTypeIsDisabled(WorkTypeDefOf.Construction));
        }

        [Fact]
        public void SkillDef_IsDisabled_applies_the_disabling_work_tag_and_all_relevant_worktypes_rule()
        {
            Assert.True(SkillDefOf.Shooting.IsDisabled(WorkTags.Violent, Enumerable.Empty<WorkTypeDef>()));
            Assert.False(SkillDefOf.Shooting.IsDisabled(WorkTags.None, Enumerable.Empty<WorkTypeDef>()));

            Assert.False(SkillDefOf.Medicine.IsDisabled(WorkTags.None, Enumerable.Empty<WorkTypeDef>()));
            Assert.True(SkillDefOf.Medicine.IsDisabled(WorkTags.None, new[] { WorkTypeDefOf.Doctor }));

            Assert.False(SkillDefOf.Crafting.IsDisabled(WorkTags.None, new[] { WorkTypeDefOf.Smithing }));
            Assert.True(SkillDefOf.Crafting.IsDisabled(WorkTags.None, new[] { WorkTypeDefOf.Smithing, WorkTypeDefOf.Tailoring, WorkTypeDefOf.Crafting }));
        }

        [Fact]
        public void NeverDisabledBasedOnWorkTypes_short_circuits_the_worktype_rule()
        {
            var def = new SkillDef { defName = "TestAlwaysAvailable", neverDisabledBasedOnWorkTypes = true };
            Assert.False(def.IsDisabled(WorkTags.None, new[] { WorkTypeDefOf.Doctor, WorkTypeDefOf.Research }));
        }

        // ---- averages / passion helpers ----

        [Fact]
        public void MaxPassion_and_average_relevant_skills_reflect_the_pawns_records()
        {
            Pawn p = NewHuman();
            p.skills.GetSkill(SkillDefOf.Cooking)!.passion = Passion.Major;
            p.skills.GetSkill(SkillDefOf.Cooking)!.Level = 8;

            Assert.Equal(Passion.Major, p.skills.MaxPassionOfRelevantSkillsFor(WorkTypeDefOf.Cooking));
            Assert.Equal(8f, p.skills.AverageOfRelevantSkillsFor(WorkTypeDefOf.Cooking));
        }

        [Fact]
        public void Average_relevant_skills_defaults_to_three_with_no_relevant_skills()
        {
            Pawn p = NewHuman();
            Assert.Equal(3f, p.skills.AverageOfRelevantSkillsFor(WorkTypeDefOf.Hauling));
            Assert.Equal(Passion.None, p.skills.MaxPassionOfRelevantSkillsFor(WorkTypeDefOf.Hauling));
        }

        // ---- work settings ----

        [Fact]
        public void Work_settings_default_every_non_disabled_type_to_priority_three_for_humanlikes()
        {
            Pawn p = NewHuman();
            Assert.True(p.workSettings.EverWork);
            foreach (WorkTypeDef workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                Assert.Equal(3, p.workSettings.GetPriority(workType));
            }
        }

        [Fact]
        public void Animals_never_get_work_settings()
        {
            var dog = new Pawn(Husky, "Rex");
            Assert.False(dog.workSettings.EverWork);
            Assert.Equal(0, dog.workSettings.GetPriority(WorkTypeDefOf.Hauling));
        }

        [Fact]
        public void SetPriority_validates_the_0_to_4_range()
        {
            Pawn p = NewHuman();
            p.workSettings.useWorkPriorities = true;
            p.workSettings.SetPriority(WorkTypeDefOf.Hauling, 1);
            Assert.Equal(1, p.workSettings.GetPriority(WorkTypeDefOf.Hauling));

            Assert.Throws<ArgumentOutOfRangeException>(() => p.workSettings.SetPriority(WorkTypeDefOf.Hauling, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.workSettings.SetPriority(WorkTypeDefOf.Hauling, -1));
        }

        [Fact]
        public void SetPriority_refuses_a_disabled_worktype()
        {
            Pawn p = NewHuman();
            p.workSettings.useWorkPriorities = true;
            TraitDef noHauling = MakeTraitDef("TestNoHauling", WorkTags.Hauling);
            p.story.traits.GainTrait(new Trait(noHauling, 0));

            Assert.Equal(0, p.workSettings.GetPriority(WorkTypeDefOf.Hauling)); // Notify_DisabledWorkTypesChanged already zeroed it
            p.workSettings.SetPriority(WorkTypeDefOf.Hauling, 2);
            Assert.Equal(0, p.workSettings.GetPriority(WorkTypeDefOf.Hauling));
        }

        [Fact]
        public void UseWorkPriorities_off_collapses_stored_values_to_3_or_0()
        {
            Pawn p = NewHuman();
            Assert.False(p.workSettings.useWorkPriorities);
            p.workSettings.SetPriority(WorkTypeDefOf.Hauling, 1);
            Assert.Equal(3, p.workSettings.GetPriority(WorkTypeDefOf.Hauling));
            p.workSettings.SetPriority(WorkTypeDefOf.Cleaning, 0);
            Assert.Equal(0, p.workSettings.GetPriority(WorkTypeDefOf.Cleaning));
        }

        [Fact]
        public void Notify_TraitsChanged_disables_matching_skills_and_worktypes()
        {
            Pawn p = NewHuman();
            Assert.False(p.skills.GetSkill(SkillDefOf.Shooting)!.TotallyDisabled);
            Assert.Equal(3, p.workSettings.GetPriority(WorkTypeDefOf.Hunting));

            TraitDef violentBan = MakeTraitDef("TestNoViolence2", WorkTags.Violent);
            p.story.traits.GainTrait(new Trait(violentBan, 0));

            Assert.True(p.skills.GetSkill(SkillDefOf.Shooting)!.TotallyDisabled);
            Assert.Equal(0, p.workSettings.GetPriority(WorkTypeDefOf.Hunting));
        }

        [Fact]
        public void Pyromaniac_disables_firefighting_via_content()
        {
            TraitDef pyro = Trait("Pyromaniac");
            Assert.Equal(WorkTags.Firefighting, pyro.disabledWorkTags);

            Pawn p = NewHuman();
            p.story.traits.GainTrait(new Trait(pyro, 0));

            Assert.True(p.WorkTypeIsDisabled(WorkTypeDefOf.Firefighter));
            Assert.Equal(0, p.workSettings.GetPriority(WorkTypeDefOf.Firefighter));
            Assert.DoesNotContain(p.workSettings.WorkGiversInOrderEmergency, g => g.defName == "FightFires");
        }

        // ---- work giver ordering ----

        [Fact]
        public void WorkGivers_split_by_emergency_and_order_by_priority_then_natural_priority_then_priorityInType()
        {
            Pawn p = NewHuman();
            IReadOnlyList<WorkGiverDef> emergency = p.workSettings.WorkGiversInOrderEmergency;
            IReadOnlyList<WorkGiverDef> normal = p.workSettings.WorkGiversInOrderNormal;

            Assert.Equal("FightFires", emergency[0].defName);
            Assert.Contains(emergency, g => g.defName == "DoctorTendEmergency");
            Assert.All(emergency, g => Assert.True(g.emergency));
            Assert.All(normal, g => Assert.False(g.emergency));

            Assert.True(IndexOf(normal, "DoctorTend") < IndexOf(normal, "CookMeals"));
            Assert.True(IndexOf(normal, "Repair") < IndexOf(normal, "ConstructFinishFrames"));
            Assert.True(IndexOf(normal, "ConstructFinishFrames") < IndexOf(normal, "ConstructDeliverResourcesToFrames"));

            Assert.Equal(DefDatabase<WorkGiverDef>.DefCount, emergency.Count + normal.Count);
        }

        [Fact]
        public void WorkGiver_worker_is_lazily_created_and_checks_required_capacities()
        {
            WorkGiverDef doctorTend = DefDatabase<WorkGiverDef>.GetNamed("DoctorTend");
            WorkGiver worker = doctorTend.Worker;
            Assert.Same(worker, doctorTend.Worker);
            Assert.IsType<WorkGiver_Pending>(worker);
            Assert.Same(doctorTend, worker.def);

            Pawn p = NewHuman();
            Assert.False(worker.MissingRequiredCapacity(p));
            Assert.False(worker.ShouldSkip(p));
        }

        // ---- scribe round trip ----

        [Fact]
        public void Skills_and_work_priorities_round_trip_through_Scribe()
        {
            Pawn a = NewHuman("Ada");
            SkillRecord craft = a.skills.GetSkill(SkillDefOf.Crafting)!;
            craft.passion = Passion.Major;
            craft.Level = 8;
            craft.xpSinceLastLevel = 1234f;
            craft.xpSinceMidnight = 500f;

            a.workSettings.useWorkPriorities = true;
            a.workSettings.SetPriority(WorkTypeDefOf.Hauling, 1);
            a.workSettings.SetPriority(WorkTypeDefOf.Cleaning, 4);

            var holder = new PawnHolder { pawns = new List<Pawn> { a } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            PawnHolder loaded = Scribe.Load<PawnHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Pawn la = loaded.pawns![0];
            SkillRecord loadedCraft = la.skills.GetSkill(SkillDefOf.Crafting)!;
            Assert.Equal(Passion.Major, loadedCraft.passion);
            Assert.Equal(8, loadedCraft.levelInt);
            Assert.Equal(1234f, loadedCraft.xpSinceLastLevel);
            Assert.Equal(500f, loadedCraft.xpSinceMidnight);

            Assert.True(la.workSettings.useWorkPriorities);
            Assert.Equal(1, la.workSettings.GetPriority(WorkTypeDefOf.Hauling));
            Assert.Equal(4, la.workSettings.GetPriority(WorkTypeDefOf.Cleaning));

            // Loaded objects are fully wired: ticking must not throw.
            RunTicks(300, la);
        }

        // ---- work.policy: standing roles ----

        [Fact]
        public void Role_emphasizes_its_work_types_and_leaves_the_rest_at_default()
        {
            Pawn p = NewHuman();
            RoleDef farmer = DefDatabase<RoleDef>.GetNamed("Farmer");

            p.workSettings.SetRole(farmer);

            Assert.Same(farmer, p.workSettings.Role);
            Assert.Equal(Pawn_WorkSettings.EmphasizedPriority, p.workSettings.GetPriority(WorkTypeDefOf.Growing));
            Assert.Equal(Pawn_WorkSettings.EmphasizedPriority, p.workSettings.GetPriority(WorkTypeDefOf.PlantCutting));
            Assert.Equal(Pawn_WorkSettings.DefaultPriority, p.workSettings.GetPriority(WorkTypeDefOf.Mining));
            Assert.Equal(Pawn_WorkSettings.DefaultPriority, p.workSettings.GetPriority(WorkTypeDefOf.Hauling));
        }

        [Fact]
        public void Assigning_a_role_turns_on_detailed_priorities_so_the_emphasis_is_not_silently_collapsed()
        {
            Pawn p = NewHuman();
            Assert.False(p.workSettings.useWorkPriorities);

            p.workSettings.SetRole(DefDatabase<RoleDef>.GetNamed("Miner"));

            Assert.True(p.workSettings.useWorkPriorities);
            Assert.Equal(Pawn_WorkSettings.EmphasizedPriority, p.workSettings.GetPriority(WorkTypeDefOf.Mining));
        }

        [Fact]
        public void A_role_never_overwrites_a_manually_set_priority()
        {
            Pawn p = NewHuman();
            p.workSettings.useWorkPriorities = true;
            p.workSettings.SetPriority(WorkTypeDefOf.Growing, 4); // a person's own explicit choice for this pawn

            p.workSettings.SetRole(DefDatabase<RoleDef>.GetNamed("Farmer")); // Farmer emphasizes Growing

            Assert.Equal(4, p.workSettings.GetPriority(WorkTypeDefOf.Growing)); // untouched by the role
            Assert.Equal(Pawn_WorkSettings.EmphasizedPriority, p.workSettings.GetPriority(WorkTypeDefOf.PlantCutting)); // still applied
        }

        [Fact]
        public void A_role_never_enables_a_disabled_worktype()
        {
            Pawn p = NewHuman();
            TraitDef noPlantWork = MakeTraitDef("TestNoPlantWork", WorkTags.PlantWork);
            p.story.traits.GainTrait(new Trait(noPlantWork, 0));
            Assert.True(p.WorkTypeIsDisabled(WorkTypeDefOf.Growing));

            p.workSettings.SetRole(DefDatabase<RoleDef>.GetNamed("Farmer"));

            Assert.Equal(0, p.workSettings.GetPriority(WorkTypeDefOf.Growing));
        }

        [Fact]
        public void Clearing_a_role_reverts_every_non_manual_priority_it_touched_leaving_no_trace()
        {
            Pawn p = NewHuman();
            p.workSettings.SetRole(DefDatabase<RoleDef>.GetNamed("Scholar"));
            Assert.Equal(Pawn_WorkSettings.EmphasizedPriority, p.workSettings.GetPriority(WorkTypeDefOf.Research));

            p.workSettings.SetRole(null);

            Assert.Null(p.workSettings.Role);
            Assert.Equal(Pawn_WorkSettings.DefaultPriority, p.workSettings.GetPriority(WorkTypeDefOf.Research));
        }

        [Fact]
        public void Switching_roles_re_derives_the_grid_from_the_new_role_alone()
        {
            Pawn p = NewHuman();
            p.workSettings.SetRole(DefDatabase<RoleDef>.GetNamed("Farmer"));
            p.workSettings.SetRole(DefDatabase<RoleDef>.GetNamed("Miner"));

            // Farmer's own emphasis (Growing) must not linger once the pawn switches to Miner.
            Assert.Equal(Pawn_WorkSettings.DefaultPriority, p.workSettings.GetPriority(WorkTypeDefOf.Growing));
            Assert.Equal(Pawn_WorkSettings.EmphasizedPriority, p.workSettings.GetPriority(WorkTypeDefOf.Mining));
        }

        [Fact]
        public void A_role_moves_its_worktype_ahead_of_others_in_job_giver_order()
        {
            Pawn p = NewHuman();
            p.workSettings.SetRole(DefDatabase<RoleDef>.GetNamed("Miner"));

            IReadOnlyList<WorkGiverDef> normal = p.workSettings.WorkGiversInOrderNormal;
            int miningIndex = IndexOf(normal, "Mine");
            int haulingIndex = IndexOf(normal, "HaulGeneral");

            Assert.True(miningIndex >= 0 && haulingIndex >= 0);
            Assert.True(miningIndex < haulingIndex, "an emphasized work type's giver should be tried before a default-priority one");
        }

        [Fact]
        public void WorkPolicyUtility_applies_a_role_across_a_population_and_skips_non_workers()
        {
            Pawn worker = NewHuman("Worker");
            var dog = new Pawn(Husky, "PolicyDog");
            var dead = NewHuman("Dead");
            dead.health.Kill(null, null);

            RoleDef artisan = DefDatabase<RoleDef>.GetNamed("Artisan");
            int applied = WorkPolicyUtility.ApplyRoleToPopulation(new List<Pawn> { worker, dog, dead }, artisan);

            Assert.Equal(1, applied);
            Assert.Same(artisan, worker.workSettings.Role);
            Assert.False(dog.workSettings.EverWork);
            Assert.Null(dog.workSettings.Role); // animals never get a grid at all, so the role never reaches them
        }

        [Fact]
        public void Role_and_manual_overrides_round_trip_through_Scribe()
        {
            Pawn a = NewHuman("Rowan");
            a.workSettings.useWorkPriorities = true;
            a.workSettings.SetPriority(WorkTypeDefOf.Cleaning, 4); // manual, protected
            a.workSettings.SetRole(DefDatabase<RoleDef>.GetNamed("Miner"));

            var holder = new PawnHolder { pawns = new List<Pawn> { a } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            PawnHolder loaded = Scribe.Load<PawnHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Pawn la = loaded.pawns![0];
            Assert.Equal("Miner", la.workSettings.Role?.defName);
            Assert.Equal(Pawn_WorkSettings.EmphasizedPriority, la.workSettings.GetPriority(WorkTypeDefOf.Mining));
            Assert.Equal(4, la.workSettings.GetPriority(WorkTypeDefOf.Cleaning)); // manual override survived the round trip

            // The manual override still protects Cleaning against a fresh role application after loading.
            la.workSettings.SetRole(DefDatabase<RoleDef>.GetNamed("Artisan")); // does not emphasize Cleaning
            Assert.Equal(4, la.workSettings.GetPriority(WorkTypeDefOf.Cleaning));
        }
    }
}
