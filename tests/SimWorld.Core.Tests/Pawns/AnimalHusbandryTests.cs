using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Pawns
{
    /// <summary>Wildness/tameness, taming's failure state, and the training tracker (system: ai.animals).</summary>
    public class AnimalHusbandryTests : ContentTestBase
    {
        public AnimalHusbandryTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Muffalo => DefDatabase<ThingDef>.GetNamed("Muffalo");
        private static ThingDef Chicken => DefDatabase<ThingDef>.GetNamed("Chicken");

        private static Faction NewFaction(string name) =>
            new Faction(DefDatabase<FactionDef>.GetNamed("OutlanderCivilization"), name, "F_" + name);

        /// <summary>
        /// Advances only the training tracker's own periodic check, not a full pawn Tick — decay is what
        /// these tests are pinning, and a real Tick would also run needs (an untended animal has nothing to
        /// eat on this test's bare setup, so a several-day window would starve it long before decay is even
        /// due, confusing "died" with "decayed").
        /// </summary>
        private static void AdvanceTraining(Pawn animal, int ticks)
        {
            int start = Find.TickManager.TicksGame;
            for (int i = 1; i <= ticks; i++)
            {
                Find.TickManager.DebugSetTicksGame(start + i);
                animal.training.TrainingTrackerTick();
            }
        }

        // ---- content ----

        [Fact]
        public void Training_content_loads_with_expected_counts_and_DefOfs_bound()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.Equal(3, DefDatabase<TrainabilityDef>.DefCount);
            Assert.Equal(4, DefDatabase<TrainableDef>.DefCount);
            Assert.NotNull(TrainabilityDefOf.None);
            Assert.NotNull(TrainabilityDefOf.Intermediate);
            Assert.NotNull(TrainabilityDefOf.Advanced);
            Assert.True(TrainabilityDefOf.Advanced.intelligenceOrder > TrainabilityDefOf.Intermediate.intelligenceOrder);
            Assert.True(TrainabilityDefOf.Intermediate.intelligenceOrder > TrainabilityDefOf.None.intelligenceOrder);
            Assert.NotNull(TrainableDefOf.Obedience);
            Assert.NotNull(TrainableDefOf.Release);
            Assert.NotNull(TrainableDefOf.Rescue);
            Assert.NotNull(TrainableDefOf.Haul);
        }

        [Fact]
        public void Wildness_varies_by_race_rather_than_being_notionally_uniform()
        {
            Assert.True(Husky.race!.wildness < Muffalo.race!.wildness);
            Assert.True(Muffalo.race!.wildness > 0f);
            Assert.True(Husky.race!.wildness > 0f);
            // Humanlike's default (unset in content) stays 0 — wildness is meaningless for a person.
            Assert.Equal(0f, Human.race!.wildness);
        }

        // ---- Pawn_TrainingTracker ----

        [Fact]
        public void An_untamed_animal_cannot_be_trained_at_all()
        {
            Pawn husky = new Pawn(Husky, "Rex"); // no faction: wild.
            Assert.False(husky.training.CanBeTrained(TrainableDefOf.Obedience));
            Assert.Null(husky.training.NextToTrain());
        }

        [Fact]
        public void A_race_with_no_trainability_can_never_be_trained_even_once_tamed()
        {
            Pawn chicken = new Pawn(Chicken, "Hen") { faction = NewFaction("Coop") };
            Assert.Equal(TrainabilityDefOf.None, chicken.RaceProps.trainability);
            Assert.False(chicken.training.CanBeTrained(TrainableDefOf.Obedience));
            Assert.Null(chicken.training.NextToTrain());
        }

        [Fact]
        public void Training_steps_accumulate_and_prerequisites_gate_later_steps()
        {
            Pawn husky = new Pawn(Husky, "Rex") { faction = NewFaction("Colony") };
            Pawn trainer = NewHuman("Trainer");

            Assert.Equal(TrainableDefOf.Obedience, husky.training.NextToTrain());
            Assert.False(husky.training.CanBeTrained(TrainableDefOf.Release));

            husky.training.Train(TrainableDefOf.Obedience, trainer);
            Assert.True(husky.training.HasLearned(TrainableDefOf.Obedience));

            // Release/Rescue/Haul all require Obedience; now learnable, and multi-step ones need more than one session.
            Assert.True(husky.training.CanBeTrained(TrainableDefOf.Release));
            Assert.False(husky.training.HasLearned(TrainableDefOf.Haul));
            husky.training.Train(TrainableDefOf.Haul, trainer);
            Assert.Equal(1, husky.training.GetSteps(TrainableDefOf.Haul));
            Assert.False(husky.training.HasLearned(TrainableDefOf.Haul));
            husky.training.Train(TrainableDefOf.Haul, trainer);
            Assert.True(husky.training.HasLearned(TrainableDefOf.Haul));
        }

        [Fact]
        public void Reinforced_training_does_not_decay_within_the_grace_window()
        {
            Pawn husky = new Pawn(Husky, "Rex") { faction = NewFaction("Colony") };
            Pawn trainer = NewHuman("Trainer");
            husky.training.Train(TrainableDefOf.Obedience, trainer);
            Assert.True(husky.training.HasLearned(TrainableDefOf.Obedience));

            AdvanceTraining(husky, AnimalTuning.TrainingDecayGraceTicks - AnimalTuning.TrainingDecayCheckIntervalTicks);

            Assert.True(husky.training.HasLearned(TrainableDefOf.Obedience));
            Assert.Equal(1, husky.training.GetSteps(TrainableDefOf.Obedience));
        }

        [Fact]
        public void Untended_training_can_decay_well_outside_the_grace_window()
        {
            // Statistical: run several independent husky/seed pairs well past the grace window and past
            // several decay-check intervals, and assert decay happens in at least one of them — the MTB roll
            // is real, not a guarantee on any single check, so this is a trend, not a per-trial assertion.
            bool everDecayed = false;
            for (int seed = 1; seed <= 30 && !everDecayed; seed++)
            {
                Rand.Current = new RandomStream(seed);
                Find.TickManager = new TickManager();
                Pawn husky = new Pawn(Husky, "Rex") { faction = NewFaction("Colony") };
                Pawn trainer = NewHuman("Trainer");
                husky.training.Train(TrainableDefOf.Obedience, trainer);

                AdvanceTraining(husky, AnimalTuning.TrainingDecayGraceTicks + AnimalTuning.TrainingDecayCheckIntervalTicks * 40);

                if (!husky.training.HasLearned(TrainableDefOf.Obedience)) everDecayed = true;
            }
            Assert.True(everDecayed, "expected untended training to decay in at least one of 30 independent trials");
        }

        // ---- Pawn_MindState tameness/angryAt Scribe round-trip ----

        [Fact]
        public void Tameness_and_angry_at_state_round_trip_through_Scribe()
        {
            Pawn tamer = NewHuman("Handler");
            Pawn husky = new Pawn(Husky, "Rex");
            husky.mindState.tameness = 0.75f;
            husky.mindState.angryAt = tamer;
            husky.mindState.angryUntilTick = 12345;
            husky.training.Train(TrainableDefOf.Obedience, tamer);

            var holder = new PawnHolder { pawns = new List<Pawn> { husky, tamer } };
            string xml = Scribe.SaveToString(holder, "game");
            Pawn.ResetThingIdCounter();
            PawnHolder loaded = Scribe.Load<PawnHolder>(xml, "game", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Pawn loadedHusky = loaded.pawns![0];
            Pawn loadedTamer = loaded.pawns[1];
            Assert.Equal(0.75f, loadedHusky.mindState.tameness);
            Assert.Same(loadedTamer, loadedHusky.mindState.angryAt);
            Assert.Equal(12345, loadedHusky.mindState.angryUntilTick);
            Assert.True(loadedHusky.training.HasLearned(TrainableDefOf.Obedience));

            // Loaded pawns must still be fully wired: ticking must not throw.
            RunTicks(1000, loadedHusky, loadedTamer);
        }
    }
}
