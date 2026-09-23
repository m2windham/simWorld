using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;

using Xunit;

using CivilizationTarget = global::SimWorld.Director.CivilizationTarget;
using DifficultyDef = global::SimWorld.Director.DifficultyDef;
using DifficultyUtility = global::SimWorld.Director.DifficultyUtility;
using Storyteller = global::SimWorld.Director.Storyteller;
using StorytellerDef = global::SimWorld.Director.StorytellerDef;
using StorytellerUtility = global::SimWorld.Director.StorytellerUtility;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// The storyteller lever: <see cref="GodViewSnapshot.Storyteller"/>,
    /// <see cref="GodCommands.SetStoryteller"/>, <see cref="GodCommands.SetDifficulty"/> and
    /// <see cref="StorytellerCatalogue"/>.
    ///
    /// <para/><c>docs/design/the-loop.md</c> makes the storyteller the one source of pressure in a
    /// one-settlement game, which makes choosing it the player's biggest single statement about what kind of
    /// run they want — and it was reachable only through <c>Game.NewGame</c>'s two optional arguments, which
    /// the host is told never to call and which can only be answered once.
    ///
    /// <para/><b>What these tests assert, and what they deliberately do not.</b> Every consequence assertion
    /// below reads what the <i>simulation</i> computes afterwards — the threat points
    /// <see cref="StorytellerUtility.DefaultThreatPointsNow"/> produces, the factors
    /// <see cref="DifficultyUtility"/> hands the rest of the game, the comps the storyteller will actually
    /// decide with. Asserting that <c>Storyteller.difficulty</c> now holds the def that was just assigned to it
    /// would be asserting the setter, which is the thing <c>docs/design/player-first.md</c> §2 is written to
    /// rule out. Directions and bands, never a literal: the numbers are content's and content may retune them.
    /// </summary>
    public class GodCommandsStorytellerTests : ContentTestBase
    {
        public GodCommandsStorytellerTests(CoreContentFixture content) : base(content)
        {
        }

        private static StorytellerDef Teller(string defName) => DefDatabase<StorytellerDef>.GetNamed(defName);

        private static DifficultyDef Difficulty(string defName) => DefDatabase<DifficultyDef>.GetNamed(defName);

        /// <summary>
        /// A game with nothing in it — enough for the commands to be legitimate (something is running for the
        /// choice to reach) and nothing like the cost of generating a world. The storyteller it auto-creates
        /// carries no def and no difficulty, which is exactly the state a game being assembled is in.
        /// </summary>
        private static Game EmptyGame()
        {
            var game = new Game();
            Find.CurrentGame = game;
            return game;
        }

        /// <summary>A civilization posed for the threat-point formula rather than founded — the same shape
        /// <c>DirectorTests</c> uses. Wealth well clear of the floor so a difficulty change has room to show
        /// up as a ratio rather than as a clamp.</summary>
        private static CivilizationTarget PosedCivilization(int people = 3, float wealth = 400_000f)
        {
            var target = new CivilizationTarget(Find.Storyteller) { PlayerWealthForStoryteller = wealth };
            for (int i = 0; i < people; i++) target.pawns.Add(NewHuman("Citizen" + i.ToString()));
            return target;
        }

        private static Game NewSoloGame(string seed, string? storyteller = null, string? difficulty = null) =>
            Game.NewGame(
                ScenarioDefOf.TribalStart.scenario, seed,
                subdivisionOverride: 3, soloStart: true, bandSize: 20,
                storytellerDef: storyteller == null ? null : Teller(storyteller),
                difficultyDef: difficulty == null ? null : Difficulty(difficulty));

        // ---- the read side: what a player is choosing between ----

        [Fact]
        public void The_snapshot_reports_the_storyteller_and_difficulty_the_game_was_started_with()
        {
            NewSoloGame("storyteller-read", storyteller: "Randy_Random", difficulty: "Rough");

            StorytellerView view = GodViewSnapshot.Capture().Storyteller;

            Assert.NotNull(view.Current);
            Assert.Equal("Randy_Random", view.Current!.DefName);
            Assert.Equal(Teller("Randy_Random").LabelCap, view.Current.Label);
            Assert.False(string.IsNullOrWhiteSpace(view.Current.Description));

            Assert.NotNull(view.CurrentDifficulty);
            Assert.Equal("Rough", view.CurrentDifficulty!.DefName);
            Assert.Equal(Difficulty("Rough").LabelCap, view.CurrentDifficulty.Label);
            Assert.False(string.IsNullOrWhiteSpace(view.CurrentDifficulty.Description));
        }

        [Fact]
        public void The_snapshot_lists_every_shipped_storyteller_and_difficulty_to_switch_to()
        {
            NewSoloGame("storyteller-options");

            StorytellerView view = GodViewSnapshot.Capture().Storyteller;

            Assert.Equal(DefDatabase<StorytellerDef>.DefCount, view.Storytellers.Count);
            Assert.Equal(DefDatabase<DifficultyDef>.DefCount, view.Difficulties.Count);
            Assert.Contains(view.Storytellers, o => o.DefName == "Cassandra_Classic");
            Assert.Contains(view.Storytellers, o => o.DefName == "Phoebe_Chillax");
            Assert.Contains(view.Storytellers, o => o.DefName == "Randy_Random");
            Assert.Contains(view.Difficulties, o => o.DefName == "Peaceful");
            Assert.Contains(view.Difficulties, o => o.DefName == "Extreme");
        }

        [Fact]
        public void Before_a_game_exists_the_snapshot_reports_nothing_in_effect_and_still_lists_the_options()
        {
            // The main-menu state. A host drawing a picker here needs the options and must not be told that
            // some storyteller is already telling a story that has not started.
            Assert.Null(Find.CurrentGame);

            StorytellerView view = GodViewSnapshot.Capture().Storyteller;

            Assert.Null(view.Current);
            Assert.Null(view.CurrentDifficulty);
            Assert.NotEmpty(view.Storytellers);
            Assert.NotEmpty(view.Difficulties);
        }

        // ---- the pre-game catalogue ----

        [Fact]
        public void The_pre_game_catalogue_lists_every_shipped_storyteller_with_no_game_in_play()
        {
            Assert.Null(Find.CurrentGame);

            IReadOnlyList<StorytellerOption> storytellers = StorytellerCatalogue.Storytellers();

            Assert.Equal(DefDatabase<StorytellerDef>.DefCount, storytellers.Count);
            foreach (StorytellerDef def in DefDatabase<StorytellerDef>.AllDefsListForReading)
            {
                StorytellerOption option = storytellers.Single(o => o.DefName == def.defName);
                Assert.Equal(def.LabelCap, option.Label);
                Assert.Equal(def.description ?? "", option.Description);
            }
        }

        [Fact]
        public void The_pre_game_catalogue_lists_every_shipped_difficulty_with_no_game_in_play()
        {
            Assert.Null(Find.CurrentGame);

            IReadOnlyList<DifficultyOption> difficulties = StorytellerCatalogue.Difficulties();

            Assert.Equal(DefDatabase<DifficultyDef>.DefCount, difficulties.Count);
            foreach (DifficultyDef def in DefDatabase<DifficultyDef>.AllDefsListForReading)
            {
                DifficultyOption option = difficulties.Single(o => o.DefName == def.defName);
                Assert.Equal(def.LabelCap, option.Label);
                Assert.Equal(def.description ?? "", option.Description);
            }
        }

        [Fact]
        public void The_catalogue_orders_storytellers_by_the_display_order_content_gives_them()
        {
            IReadOnlyList<StorytellerOption> storytellers = StorytellerCatalogue.Storytellers();

            List<int> order = storytellers.Select(o => Teller(o.DefName).listOrder).ToList();
            Assert.Equal(order.OrderBy(i => i).ToList(), order);
        }

        [Fact]
        public void The_catalogue_orders_difficulties_gentlest_first()
        {
            IReadOnlyList<DifficultyOption> difficulties = StorytellerCatalogue.Difficulties();

            List<float> scales = difficulties.Select(o => Difficulty(o.DefName).threatScale).ToList();
            Assert.Equal(scales.OrderBy(f => f).ToList(), scales);
            Assert.Equal("Peaceful", difficulties[0].DefName);
            Assert.Equal("Extreme", difficulties[difficulties.Count - 1].DefName);
        }

        [Fact]
        public void The_snapshot_and_the_pre_game_catalogue_offer_the_same_choices()
        {
            // One implementation, so a player cannot be offered a storyteller before the game that is not
            // there afterwards.
            NewSoloGame("storyteller-same-options");

            StorytellerView view = GodViewSnapshot.Capture().Storyteller;

            Assert.Equal(
                StorytellerCatalogue.Storytellers().Select(o => o.DefName).ToList(),
                view.Storytellers.Select(o => o.DefName).ToList());
            Assert.Equal(
                StorytellerCatalogue.Difficulties().Select(o => o.DefName).ToList(),
                view.Difficulties.Select(o => o.DefName).ToList());
        }

        // ---- the seam carries no instrument ----

        /// <summary>
        /// Nothing the storyteller seam hands out is a number.
        ///
        /// <para/><c>GodViewSeamIntegrityTests</c> already walks the whole of <c>God/View</c> for a
        /// <see cref="Def"/> or a live object, and it covers these three types — but a threat scale, a
        /// points-per-day factor or a "threat points now" readout is none of those things. It is a float, and
        /// it would sail through that test untouched. CLAUDE.md's rule about instruments ("the moment it
        /// becomes the target it starts doing the opposite") and RimWorld's own choice never to show a colony
        /// its threat points are therefore enforced here, structurally, rather than left to whoever adds the
        /// next field to <see cref="StorytellerOption"/> remembering the reason.
        ///
        /// <para/>The allow-list is the point: names and sentences, and lists of things made of names and
        /// sentences. A player choosing a storyteller is choosing the kind of run they want, and a column of
        /// multipliers beside that choice turns it into a spreadsheet to solve.
        /// </summary>
        [Fact]
        public void The_storyteller_seam_hands_out_no_number_for_a_player_to_optimise_against()
        {
            Type[] seamTypes = { typeof(StorytellerOption), typeof(DifficultyOption), typeof(StorytellerView) };
            var allowed = new HashSet<Type>
            {
                typeof(string), typeof(StorytellerOption), typeof(DifficultyOption),
            };

            int checkedProperties = 0;
            foreach (Type type in seamTypes)
            {
                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    checkedProperties++;

                    Type carried = property.PropertyType;
                    if (carried.IsGenericType && carried.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
                    {
                        carried = carried.GetGenericArguments()[0];
                    }

                    Assert.True(
                        allowed.Contains(carried),
                        type.Name + "." + property.Name + " carries a " + carried.Name
                        + "; the storyteller seam carries defNames and content's own sentences only, so a "
                        + "player picks the run they want instead of solving for the number beside it.");
                }
            }

            // Guards the guard: three types with no properties between them would pass vacuously.
            Assert.True(checkedProperties >= 8, "expected the whole storyteller seam, found "
                + checkedProperties.ToString() + " properties");
        }

        // ---- the consequence: what the simulation computes afterwards ----

        [Fact]
        public void A_harder_difficulty_raises_the_threat_points_the_storyteller_computes()
        {
            EmptyGame();
            CivilizationTarget target = PosedCivilization();
            Find.TickManager.DebugSetTicksGame(0);
            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetDifficulty("Medium").Outcome);
            float onMedium = StorytellerUtility.DefaultThreatPointsNow(target);

            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetDifficulty("Rough").Outcome);
            float onRough = StorytellerUtility.DefaultThreatPointsNow(target);

            // Nothing else moved between the two reads — same tick, same people, same wealth — so the whole
            // difference is the difficulty the formula multiplied by. A band, not a literal: content owns the
            // two threatScales and may retune them, but Rough is harder than Medium by construction.
            Assert.True(onRough > onMedium,
                "Rough should cost more than Medium: " + onRough.ToString() + " vs " + onMedium.ToString());
            Assert.InRange(onRough / onMedium, 1.2f, 2.0f);
        }

        [Fact]
        public void Peaceful_drops_the_storyteller_to_its_own_floor()
        {
            EmptyGame();
            CivilizationTarget target = PosedCivilization();
            Find.TickManager.DebugSetTicksGame(0);
            GodCommands.SetDifficulty("Medium");
            float onMedium = StorytellerUtility.DefaultThreatPointsNow(target);

            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetDifficulty("Peaceful").Outcome);
            float onPeaceful = StorytellerUtility.DefaultThreatPointsNow(target);

            Assert.True(onPeaceful < onMedium);
            // Peaceful's threatScale of 0 sends the product to zero and the clamp catches it — the same floor
            // director.difficulty.peaceful already pins from the Director's own side.
            Assert.Equal(StorytellerUtility.MinThreatPoints, onPeaceful);
        }

        [Fact]
        public void A_difficulty_chosen_through_the_command_reaches_the_rest_of_the_simulation_too()
        {
            // Not only the storyteller: every consumer reads DifficultyUtility live, so the same one choice
            // moves harvests, mood and research from the next time each is asked.
            EmptyGame();
            GodCommands.SetDifficulty("Medium");
            float cropOnMedium = DifficultyUtility.CropYieldFactor;
            float moodOnMedium = DifficultyUtility.ColonistMoodOffset;
            float researchOnMedium = DifficultyUtility.ResearchSpeedFactor;

            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetDifficulty("Extreme").Outcome);

            Assert.True(DifficultyUtility.CropYieldFactor < cropOnMedium);
            Assert.True(DifficultyUtility.ColonistMoodOffset < moodOnMedium);
            Assert.True(DifficultyUtility.ResearchSpeedFactor < researchOnMedium);
        }

        [Fact]
        public void A_new_storyteller_brings_its_own_escalation_curve_to_the_threat_points()
        {
            EmptyGame();
            CivilizationTarget target = PosedCivilization();
            GodCommands.SetDifficulty("Medium");
            // Day 100: far enough along that the two narrators' own pointsFactorFromDaysPassed curves have
            // visibly parted (Cassandra escalates harder than Phoebe over a long game). At day 0 both are 1
            // and the change would be invisible, which is the honest reason this test moves the clock.
            Find.TickManager.DebugSetTicksGame(100 * GenDate.TicksPerDay);

            GodCommands.SetStoryteller("Cassandra_Classic");
            float underCassandra = StorytellerUtility.DefaultThreatPointsNow(target);

            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetStoryteller("Phoebe_Chillax").Outcome);
            float underPhoebe = StorytellerUtility.DefaultThreatPointsNow(target);

            Assert.True(underPhoebe < underCassandra,
                "Phoebe escalates more gently than Cassandra by day 100: "
                + underPhoebe.ToString() + " vs " + underCassandra.ToString());
            Assert.InRange(underPhoebe / underCassandra, 0.3f, 0.9f);
        }

        [Fact]
        public void A_new_storyteller_rebuilds_the_comps_that_decide_what_actually_fires()
        {
            // The half of the change that is not the points curve. Without RebuildComps the storyteller would
            // keep firing the old narrator's cadence under the new one's name — a command reporting Done for
            // something that only half happened.
            EmptyGame();
            GodCommands.SetStoryteller("Cassandra_Classic");
            int cassandraComps = Find.Storyteller.Comps.Count;
            Assert.True(cassandraComps > 1, "Cassandra ships several comps; the test needs that to be true.");

            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetStoryteller("Randy_Random").Outcome);

            StorytellerDef randy = Teller("Randy_Random");
            Assert.NotNull(randy.comps);
            Assert.Equal(randy.comps!.Count, Find.Storyteller.Comps.Count);
            Assert.NotEqual(cassandraComps, Find.Storyteller.Comps.Count);
            for (int i = 0; i < randy.comps.Count; i++)
            {
                Assert.Same(randy.comps[i], Find.Storyteller.Comps[i].props);
            }
        }

        [Fact]
        public void A_difficulty_change_moves_the_points_a_real_founded_civilization_computes()
        {
            // The same assertion again, this time against a game that was actually generated and founded,
            // rather than a posed target — so nothing here depends on the pose being representative.
            Game game = NewSoloGame("storyteller-real-game", difficulty: "Medium");
            float onMedium = StorytellerUtility.DefaultThreatPointsNow(game.CivilizationTarget);

            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetDifficulty("Extreme").Outcome);
            float onExtreme = StorytellerUtility.DefaultThreatPointsNow(game.CivilizationTarget);

            Assert.True(onExtreme > onMedium,
                "Extreme should cost more than Medium: " + onExtreme.ToString() + " vs " + onMedium.ToString());
        }

        // ---- refusals, one per guard ----

        [Fact]
        public void Refuses_a_missing_storyteller_id_before_it_ever_reaches_the_database()
        {
            // Asserting the wording, not just the outcome, because an unnamed id would otherwise fall through
            // to the unknown-def branch and be refused there — which looks identical from the outside and
            // would leave this guard untested. It is not redundant: a null id reaching
            // DefDatabase.GetNamedSilentFail throws rather than returning null, which is why
            // GodCommands.Resolve makes the same check one file over.
            EmptyGame();
            GodCommands.SetStoryteller("Cassandra_Classic");

            GodCommandResult empty = GodCommands.SetStoryteller("");
            GodCommandResult missing = GodCommands.SetStoryteller(null!);

            Assert.Equal(GodCommandOutcome.Refused, empty.Outcome);
            Assert.False(empty.Changed);
            Assert.Equal("No storyteller id given.", empty.Reason);
            Assert.Equal(GodCommandOutcome.Refused, missing.Outcome);
            Assert.Equal("No storyteller id given.", missing.Reason);
            Assert.Equal("Cassandra_Classic", Find.Storyteller.def.defName);
        }

        [Fact]
        public void Refuses_a_missing_difficulty_id_before_it_ever_reaches_the_database()
        {
            EmptyGame();
            GodCommands.SetDifficulty("Medium");

            GodCommandResult empty = GodCommands.SetDifficulty("");
            GodCommandResult missing = GodCommands.SetDifficulty(null!);

            Assert.Equal(GodCommandOutcome.Refused, empty.Outcome);
            Assert.Equal("No difficulty id given.", empty.Reason);
            Assert.Equal(GodCommandOutcome.Refused, missing.Outcome);
            Assert.Equal("No difficulty id given.", missing.Reason);
            Assert.Equal("Medium", Find.Storyteller.difficulty.defName);
        }

        [Fact]
        public void Refuses_an_unknown_storyteller_and_leaves_the_one_in_effect_alone()
        {
            EmptyGame();
            GodCommands.SetStoryteller("Cassandra_Classic");
            int compsBefore = Find.Storyteller.Comps.Count;

            GodCommandResult result = GodCommands.SetStoryteller("NoSuchStorytellerAtAll");

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.False(result.Changed);
            // The name the host asked for is in the refusal: a stale handle is told it is stale rather than
            // silently doing nothing.
            Assert.Contains("NoSuchStorytellerAtAll", result.Reason);
            Assert.Equal("Cassandra_Classic", Find.Storyteller.def.defName);
            Assert.Equal(compsBefore, Find.Storyteller.Comps.Count);
        }

        [Fact]
        public void Refuses_an_unknown_difficulty_and_leaves_the_one_in_effect_alone()
        {
            EmptyGame();
            CivilizationTarget target = PosedCivilization();
            Find.TickManager.DebugSetTicksGame(0);
            GodCommands.SetDifficulty("Medium");
            float before = StorytellerUtility.DefaultThreatPointsNow(target);

            GodCommandResult result = GodCommands.SetDifficulty("NoSuchDifficultyAtAll");

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.False(result.Changed);
            Assert.Contains("NoSuchDifficultyAtAll", result.Reason);
            Assert.Equal("Medium", Find.Storyteller.difficulty.defName);
            // And the simulation computes exactly what it did before the refused call.
            Assert.Equal(before, StorytellerUtility.DefaultThreatPointsNow(target));
        }

        [Fact]
        public void Refuses_to_choose_a_storyteller_before_a_game_exists()
        {
            // Find.Storyteller auto-creates a bare storyteller with no game, and Game.NewGame then replaces it
            // wholesale — so a Done here would be a report about an object nothing will ever read.
            Assert.Null(Find.CurrentGame);

            GodCommandResult result = GodCommands.SetStoryteller("Randy_Random");

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.False(result.Changed);
            Assert.Contains("No game is running", result.Reason);
            Assert.Null(GodViewSnapshot.Capture().Storyteller.Current);
        }

        [Fact]
        public void Refuses_to_choose_a_difficulty_before_a_game_exists()
        {
            Assert.Null(Find.CurrentGame);

            GodCommandResult result = GodCommands.SetDifficulty("Extreme");

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.False(result.Changed);
            Assert.Contains("No game is running", result.Reason);
            Assert.Null(GodViewSnapshot.Capture().Storyteller.CurrentDifficulty);
        }

        [Fact]
        public void Choosing_the_storyteller_already_in_effect_is_a_no_op_not_a_success()
        {
            EmptyGame();
            GodCommands.SetStoryteller("Phoebe_Chillax");

            GodCommandResult result = GodCommands.SetStoryteller("Phoebe_Chillax");

            Assert.Equal(GodCommandOutcome.NoChange, result.Outcome);
            Assert.False(result.Changed);
            Assert.Equal("Phoebe_Chillax", Find.Storyteller.def.defName);
        }

        [Fact]
        public void Choosing_the_difficulty_already_in_effect_is_a_no_op_not_a_success()
        {
            EmptyGame();
            GodCommands.SetDifficulty("Rough");

            GodCommandResult result = GodCommands.SetDifficulty("Rough");

            Assert.Equal(GodCommandOutcome.NoChange, result.Outcome);
            Assert.False(result.Changed);
            Assert.Equal("Rough", Find.Storyteller.difficulty.defName);
        }

        // ---- allowed, however unwise (docs/design/player-first.md §5) ----

        [Fact]
        public void Turning_the_pressure_off_mid_run_is_allowed_and_the_history_is_not_unmade()
        {
            // Dropping to Peaceful two hundred days in is a choice the player is allowed to regret. What it
            // must not do is quietly wipe what the civilization has already lived through, or hand out a
            // guaranteed quiet spell by resetting the adaptation clock.
            EmptyGame();
            GodCommands.SetStoryteller("Cassandra_Classic");
            GodCommands.SetDifficulty("Rough");
            Find.Storyteller.RecordChronicle("Something happened worth remembering.");
            for (int i = 0; i < 100; i++) Find.Storyteller.adaptation.AdaptationTick(Find.Storyteller.difficulty);

            int chronicleBefore = Find.Storyteller.Chronicle.Count;
            int momentsBefore = Find.Storyteller.Moments.Count;
            float adaptBefore = Find.Storyteller.adaptation.AdaptDays;
            Assert.True(chronicleBefore > 0 && adaptBefore > 0f);

            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetDifficulty("Peaceful").Outcome);
            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetStoryteller("Randy_Random").Outcome);

            Assert.Equal(chronicleBefore, Find.Storyteller.Chronicle.Count);
            Assert.Equal(momentsBefore, Find.Storyteller.Moments.Count);
            Assert.Equal(adaptBefore, Find.Storyteller.adaptation.AdaptDays);
        }

        // ---- the view can never offer a pick the command would refuse ----

        [Fact]
        public void Every_option_the_view_offers_is_one_the_command_accepts()
        {
            EmptyGame();
            StorytellerView view = GodViewSnapshot.Capture().Storyteller;

            foreach (StorytellerOption option in view.Storytellers)
            {
                GodCommandResult result = GodCommands.SetStoryteller(option.DefName);
                Assert.True(
                    result.Outcome == GodCommandOutcome.Done || result.Outcome == GodCommandOutcome.NoChange,
                    option.DefName + " was offered by the view and refused by the command: " + result.Reason);
            }

            foreach (DifficultyOption option in view.Difficulties)
            {
                GodCommandResult result = GodCommands.SetDifficulty(option.DefName);
                Assert.True(
                    result.Outcome == GodCommandOutcome.Done || result.Outcome == GodCommandOutcome.NoChange,
                    option.DefName + " was offered by the view and refused by the command: " + result.Reason);
            }
        }

        // ---- Scribe round trip ----

        [Fact]
        public void A_storyteller_and_difficulty_chosen_through_the_commands_survive_a_Scribe_round_trip()
        {
            EmptyGame();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetStoryteller("Phoebe_Chillax").Outcome);
            Assert.Equal(GodCommandOutcome.Done, GodCommands.SetDifficulty("Rough").Outcome);

            string xml = Scribe.SaveToString(Find.Storyteller, "storyteller");
            Storyteller loaded = Scribe.Load<Storyteller>(xml, "storyteller", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal("Phoebe_Chillax", loaded.def.defName);
            Assert.Equal("Rough", loaded.difficulty.defName);
            // The comps come back with it, so a reloaded game keeps the cadence the player chose and not only
            // the name of the narrator who was meant to be running it.
            Assert.Equal(Teller("Phoebe_Chillax").comps!.Count, loaded.Comps.Count);

            // And the seam reports the reloaded choice, which is what the host would draw after a load.
            Find.Storyteller = loaded;
            StorytellerView view = GodViewSnapshot.Capture().Storyteller;
            Assert.Equal("Phoebe_Chillax", view.Current!.DefName);
            Assert.Equal("Rough", view.CurrentDifficulty!.DefName);
        }
    }
}
