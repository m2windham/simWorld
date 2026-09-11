using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Quests;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;

using Xunit;

using CoreMap = global::SimWorld.Map.Map;
using ResearchManager = global::SimWorld.Research.ResearchManager;
using ResearchProjectDef = global::SimWorld.Research.ResearchProjectDef;
using Storyteller = global::SimWorld.Director.Storyteller;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// Choosing a difficulty has to do something.
    ///
    /// <para/>Six of <see cref="DifficultyDef"/>'s knobs were set by the shipped presets and read by no line of
    /// <c>src/</c>: a player picked Peaceful and was raided anyway, picked Extreme and harvested the same
    /// potatoes. Each test here takes one scenario, runs it twice under two difficulties with everything else
    /// held equal, and asserts the outcome moved in the direction the field's name promises.
    ///
    /// <para/><b>Ordering, never a literal.</b> The preset values (0.7, 1.15, −12…) are tuning and are declared
    /// as approximations in <c>Difficulties.xml</c> itself. Asserting "Extreme yields less than Medium" stays
    /// true when someone retunes them; asserting "Extreme yields 14" turns a tuning change into a red suite.
    /// </summary>
    public class DifficultyWiringTests : ContentTestBase
    {
        public DifficultyWiringTests(CoreContentFixture content) : base(content)
        {
            // Find's storyteller/quest services are thread-static and outlive a test unless replaced; every
            // test here reads difficulty through Find.Storyteller, so each starts from a known one.
            Find.Storyteller = new Storyteller();
            Find.QuestManager = new QuestManager();
            QuestGen.ResetIdCounterForTests();
        }

        // ---- helpers ----

        private static DifficultyDef Difficulty(string defName) => DefDatabase<DifficultyDef>.GetNamed(defName);

        private static IncidentDef Incident(string defName) => DefDatabase<IncidentDef>.GetNamed(defName);

        private static ThingDef Def(string defName) => DefDatabase<ThingDef>.GetNamed(defName);

        /// <summary>Points the game on <paramref name="difficultyName"/> and returns a civilization to aim incidents at.</summary>
        private static CivilizationTarget PlayOn(string difficultyName, int pawnCount = 3)
        {
            var storyteller = new Storyteller(StorytellerDefOf.Cassandra_Classic, Difficulty(difficultyName));
            Find.Storyteller = storyteller;
            var target = new CivilizationTarget(storyteller) { PlayerWealthForStoryteller = 0f };
            for (int i = 0; i < pawnCount; i++) target.pawns.Add(NewHuman("Pawn" + i));
            return target;
        }

        private static Faction PlayerFaction() =>
            new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Ours", "F_Ours");

        // -----------------------------------------------------------------------------------------
        // allowBigThreats
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The bug this flag was carried for, stated as a test: Peaceful's <c>threatScale</c> of 0 does not
        /// stop a raid, because <see cref="StorytellerUtility.DefaultThreatPointsNow"/> clamps its result up
        /// to <see cref="StorytellerUtility.MinThreatPoints"/> — which is exactly the floor a big threat asks
        /// for. So a peaceful civilization was raided every cycle by the smallest possible war band. The
        /// switch is the only thing that can say no.
        /// </summary>
        [Fact]
        public void A_peaceful_game_fires_no_big_threat_even_though_its_threat_points_clear_the_floor()
        {
            CivilizationTarget peaceful = PlayOn("Peaceful");
            IncidentDef bigThreat = Incident("ManhunterPack");
            IncidentParms peacefulParms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, peaceful);

            // The premise: points survive a zero threat scale, and clear the incident's own floor.
            Assert.Equal(StorytellerUtility.MinThreatPoints, peacefulParms.points);
            Assert.True(peacefulParms.points >= bigThreat.minThreatPoints,
                "Peaceful must still clear the floor, or this test would be passing for the wrong reason.");

            Assert.False(bigThreat.Worker.CanFireNow(peacefulParms));

            CivilizationTarget medium = PlayOn("Medium");
            IncidentParms mediumParms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, medium);
            Assert.True(bigThreat.Worker.CanFireNow(mediumParms));
        }

        /// <summary>The switch is category-scoped, not a blanket mute: a peaceful world still has weather,
        /// wanderers and traders.</summary>
        [Fact]
        public void A_peaceful_game_still_fires_everything_that_is_not_a_big_threat()
        {
            CivilizationTarget peaceful = PlayOn("Peaceful");
            IncidentDef wanderer = Incident("WandererJoin");
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(wanderer.category, peaceful);

            Assert.True(wanderer.Worker.CanFireNow(parms));
        }

        // -----------------------------------------------------------------------------------------
        // allowIntroThreats
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The scripted cold open (<see cref="StorytellerComp_ClassicIntro"/>) is the only "intro threat" this
        /// core has, and the flag turns it off independently of <c>allowBigThreats</c> — the difficulties are
        /// built here rather than taken from content precisely because the two shipped presets that disallow
        /// one disallow both, and a test that cannot tell the two gates apart proves nothing about either.
        /// </summary>
        [Fact]
        public void The_scripted_first_threat_is_scheduled_only_when_the_difficulty_allows_intro_threats()
        {
            Assert.Empty(IntroIncidentsOn(new DifficultyDef { defName = "Test_NoIntro", allowIntroThreats = false }));
            Assert.Single(IntroIncidentsOn(new DifficultyDef { defName = "Test_Intro", allowIntroThreats = true }));
        }

        private static FiringIncident[] IntroIncidentsOn(DifficultyDef difficulty)
        {
            var storyteller = new Storyteller(StorytellerDefOf.Cassandra_Classic, difficulty);
            Find.Storyteller = storyteller;
            var target = new CivilizationTarget(storyteller) { PlayerWealthForStoryteller = 0f };
            for (int i = 0; i < 3; i++) target.pawns.Add(NewHuman("Pawn" + i));

            // Past the comp's own day gate, so the only thing left that can refuse is the difficulty.
            Find.TickManager.DebugSetTicksGame(GenDate.TicksPerDay * 5);

            StorytellerComp_ClassicIntro intro = storyteller.Comps.OfType<StorytellerComp_ClassicIntro>().Single();
            return intro.MakeIntervalIncidents(target).ToArray();
        }

        // -----------------------------------------------------------------------------------------
        // colonistMoodOffset
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void A_citizens_mood_target_rises_on_a_kind_difficulty_and_falls_on_a_cruel_one()
        {
            Assert.True(MoodTargetOn("Easy") > MoodTargetOn("Medium"),
                "Easy carries a positive colonistMoodOffset, so its people should sit happier.");
            Assert.True(MoodTargetOn("Extreme") < MoodTargetOn("Medium"),
                "Extreme carries a negative colonistMoodOffset, so its people should sit unhappier.");
            Assert.True(MoodTargetOn("Extreme") < MoodTargetOn("Rough"),
                "Extreme's offset is the harsher of the two, and the ordering should follow.");
        }

        /// <summary>Medium sets no offset at all, so it must land exactly on the neutral half-full bar the
        /// mood need has always started at — the guard that says this wiring is an offset and not a rescale.</summary>
        [Fact]
        public void A_difficulty_with_no_offset_leaves_the_mood_target_exactly_where_it_was()
        {
            Assert.Equal(0.5f, MoodTargetOn("Medium"), 4);
        }

        /// <summary>RimWorld's own restriction: the player's difficulty setting cheers up the player's people,
        /// not everyone standing on the map. A raider gets nothing.</summary>
        [Fact]
        public void The_difficulty_mood_offset_reaches_the_civilizations_own_people_only()
        {
            Find.Storyteller = new Storyteller(StorytellerDefOf.Cassandra_Classic, Difficulty("Extreme"));

            Pawn outsider = NewHuman("Raider");
            outsider.faction = new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Them", "F_Them");
            Pawn ours = NewHuman("Citizen");
            ours.faction = PlayerFaction();

            Assert.Equal(0.5f, outsider.needs.mood!.CurInstantLevel, 4);
            Assert.True(ours.needs.mood!.CurInstantLevel < outsider.needs.mood!.CurInstantLevel);
        }

        private static float MoodTargetOn(string difficultyName)
        {
            Find.Storyteller = new Storyteller(StorytellerDefOf.Cassandra_Classic, Difficulty(difficultyName));
            Pawn pawn = NewHuman("Citizen");
            pawn.faction = PlayerFaction();
            return pawn.needs.mood!.CurInstantLevel;
        }

        // -----------------------------------------------------------------------------------------
        // cropYieldFactor
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void The_same_field_harvested_on_a_harder_difficulty_brings_in_less_food()
        {
            int medium = HarvestOn("Medium");
            int rough = HarvestOn("Rough");
            int extreme = HarvestOn("Extreme");

            Assert.True(rough < medium, "Rough's cropYieldFactor is below 1, so its harvest should be smaller.");
            Assert.True(extreme < rough, "Extreme's factor is the lower of the two, and the ordering should follow.");
            Assert.True(extreme > 0, "A worse difficulty should thin the harvest, not abolish it.");
        }

        /// <summary>Harvests one fully-grown potato plant under <paramref name="difficultyName"/> and returns
        /// the stack it dropped. Same plant, same growth, same map in every run.</summary>
        private static int HarvestOn(string difficultyName)
        {
            Find.Storyteller = new Storyteller(StorytellerDefOf.Cassandra_Classic, Difficulty(difficultyName));

            var map = new CoreMap(5, 5, global::SimWorld.Map.TerrainDefOf.Soil);
            var plant = (Plant)ThingMaker.MakeThing(Def("Plant_Potato"));
            GenSpawn.Spawn(plant, new global::SimWorld.Map.IntVec3(2, 0, 2), map);
            plant.Growth = 1f;

            plant.Harvest(null);

            return map.listerThings.ThingsOfDef(Def("RawPotatoes")).Sum(t => t.stackCount);
        }

        // -----------------------------------------------------------------------------------------
        // researchSpeedFactor
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The knob the wiring audit could not see: no preset set it and no code read it, so it fell between
        /// the audit's two checks ("content sets it, does code read it?" and the reverse) and was reported by
        /// neither. Both halves exist now.
        /// </summary>
        [Fact]
        public void The_same_hours_at_the_bench_buy_less_research_on_a_harder_difficulty()
        {
            float easy = ResearchedOn("Easy");
            float medium = ResearchedOn("Medium");
            float extreme = ResearchedOn("Extreme");

            Assert.True(easy > medium, "Easy's researchSpeedFactor is above 1, so the same work should go further.");
            Assert.True(extreme < medium, "Extreme's is below 1, so the same work should go less far.");
        }

        private static float ResearchedOn(string difficultyName)
        {
            Find.Storyteller = new Storyteller(StorytellerDefOf.Cassandra_Classic, Difficulty(difficultyName));
            var manager = new ResearchManager();
            Find.ResearchManager = manager;

            ResearchProjectDef project = global::SimWorld.Research.ResearchProjectDefOf.Fire;
            manager.CurrentProj = project;
            manager.ResearchPerformed(10f, null);

            Assert.False(manager.IsFinished(project), "The sample of work must not finish the project, or every difficulty ties.");
            return manager.GetProgress(project);
        }

        // -----------------------------------------------------------------------------------------
        // questRewardValueFactor
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// A reward's *value* is decided when the quest is generated, and that is real today whether or not
        /// anything ever pays it out: <see cref="Quest.RewardsGiven"/> and <c>Quest.RewardsSummary</c> read it
        /// back. (Paying it out is <c>IQuestRewardSink</c>'s job and nothing implements one yet.)
        /// </summary>
        [Fact]
        public void A_quest_generated_on_a_harder_difficulty_is_worth_more_silver()
        {
            float medium = SilverOfferedOn("Medium");
            float extreme = SilverOfferedOn("Extreme");

            Assert.True(medium > 0f, "The sample quest must actually offer silver, or this proves nothing.");
            Assert.True(extreme > medium, "Extreme's questRewardValueFactor is above 1, so its quests should pay more.");
        }

        /// <summary>Goodwill is diplomatic standing rather than market value, and the factor deliberately does
        /// not touch it — see <see cref="QuestNode_GiveReward"/>.</summary>
        [Fact]
        public void The_reward_factor_scales_silver_and_leaves_goodwill_alone()
        {
            Assert.Equal(GoodwillOfferedOn("Medium"), GoodwillOfferedOn("Extreme"), 4);
        }

        private static float SilverOfferedOn(string difficultyName) => RewardOfferedOn(difficultyName, "silver");

        private static float GoodwillOfferedOn(string difficultyName) => RewardOfferedOn(difficultyName, "goodwill");

        private static float RewardOfferedOn(string difficultyName, string kind)
        {
            Find.Storyteller = new Storyteller(StorytellerDefOf.Cassandra_Classic, Difficulty(difficultyName));

            Quest quest = QuestGen.Generate(QuestScriptDefOf.Quest_LostCaravan, new Slate());
            return quest.PartsListForReading
                .OfType<QuestPart_Reward>()
                .SelectMany(p => p.rewards)
                .Where(r => r.kind == kind)
                .Sum(r => r.amount);
        }
    }
}
