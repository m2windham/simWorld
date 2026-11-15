using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.God.View;
using SimWorld.Letters;
using SimWorld.MindState;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// The three ways the player follows a settlement through the god view: the roster row (who is doing
    /// what, in what state), the letter feed (news as it arrives), and the chronicle feed (what happened, kept).
    /// Every event here goes through the simulation's own funnel — a mental state started on the pawn, a death
    /// through <c>FamilyManager.HandleDeath</c> — never a hand-built entry.
    /// </summary>
    [Collection("GlobalDefs")]
    public class PlayerFeedTests : ContentTestBase
    {
        public PlayerFeedTests(CoreContentFixture content) : base(content)
        {
        }

        private static Settlement OpenedSettlement(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        private static Pawn SpawnedCitizen(Settlement s) => s.Citizens.First(p => p.Spawned && !p.Dead);

        private static CitizenLine Row(Settlement s, Pawn p) =>
            GodViewSnapshot.CitizensOf(s.tile)!.Citizens.Single(c => c.ThingId == p.thingIDNumber);

        // ---- roster ----

        [Fact]
        public void A_roster_row_reflects_a_citizen_going_downed_and_getting_up()
        {
            Settlement s = OpenedSettlement("roster-downed");
            Pawn p = SpawnedCitizen(s);
            Assert.False(Row(s, p).Downed);

            p.health.ForceDowned = true;
            Assert.True(Row(s, p).Downed);

            p.health.ForceDowned = false;
            Assert.False(Row(s, p).Downed);
        }

        [Fact]
        public void A_roster_row_carries_life_stage_activity_skills_and_household()
        {
            Settlement s = OpenedSettlement("roster-fields");
            RunTicks(600);
            Pawn p = SpawnedCitizen(s);
            CitizenLine row = Row(s, p);

            Assert.Equal(p.ageTracker.CurLifeStage!.defName, row.LifeStageDefName);
            Assert.Equal(p.jobs.curJob?.def.defName, row.CurrentJobDefName);
            Assert.Equal(row.CurrentJobDefName != null, row.ActivityLabel != null);
            Assert.Equal(p.skills!.skills.Count, row.Skills.Count);
            Assert.Equal(p.relations.HasFamily, row.HouseholdId != null);
            Assert.Equal(row.HouseholdId != null, row.HouseholdName != null);
        }

        [Fact]
        public void Starving_shows_as_a_critical_need_and_a_mental_state_as_the_activity()
        {
            Settlement s = OpenedSettlement("roster-critical");
            Pawn p = SpawnedCitizen(s);

            p.needs.food!.CurLevelPercentage = 0f;
            Assert.Contains(p.needs.food.def.defName, Row(s, p).CriticalNeedDefNames);
            p.needs.food.CurLevelPercentage = 1f;
            Assert.DoesNotContain(p.needs.food.def.defName, Row(s, p).CriticalNeedDefNames);

            MentalStateDef sad = DefDatabase<MentalStateDef>.GetNamed("Wander_Sad");
            Assert.True(p.mindState.mentalStateHandler.TryStartMentalState(sad));
            CitizenLine row = Row(s, p);
            Assert.Equal("Wander_Sad", row.MentalStateDefName);
            Assert.Equal(sad.LabelCap, row.ActivityLabel);
        }

        // ---- letters ----

        [Fact]
        public void A_mental_break_lands_in_the_letter_feed_naming_the_citizen_and_dismissing_removes_it()
        {
            Settlement s = OpenedSettlement("letter-break");
            Pawn p = SpawnedCitizen(s);
            int before = GodViewSnapshot.Capture().Letters.Count;

            Assert.True(p.mindState.mentalStateHandler.TryStartMentalState(DefDatabase<MentalStateDef>.GetNamed("Wander_Sad")));

            IReadOnlyList<LetterView> letters = GodViewSnapshot.Capture().Letters;
            Assert.Equal(before + 1, letters.Count);
            LetterView letter = letters[letters.Count - 1];
            Assert.Contains(p.thingIDNumber, letter.LookTargetThingIds);
            Assert.Contains(p.Label, letter.Label);
            Assert.Equal(Find.TickManager.TicksGame, letter.ArrivalTick);
            Assert.False(letter.IsChoice);
            Assert.False(string.IsNullOrEmpty(letter.DefName));

            GodCommandResult result = GodCommands.DismissLetter(letter.LetterID);
            Assert.Equal(GodCommandOutcome.Done, result.Outcome);
            Assert.DoesNotContain(GodViewSnapshot.Capture().Letters, l => l.LetterID == letter.LetterID);

            Assert.Equal(GodCommandOutcome.Refused, GodCommands.DismissLetter(letter.LetterID).Outcome);
        }

        [Fact]
        public void A_letter_asking_a_question_is_refused_dismissal_and_stays()
        {
            Find.LetterStack = new LetterStack();
            var choice = new ChoiceLetter { def = LetterDefOf.NeutralEvent, label = "Offer", text = "?" };
            choice.choices.Add(new LetterChoice("Decline"));
            Find.LetterStack.ReceiveLetter(choice);

            GodCommandResult result = GodCommands.DismissLetter(choice.letterID);

            Assert.Equal(GodCommandOutcome.Refused, result.Outcome);
            Assert.Contains(GodViewSnapshot.Capture().Letters, l => l.LetterID == choice.letterID && l.IsChoice);
        }

        [Fact]
        public void Look_targets_read_back_as_thing_ids_and_anything_else_is_left_out()
        {
            IReadOnlyList<int> ids = LetterView.ThingIdsOf(new List<string> { "Thing_Human42", "Thing_Sandstone7", "Map_3", "Thing_" });
            Assert.Equal(new[] { 42, 7 }, ids);
        }

        // ---- chronicle ----

        [Fact]
        public void A_death_enters_the_chronicle_feed_with_its_cause_and_who_died()
        {
            Settlement s = OpenedSettlement("chronicle-death");
            Pawn victim = SpawnedCitizen(s);
            int tick = Find.TickManager.TicksGame;

            Find.FamilyManager.HandleDeath(victim, DeathCause.Disease, s.Citizens.ToDictionary(p => p.thingIDNumber));

            ChronicleLine death = GodViewSnapshot.ChronicleSince(tick - 1).Single(l => l.Kind == ChronicleKind.Death);
            Assert.Equal(DeathCause.Disease, death.DeathCause);
            Assert.Equal(new[] { victim.thingIDNumber }, death.InvolvedThingIds);
            Assert.Contains(victim.Label, death.Text);
            Assert.Equal(tick, death.Tick);
        }

        [Fact]
        public void The_chronicle_feed_returns_only_entries_after_the_tick_given_and_nothing_before_a_game()
        {
            Find.Storyteller = new Storyteller();
            Assert.Empty(GodViewSnapshot.ChronicleSince(-1));

            Find.Storyteller.RecordChronicle("First thing");
            int firstTick = Find.TickManager.TicksGame;
            RunTicks(5);
            Find.Storyteller.RecordChronicle("Second thing");

            ChronicleLine only = GodViewSnapshot.ChronicleSince(firstTick).Single();
            Assert.Equal(ChronicleKind.Event, only.Kind);
            Assert.Equal("Second thing", only.Text);
            Assert.Empty(only.InvolvedThingIds);
            Assert.Equal(2, GodViewSnapshot.ChronicleSince(-1).Count);
        }

        [Fact]
        public void A_chronicle_entrys_involved_ids_round_trip_through_Scribe()
        {
            var entry = new ChronicleEntry(10, "Death", "Ana", 0f)
            {
                deathCause = DeathCause.Injury,
                involvedThingIds = new List<int> { 17 },
            };

            string xml = Scribe.SaveToString(entry, "entry");
            ChronicleEntry loaded = Scribe.Load<ChronicleEntry>(xml, "entry", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(new List<int> { 17 }, loaded.involvedThingIds);
            Assert.Equal(DeathCause.Injury, loaded.deathCause);
        }
    }
}
