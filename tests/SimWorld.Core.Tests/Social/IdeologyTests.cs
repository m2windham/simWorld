using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Social.Ideology;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Thoughts;
using Xunit;

namespace SimWorld.Tests.Social
{
    /// <summary>Scribe holder for round-tripping an <see cref="Ideo"/> alongside the pawns its role tracker
    /// references, mirroring <c>SocialHolder</c>'s own pawns-plus-references shape.</summary>
    public class IdeoHolder : IExposable
    {
        public Ideo? ideo;
        public List<Pawn>? pawns;

        public void ExposeData()
        {
            Scribe_Deep.Look(ref ideo, "ideo");
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Deep);
        }
    }

    /// <summary>
    /// Memes/precepts feeding thoughts, rituals and roles (system 17: <c>social.ideology</c>,
    /// <c>docs/spec/simworld-spec.md</c> §8.4). <see cref="IdeoManager.Current"/> is not reset by
    /// <see cref="ContentTestBase"/> (see that class's own "Test isolation" doc — <c>Sim/**</c> is out of this
    /// pass's file ownership, so nothing there resets it for us) — every test here resets it in the
    /// constructor AND in <see cref="Dispose"/> so a non-null Ideo can never leak into an unrelated test
    /// sharing the same pooled xUnit thread.
    /// </summary>
    public class IdeologyTests : ContentTestBase, IDisposable
    {
        public IdeologyTests(CoreContentFixture content) : base(content)
        {
            IdeoManager.Reset();
        }

        public void Dispose()
        {
            IdeoManager.Reset();
        }

        private static IdeoDef Hearthway => DefDatabase<IdeoDef>.GetNamed("TheHearthway");
        private static IdeoDef ForgeCovenant => DefDatabase<IdeoDef>.GetNamed("TheForgeCovenant");
        private static PreceptDef Precept(string defName) => DefDatabase<PreceptDef>.GetNamed(defName);
        private static ThoughtDef Thought(string defName) => DefDatabase<ThoughtDef>.GetNamed(defName);
        private static IdeoRoleDef Role(string defName) => DefDatabase<IdeoRoleDef>.GetNamed(defName);
        private static RitualDef Ritual(string defName) => DefDatabase<RitualDef>.GetNamed(defName);

        private static bool HasMoodThought(Pawn pawn, ThoughtDef def)
        {
            pawn.needs.mood!.thoughts.situational.Recalculate();
            var thoughts = new List<Thought>();
            pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);
            return thoughts.Exists(t => t.def == def);
        }

        // ---- content ----

        [Fact]
        public void Ideology_content_loads_with_no_errors()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.True(DefDatabase<MemeDef>.DefCount >= 8);
            Assert.True(DefDatabase<PreceptDef>.DefCount >= 7);
            Assert.True(DefDatabase<IdeoRoleDef>.DefCount >= 2);
            Assert.True(DefDatabase<RitualDef>.DefCount >= 2);
            Assert.Equal(2, DefDatabase<IdeoDef>.DefCount);
        }

        [Fact]
        public void Each_shipped_ideoligion_carries_exactly_one_structure_meme()
        {
            Assert.Empty(Hearthway.ConfigErrors());
            Assert.Empty(ForgeCovenant.ConfigErrors());
        }

        [Fact]
        public void AllPrecepts_unions_auto_precepts_and_slot_fills()
        {
            var precepts = new List<PreceptDef>(Hearthway.AllPrecepts);
            Assert.Contains(Precept("Precept_HearthwayBelonging"), precepts); // auto, from Structure_Communal
            Assert.Contains(Precept("Precept_Nudity_Preferred"), precepts); // slot fill
            Assert.Contains(Precept("Precept_Kindness_Celebrated"), precepts);
            Assert.Contains(Precept("Precept_EldersLead"), precepts);
            Assert.Equal(4, precepts.Count);
        }

        // ---- IdeoDef structural validation ----

        [Fact]
        public void An_ideo_with_no_structure_meme_is_a_config_error()
        {
            var normalOnly = new MemeDef { defName = "TestNormalOnly", category = MemeCategory.Normal };
            var ideo = new IdeoDef { defName = "TestNoStructure", memes = new List<MemeDef> { normalOnly } };

            Assert.Contains(ideo.ConfigErrors(), e => e.Contains("exactly one Structure meme"));
        }

        [Fact]
        public void An_ideo_with_two_structure_memes_is_a_config_error()
        {
            var a = new MemeDef { defName = "TestStructureA", category = MemeCategory.Structure };
            var b = new MemeDef { defName = "TestStructureB", category = MemeCategory.Structure };
            var ideo = new IdeoDef { defName = "TestTwoStructure", memes = new List<MemeDef> { a, b } };

            Assert.Contains(ideo.ConfigErrors(), e => e.Contains("exactly one Structure meme"));
        }

        [Fact]
        public void An_unfilled_precept_slot_is_a_config_error()
        {
            var structure = new MemeDef { defName = "TestStructureUnfilled", category = MemeCategory.Structure };
            var opensSlot = new MemeDef
            {
                defName = "TestMemeOpensSlot",
                preceptSlots = new List<string> { "TestTopic" },
            };
            var ideo = new IdeoDef
            {
                defName = "TestUnfilledSlot",
                memes = new List<MemeDef> { structure, opensSlot },
                // precepts deliberately left empty: TestTopic's slot is never filled.
            };

            Assert.Contains(ideo.ConfigErrors(), e => e.Contains("TestTopic") && e.Contains("not filled"));
        }

        [Fact]
        public void An_orphan_precept_claimed_by_no_slot_is_a_config_error()
        {
            var structure = new MemeDef { defName = "TestStructureOrphan", category = MemeCategory.Structure };
            var orphan = new PreceptDef { defName = "TestOrphanPrecept", issue = "NoOneOpenedThis" };
            var ideo = new IdeoDef
            {
                defName = "TestOrphanIdeo",
                memes = new List<MemeDef> { structure }, // opens no slots at all
                precepts = new List<PreceptDef> { orphan },
            };

            Assert.Contains(ideo.ConfigErrors(), e => e.Contains("TestOrphanPrecept") && e.Contains("not claimed"));
        }

        [Fact]
        public void A_precepts_moodThought_must_be_situational_not_a_memory()
        {
            var memoryThought = new ThoughtDef { defName = "TestMemoryMoodThought", durationDays = 1f };
            var precept = new PreceptDef { defName = "TestBadPrecept", issue = "Test", moodThought = memoryThought };

            Assert.Contains(precept.ConfigErrors(), e => e.Contains("situational"));
        }

        // ---- the core loop: precepts feeding thoughts ----

        [Fact]
        public void No_ambient_Ideo_means_no_precept_thoughts_at_all()
        {
            Pawn pawn = NewHuman();
            Assert.Null(IdeoManager.Current);
            Assert.False(HasMoodThought(pawn, Thought("HearthwayBelonging")));
            Assert.False(HasMoodThought(pawn, Thought("BareSkinComfortable")));
        }

        [Fact]
        public void An_unclothed_citizen_reads_opposite_judgments_under_the_two_ideoligions()
        {
            // Same pawn, same (unclothed) state — RimWorld's whole point of belief being civilization-specific,
            // proven directly: the Hearthway approves nudity, the Forge Covenant condemns the same exposure.
            Pawn pawn = NewHuman();
            Assert.Empty(pawn.apparel.WornApparel);

            IdeoManager.Current = new Ideo(Hearthway);
            Assert.True(HasMoodThought(pawn, Thought("BareSkinComfortable")));
            Assert.False(HasMoodThought(pawn, Thought("ImmodestExposure")));
            Assert.True(HasMoodThought(pawn, Thought("HearthwayBelonging"))); // always-on belonging precept

            IdeoManager.Current = new Ideo(ForgeCovenant);
            Assert.False(HasMoodThought(pawn, Thought("BareSkinComfortable")));
            Assert.True(HasMoodThought(pawn, Thought("ImmodestExposure")));
            Assert.True(HasMoodThought(pawn, Thought("ForgeCovenantBelonging")));
        }

        [Fact]
        public void Precept_thoughts_carry_the_mood_sign_their_ideoligion_intends()
        {
            Pawn pawn = NewHuman();

            IdeoManager.Current = new Ideo(Hearthway);
            pawn.needs.mood!.thoughts.situational.Recalculate();
            float hearthwayMood = pawn.needs.mood.thoughts.TotalMoodOffset();

            IdeoManager.Current = new Ideo(ForgeCovenant);
            pawn.needs.mood.thoughts.situational.Recalculate();
            float forgeCovenantMood = pawn.needs.mood.thoughts.TotalMoodOffset();

            // Bare skin is a net mood positive under the Hearthway and a net mood negative under the Forge
            // Covenant — not just "different thoughts fire" but "the fired thoughts actually pull mood apart".
            Assert.True(hearthwayMood > 0f, $"expected positive mood under the Hearthway, got {hearthwayMood}");
            Assert.True(forgeCovenantMood < 0f, $"expected negative mood under the Forge Covenant, got {forgeCovenantMood}");
        }

        [Fact]
        public void Dressing_a_bare_citizen_removes_the_nudity_precepts_thought_immediately()
        {
            Pawn pawn = NewHuman();
            IdeoManager.Current = new Ideo(Hearthway);
            Assert.True(HasMoodThought(pawn, Thought("BareSkinComfortable")));

            var shirt = (ThingWithComps)ThingMaker.MakeThing(
                DefDatabase<ThingDef>.GetNamed("Apparel_Shirt"));
            pawn.apparel.Wear(shirt);

            Assert.False(HasMoodThought(pawn, Thought("BareSkinComfortable")));
            Assert.True(HasMoodThought(pawn, Thought("HearthwayBelonging"))); // unaffected — not apparel-driven
        }

        // ---- trait-driven precepts ----

        [Fact]
        public void Trait_driven_precepts_apply_only_to_pawns_holding_the_named_trait()
        {
            Pawn kind = NewHuman("Kind");
            kind.story.traits.GainTrait(new Trait(Trait("Kind")));
            Pawn plain = NewHuman("Plain");

            IdeoManager.Current = new Ideo(Hearthway);

            Assert.True(HasMoodThought(kind, Thought("KindnessHonored")));
            Assert.False(HasMoodThought(plain, Thought("KindnessHonored")));
        }

        [Fact]
        public void The_two_ideoligions_celebrate_opposing_traits()
        {
            Pawn bloodlust = NewHuman("Bloodlust");
            bloodlust.story.traits.GainTrait(new Trait(Trait("Bloodlust")));

            IdeoManager.Current = new Ideo(Hearthway);
            Assert.False(HasMoodThought(bloodlust, Thought("BloodlustHonored"))); // Hearthway has no such precept

            IdeoManager.Current = new Ideo(ForgeCovenant);
            Assert.True(HasMoodThought(bloodlust, Thought("BloodlustHonored")));
        }

        // ---- roles ----

        [Fact]
        public void TryAssignRole_refuses_a_role_no_precept_in_the_ideo_grants()
        {
            var ungranted = new IdeoRoleDef { defName = "TestUngrantedRole" };
            var ideo = new Ideo(Hearthway);
            Pawn pawn = NewHuman();

            Assert.False(ideo.TryAssignRole(pawn, ungranted));
            Assert.Null(ideo.Roles.RoleOf(pawn));
        }

        [Fact]
        public void TryAssignRole_succeeds_for_a_role_a_precept_grants_and_enforces_maxHolders()
        {
            IdeoRoleDef elder = Role("Elder"); // maxHolders = 1
            var ideo = new Ideo(Hearthway);
            Pawn first = NewHuman("First");
            Pawn second = NewHuman("Second");

            Assert.True(ideo.TryAssignRole(first, elder));
            Assert.Equal(elder, ideo.Roles.RoleOf(first));

            // The cap refuses a second, different holder while the first still holds it.
            Assert.False(ideo.TryAssignRole(second, elder));
            Assert.Null(ideo.Roles.RoleOf(second));

            // Freeing the seat lets the refused pawn in.
            Assert.True(ideo.Roles.Unassign(first));
            Assert.True(ideo.TryAssignRole(second, elder));
            Assert.Equal(elder, ideo.Roles.RoleOf(second));
        }

        [Fact]
        public void A_role_with_maxHolders_2_admits_exactly_that_many()
        {
            IdeoRoleDef forgemaster = Role("Forgemaster"); // maxHolders = 2
            var ideo = new Ideo(ForgeCovenant);
            Pawn a = NewHuman("A");
            Pawn b = NewHuman("B");
            Pawn c = NewHuman("C");

            Assert.True(ideo.TryAssignRole(a, forgemaster));
            Assert.True(ideo.TryAssignRole(b, forgemaster));
            Assert.False(ideo.TryAssignRole(c, forgemaster));
            Assert.Equal(2, ideo.Roles.CountHolding(forgemaster));
        }

        [Fact]
        public void Only_the_role_holder_gets_the_role_holders_thought()
        {
            var ideo = new Ideo(Hearthway);
            IdeoManager.Current = ideo;
            Pawn elder = NewHuman("Elder");
            Pawn commoner = NewHuman("Commoner");
            ideo.TryAssignRole(elder, Role("Elder"));

            Assert.True(HasMoodThought(elder, Thought("GuidingTheHearthway")));
            Assert.False(HasMoodThought(commoner, Thought("GuidingTheHearthway")));

            ideo.Roles.Unassign(elder);
            Assert.False(HasMoodThought(elder, Thought("GuidingTheHearthway")));
        }

        // ---- rituals: quality resolution ----

        private static Pawn NeutralPawn(string name)
        {
            Pawn p = NewHuman(name);
            p.needs.mood!.CurLevelPercentage = 0.5f; // isolates the participant-count/officiant terms from mood's own.
            return p;
        }

        [Fact]
        public void More_participants_never_score_a_lower_quality()
        {
            RitualDef ritual = Ritual("HearthGathering");
            var few = new List<Pawn> { NeutralPawn("A") };
            var many = new List<Pawn> { NeutralPawn("B"), NeutralPawn("C"), NeutralPawn("D"), NeutralPawn("E"), NeutralPawn("F"), NeutralPawn("G") };

            float qualityFew = RitualUtility.ComputeQuality(ritual, few, null);
            float qualityMany = RitualUtility.ComputeQuality(ritual, many, null);

            Assert.True(qualityMany > qualityFew, $"expected more participants to score higher: {qualityMany} vs {qualityFew}");
        }

        [Fact]
        public void An_officiant_present_never_scores_a_lower_quality_than_the_same_ritual_without_one()
        {
            RitualDef ritual = Ritual("HearthGathering");
            var ideo = new Ideo(Hearthway);
            Pawn elder = NeutralPawn("Elder");
            Pawn commoner = NeutralPawn("Commoner");
            ideo.TryAssignRole(elder, Role("Elder"));

            var withoutOfficiant = new List<Pawn> { commoner };
            var withOfficiant = new List<Pawn> { commoner, elder };

            float withoutQuality = RitualUtility.ComputeQuality(ritual, withoutOfficiant, ideo);
            float withQuality = RitualUtility.ComputeQuality(ritual, withOfficiant, ideo);

            // withOfficiant also has one more participant, so isolate the officiant term by comparing against
            // what the extra body alone would have been worth (both non-officiant additions score 0 here).
            var twoCommoners = new List<Pawn> { commoner, NeutralPawn("Commoner2") };
            float twoCommonersQuality = RitualUtility.ComputeQuality(ritual, twoCommoners, ideo);

            Assert.True(withQuality > twoCommonersQuality,
                $"an officiant must be worth strictly more than an ordinary extra participant: {withQuality} vs {twoCommonersQuality}");
            Assert.True(withQuality > withoutQuality);
        }

        [Fact]
        public void A_happier_crowd_scores_a_higher_quality()
        {
            RitualDef ritual = Ritual("HearthGathering");

            Pawn sad = NewHuman("Sad");
            sad.needs.mood!.CurLevelPercentage = 0.1f;
            Pawn joyful = NewHuman("Joyful");
            joyful.needs.mood!.CurLevelPercentage = 0.95f;

            float sadQuality = RitualUtility.ComputeQuality(ritual, new List<Pawn> { sad }, null);
            float joyfulQuality = RitualUtility.ComputeQuality(ritual, new List<Pawn> { joyful }, null);

            Assert.True(joyfulQuality > sadQuality, $"expected a happier crowd to score higher: {joyfulQuality} vs {sadQuality}");
        }

        [Fact]
        public void ComputeQuality_always_stays_in_zero_to_one()
        {
            RitualDef ritual = Ritual("HearthGathering");
            var ideo = new Ideo(Hearthway);
            var crowd = new List<Pawn>();
            for (int i = 0; i < 20; i++)
            {
                Pawn p = NeutralPawn("P" + i);
                p.needs.mood!.CurLevelPercentage = 1f;
                crowd.Add(p);
            }
            ideo.TryAssignRole(crowd[0], Role("Elder"));

            float quality = RitualUtility.ComputeQuality(ritual, crowd, ideo);
            Assert.InRange(quality, 0f, 1f);

            float qualityEmpty = RitualUtility.ComputeQuality(ritual, new List<Pawn>(), null);
            Assert.InRange(qualityEmpty, 0f, 1f);
        }

        // ---- rituals: performing one ----

        [Fact]
        public void PerformRitual_grants_the_attendee_memory_to_every_living_participant()
        {
            RitualDef ritual = Ritual("HearthGathering");
            Pawn alive = NewHuman("Alive");
            Pawn dead = NewHuman("Dead");
            dead.health.Kill(null, null);

            RitualUtility.PerformRitual(ritual, new List<Pawn> { alive, dead }, null);

            ThoughtDef memoryDef = Thought("AttendedHearthGathering");
            Assert.Single(alive.needs.mood!.thoughts.memories.Memories, m => m.def == memoryDef);
            // A dead pawn must never be handed a new memory — nothing throws either.
            Assert.DoesNotContain(dead.needs.mood!.thoughts.memories.Memories, m => m.def == memoryDef);
        }

        [Fact]
        public void A_richer_ritual_lands_its_attendees_on_a_higher_memory_stage_than_a_thin_one()
        {
            RitualDef ritual = Ritual("HearthGathering");
            var ideo = new Ideo(Hearthway);

            // Thin: one participant, no officiant, miserable mood.
            Pawn thinAttendee = NewHuman("ThinAttendee");
            thinAttendee.needs.mood!.CurLevelPercentage = 0f;
            RitualOutcome thin = RitualUtility.PerformRitual(ritual, new List<Pawn> { thinAttendee }, ideo);

            // Rich: many participants, the officiant present, ecstatic mood — the pre-jitter gap between this
            // and the thin scenario is designed to swamp RitualTuning.QualityJitter so the ordering can never
            // flip on the roll.
            var richCrowd = new List<Pawn>();
            for (int i = 0; i < 8; i++)
            {
                Pawn p = NewHuman("Rich" + i);
                p.needs.mood!.CurLevelPercentage = 1f;
                richCrowd.Add(p);
            }
            ideo.TryAssignRole(richCrowd[0], Role("Elder"));
            RitualOutcome rich = RitualUtility.PerformRitual(ritual, richCrowd, ideo);

            Assert.True(rich.Quality > thin.Quality, $"{rich.Quality} vs {thin.Quality}");
            Assert.True(rich.StageIndex > thin.StageIndex, $"stage {rich.StageIndex} vs {thin.StageIndex}");

            ThoughtDef memoryDef = Thought("AttendedHearthGathering");
            var richMemory = richCrowd[0].needs.mood!.thoughts.memories.Memories[0];
            Assert.Equal(memoryDef, richMemory.def);
            Assert.Equal(rich.StageIndex, richMemory.CurStageIndex);
        }

        [Fact]
        public void PerformRitual_with_no_attendeeMemory_still_reports_an_outcome()
        {
            var bare = new RitualDef { defName = "TestBareRitual", baseQuality = 0.4f };
            Pawn pawn = NewHuman();

            RitualOutcome outcome = RitualUtility.PerformRitual(bare, new List<Pawn> { pawn }, null);

            Assert.Equal(-1, outcome.StageIndex);
            Assert.InRange(outcome.Quality, 0f, 1f);
            Assert.Empty(pawn.needs.mood!.thoughts.memories.Memories);
        }

        // ---- Scribe round trip ----

        [Fact]
        public void Ideo_and_its_role_assignments_round_trip_through_Scribe()
        {
            var ideo = new Ideo(Hearthway);
            Pawn elder = NewHuman("Elder");
            Assert.True(ideo.TryAssignRole(elder, Role("Elder")));

            var holder = new IdeoHolder { ideo = ideo, pawns = new List<Pawn> { elder } };
            string xml = Scribe.SaveToString(holder, "holder");
            IdeoHolder loaded = Scribe.Load<IdeoHolder>(xml, "holder", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.NotNull(loaded.ideo);
            Assert.Equal(Hearthway, loaded.ideo!.def);
            Assert.Equal(Hearthway.LabelCap, loaded.ideo.name);

            Pawn loadedElder = Assert.Single(loaded.pawns!);
            Assert.Equal(Role("Elder"), loaded.ideo.Roles.RoleOf(loadedElder));
        }

        [Fact]
        public void An_ideo_with_no_role_holders_round_trips_to_an_empty_tracker()
        {
            var ideo = new Ideo(ForgeCovenant);
            var holder = new IdeoHolder { ideo = ideo, pawns = new List<Pawn>() };

            string xml = Scribe.SaveToString(holder, "holder");
            IdeoHolder loaded = Scribe.Load<IdeoHolder>(xml, "holder", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(ForgeCovenant, loaded.ideo!.def);
            Assert.Null(loaded.ideo.Roles.RoleOf(NewHuman("Nobody"))); // never assigned; never throws either.
        }
    }
}
