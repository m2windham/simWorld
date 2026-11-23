using System.Collections.Generic;
using System.Linq;

using SimWorld.Alerts;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Health;
using SimWorld.Letters;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Thoughts;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// The view data behind the fresh-eyes playtest's top findings (2026-11-23): a citizen's mood reasons and
    /// health, era progress counted on the projects that actually turn the era, where events happened, and
    /// labels where developer strings used to leak. Every assertion goes through the seam the host binds to.
    /// </summary>
    [Collection("GlobalDefs")]
    public class PlaytestViewTests : ContentTestBase
    {
        public PlaytestViewTests(CoreContentFixture content) : base(content)
        {
        }

        private static Game NewSoloGame(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);

        private static Settlement PlayerSettlement(Game game) => game.World!.worldObjects.OfType<Settlement>().First();

        private static void ResetDirector()
        {
            Find.Storyteller = new global::SimWorld.Director.Storyteller();
            Find.FactionManager = new FactionManager();
            Find.LetterStack = new LetterStack();
            Find.God = new GodManager();
            Find.ResearchManager = new ResearchManager();
            CorpseDefGenerator.EnsureGenerated();
            NameUseChecker.Clear();
        }

        // ---- 1. citizen detail ----

        [Fact]
        public void A_citizen_view_says_what_is_weighing_on_them()
        {
            Game game = NewSoloGame("playtest-thoughts");
            Settlement settlement = PlayerSettlement(game);
            GodCommands.OpenSettlement(settlement.tile);
            Pawn pawn = settlement.Citizens.First(p => p.Spawned && p.needs?.mood != null);

            ThoughtHandler thoughts = pawn.needs.mood!.thoughts;
            thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("AteWithoutTable"));
            thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("Insulted"));
            thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("Insulted"));
            float simulationTotal = thoughts.TotalMoodOffset();

            CitizenView view = GodViewSnapshot.Citizen(pawn.thingIDNumber)!;

            Assert.NotEqual(CitizenFidelity.NotTracked, view.ThoughtsFidelity);
            ThoughtLine table = Assert.Single(view.Thoughts, t => t.DefName == "AteWithoutTable");
            Assert.True(table.MoodOffset < 0f);
            Assert.True(table.IsMemory);
            Assert.True(table.TicksLeft > 0);
            Assert.NotEqual(table.DefName, table.Label);
            ThoughtLine insulted = Assert.Single(view.Thoughts, t => t.DefName == "Insulted");
            Assert.Equal(2, insulted.Count);

            // The rows are the simulation's own sum, stacking included — not a second formula that drifts.
            Assert.Equal(simulationTotal, view.ThoughtsMoodOffset, 3);
            Assert.Equal(simulationTotal, view.Thoughts.Sum(t => t.MoodOffset), 3);
            // Worst first.
            for (int i = 1; i < view.Thoughts.Count; i++)
            {
                Assert.True(view.Thoughts[i - 1].MoodOffset <= view.Thoughts[i].MoodOffset);
            }

            Assert.NotNull(view.BreakThresholdMinor);
            Assert.True(view.BreakThresholdExtreme < view.BreakThresholdMajor);
            Assert.True(view.BreakThresholdMajor < view.BreakThresholdMinor);
        }

        [Fact]
        public void A_citizen_view_reports_job_role_and_illness_in_words()
        {
            Game game = NewSoloGame("playtest-health");
            Settlement settlement = PlayerSettlement(game);
            GodCommands.OpenSettlement(settlement.tile);
            RunTicks(GenDate.TicksPerHour);
            Pawn pawn = settlement.Citizens.First(p => p.Spawned && p.jobs?.curJob != null);
            pawn.health.AddHediff(DefDatabase<HediffDef>.GetNamed("Flu"));

            CitizenView view = GodViewSnapshot.Citizen(pawn.thingIDNumber)!;

            Assert.Equal(pawn.jobs.curJob!.def.defName, view.CurrentJobDefName);
            Assert.False(string.IsNullOrEmpty(view.CurrentJobLabel));
            Assert.NotEqual(view.CurrentJobDefName, view.CurrentJobLabel);
            Assert.False(string.IsNullOrEmpty(view.ActivityLabel));
            if (view.RoleDefName != null) Assert.False(string.IsNullOrEmpty(view.RoleLabel));

            HediffLine flu = Assert.Single(view.Hediffs, h => h.DefName == "Flu");
            Assert.NotNull(flu.Immunity);
            Assert.NotNull(flu.LethalSeverity);
            Assert.True(flu.IsBad);
            if (flu.NeedsTendingNow) Assert.True(view.NeedsTending);

            CitizenLine line = GodViewSnapshot.CitizensOf(settlement.tile)!.Citizens.First(c => c.ThingId == pawn.thingIDNumber);
            Assert.Equal(view.CurrentJobLabel, line.CurrentJobLabel);
        }

        [Fact]
        public void Reading_a_citizens_thoughts_does_not_recompute_them()
        {
            // The situational cache is the simulation's; a host looking must not move its clock. If the read
            // recomputed, a thought made true after the last recompute would appear in the view.
            Game game = NewSoloGame("playtest-thoughts-read");
            Settlement settlement = PlayerSettlement(game);
            GodCommands.OpenSettlement(settlement.tile);
            Pawn pawn = settlement.Citizens.First(p => p.Spawned && p.needs?.mood != null);
            ThoughtHandler thoughts = pawn.needs.mood!.thoughts;
            var before = new List<Thought>();
            thoughts.GetAllMoodThoughts(before); // recomputes now, as the simulation would
            int countBefore = thoughts.MoodThoughtGroupsAsComputed().Sum(g => g.Count);

            // A hunger thought becomes true and the cache is marked stale — the state the simulation leaves
            // between a need crossing a threshold and its own next query.
            pawn.needs.food!.CurLevel = 0f;
            thoughts.situational.Notify_SituationalThoughtsDirty();
            CitizenView view = GodViewSnapshot.Citizen(pawn.thingIDNumber)!;

            Assert.Equal(countBefore, view.Thoughts.Sum(t => t.Count));
            Assert.Equal(countBefore, thoughts.MoodThoughtGroupsAsComputed().Sum(g => g.Count));

            // Fixture: the simulation's own next query does see the new thought, so there was something to miss.
            var after = new List<Thought>();
            thoughts.GetAllMoodThoughts(after);
            Assert.True(after.Count > before.Count, "fixture: starving adds a situational thought");
        }

        // ---- 2. era progress on the spine ----

        [Fact]
        public void Era_progress_counts_the_projects_that_gate_the_era_and_names_the_missing_ones()
        {
            ResetDirector();
            EraDef first = DefDatabase<EraDef>.AllDefsListForReading.OrderBy(e => e.order).First();
            IReadOnlyList<ResearchProjectDef> spine = first.SpineProjects;
            List<ResearchProjectDef> leaves = first.Projects.Where(p => !spine.Contains(p)).ToList();
            Assert.True(spine.Count > 1 && leaves.Count > 0, "fixture: the first era has a spine and leaves");

            // The playtest's case: plenty researched, none of it the spine.
            foreach (ResearchProjectDef leaf in leaves) Find.ResearchManager.SetProjectFinishedForSetup(leaf);
            Find.ResearchManager.SetProjectFinishedForSetup(spine[0]);

            GodViewSnapshot snapshot = GodViewSnapshot.Capture();
            EraOutlook era = snapshot.Era;

            Assert.Equal(first.defName, era.FrontierEraDefName);
            Assert.Null(era.ReachedEraDefName);
            Assert.Equal(spine.Count, era.GatesTotal);
            Assert.Equal(1, era.GatesFinished);
            Assert.False(era.GatesAreWholeEra);
            Assert.Equal(1f / spine.Count, era.Progress, 4);
            Assert.Equal(leaves.Count + 1, era.EraProjectsFinished);
            // The old bar ran ahead of what turns the era; this one does not.
            Assert.True(snapshot.Civilization.EraProgress > era.Progress);

            Assert.Equal(spine.Skip(1).Select(p => p.defName), era.MissingGates.Select(g => g.DefName));
            Assert.All(era.MissingGates, g => Assert.Equal(DefDatabase<ResearchProjectDef>.GetNamed(g.DefName).LabelCap, g.Label));
        }

        [Fact]
        public void Finishing_the_spine_reaches_the_era_and_moves_the_bar_to_the_next()
        {
            ResetDirector();
            List<EraDef> eras = DefDatabase<EraDef>.AllDefsListForReading.OrderBy(e => e.order).ToList();
            foreach (ResearchProjectDef p in eras[0].SpineProjects) Find.ResearchManager.SetProjectFinishedForSetup(p);

            EraOutlook era = GodViewSnapshot.Capture().Era;

            Assert.Equal(eras[0].defName, era.ReachedEraDefName);
            Assert.Equal(eras[1].defName, era.FrontierEraDefName);
            Assert.Equal(era.GatesTotal - era.GatesFinished, era.MissingGates.Count);
            Assert.True(era.Progress < 1f);
        }

        // ---- 3. where events happened ----

        private static (CoreMap Map, Settlement Settlement, List<Thing> Walls) QuakeFixture()
        {
            ResetDirector();
            var map = new CoreMap(30, 30, TerrainDefOf.Soil);
            var ours = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Settlers", "F_Settlers");
            ThingDef wall = DefDatabase<ThingDef>.GetNamed("Wall");
            var walls = new List<Thing>();
            for (int x = 2; x < 26; x++)
            {
                Thing w = ThingMaker.MakeThing(wall, DefDatabase<ThingDef>.GetNamed("WoodLog"));
                w.SetFaction(ours);
                walls.Add(GenSpawn.Spawn(w, new IntVec3(x, 0, 12), map));
            }
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 7, ours, "Quaketown", 0);
            return (map, settlement, walls);
        }

        private static void FireQuake(CoreMap map, Settlement settlement)
        {
            var target = new CivilizationTarget(Find.Storyteller);
            target.SetSettlements(new[] { settlement });
            target.Map = map;
            Find.God.Attention.Focus(settlement);
            var parms = new IncidentParms { target = target };
            Assert.True(Find.Storyteller.TryFire(new FiringIncident(DefDatabase<IncidentDef>.GetNamed("Earthquake"), null, parms)));
        }

        [Fact]
        public void A_quake_letter_and_its_chronicle_line_say_where_the_structures_fell()
        {
            (CoreMap map, Settlement settlement, List<Thing> walls) = QuakeFixture();
            List<IntVec3> standing = walls.Select(w => w.Position).ToList();

            FireQuake(map, settlement);

            var fell = new HashSet<IntVec3>(standing.Where((c, i) => walls[i].Destroyed));
            Assert.NotEmpty(fell);

            LetterView letter = Assert.Single(GodViewSnapshot.Capture().Letters);
            Assert.NotNull(letter.Location);
            Assert.Equal(settlement.tile, letter.Location!.Tile);
            Assert.NotNull(letter.Location.Cell);
            Assert.Equal(System.Math.Min(fell.Count, EventLocation.MaxAffectedCells), letter.Location.AffectedCells.Count);
            Assert.All(letter.Location.AffectedCells, c => Assert.Contains(c, fell));
            Assert.Contains(letter.Location.Cell!.Value, fell);

            ChronicleLine line = Assert.Single(GodViewSnapshot.ChronicleSince(-1));
            Assert.NotNull(line.Location);
            Assert.Equal(letter.Location.AffectedCells, line.Location!.AffectedCells);
            Assert.Equal("Earthquake", line.Text);

            // One event, one mark: the letter and the chronicle line are the same quake.
            EventMark mark = Assert.Single(GodViewSnapshot.RecentEvents());
            Assert.Equal(EventMarkSource.Letter, mark.Source);
            Assert.Equal(letter.LetterID, mark.LetterID);
            Assert.Equal(letter.Location.Cell, mark.Location.Cell);
        }

        [Fact]
        public void Recent_events_are_newest_first_and_capped()
        {
            (CoreMap map, Settlement settlement, _) = QuakeFixture();
            FireQuake(map, settlement);
            int firstTick = Find.TickManager.TicksGame;
            Find.LetterStack.RemoveLetter(Find.LetterStack.LettersListForReading[0]);
            RunTicks(100);
            Pawn colonist = NewHuman("Ash");
            colonist.faction = settlement.faction;
            GenSpawn.Spawn(colonist, new IntVec3(5, 0, 20), map);
            colonist.health.Kill(null, null);

            IReadOnlyList<EventMark> marks = GodViewSnapshot.RecentEvents(10);

            Assert.True(marks.Count >= 2);
            Assert.Equal(new IntVec3(5, 0, 20), marks[0].Location.Cell);
            Assert.Equal(EventMarkSource.Letter, marks[0].Source);
            Assert.Contains(marks, m => m.Tick == firstTick && m.Source == EventMarkSource.Chronicle);
            for (int i = 1; i < marks.Count; i++) Assert.True(marks[i - 1].Tick >= marks[i].Tick);
            Assert.Single(GodViewSnapshot.RecentEvents(1));
            Assert.Empty(GodViewSnapshot.RecentEvents(0));
        }

        [Fact]
        public void Event_locations_survive_a_save_of_the_letters_and_the_chronicle()
        {
            (CoreMap map, Settlement settlement, _) = QuakeFixture();
            Find.Storyteller.def = DefDatabase<StorytellerDef>.GetNamed("Cassandra_Classic");
            Find.Storyteller.difficulty = DefDatabase<DifficultyDef>.GetNamed("Medium");
            FireQuake(map, settlement);
            EventLocation saved = Find.LetterStack.LettersListForReading[0].location!;

            LetterStack letters = Scribe.Load<LetterStack>(
                Scribe.SaveToString(Find.LetterStack, "letters"), "letters", out IReadOnlyList<string> letterErrors);
            var storyteller = Scribe.Load<global::SimWorld.Director.Storyteller>(
                Scribe.SaveToString(Find.Storyteller, "storyteller"), "storyteller", out IReadOnlyList<string> storyErrors);

            Assert.Empty(letterErrors);
            Assert.Empty(storyErrors);
            foreach (EventLocation? loaded in new[] { letters.LettersListForReading[0].location, storyteller.Chronicle[0].location })
            {
                Assert.NotNull(loaded);
                Assert.Equal(saved.tile, loaded!.tile);
                Assert.Equal(saved.cell, loaded.cell);
                Assert.Equal(saved.affectedCells, loaded.affectedCells);
            }
        }

        // ---- 4. labels, not developer strings ----

        [Fact]
        public void An_incident_chronicle_line_reads_as_its_label()
        {
            var entry = new ChronicleEntry(10, "WandererJoin", global::SimWorld.Director.Storyteller.CivilizationTargetLabel, 0f);
            Assert.Equal("Wanderer joins", ChronicleLine.Of(entry).Text);
            Assert.Equal("WandererJoin", ChronicleLine.Of(entry).IncidentDefName);

            var death = new ChronicleEntry(10, "Death", "Ash", 0f) { deathCause = DeathCause.Unknown };
            Assert.Equal("Death: Ash", ChronicleLine.Of(death).Text);
        }

        [Fact]
        public void An_animals_death_sends_no_letter_and_a_colonists_says_where()
        {
            ResetDirector();
            var map = new CoreMap(20, 20, TerrainDefOf.Soil);
            var ours = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Settlers", "F_Settlers");
            var dog = new Pawn(Husky, "husky3") { faction = ours };
            GenSpawn.Spawn(dog, new IntVec3(3, 0, 3), map);
            dog.health.Kill(null, null);
            Assert.Empty(Find.LetterStack.LettersListForReading);

            Pawn colonist = NewHuman("Ash");
            colonist.faction = ours;
            GenSpawn.Spawn(colonist, new IntVec3(9, 0, 4), map);
            colonist.health.Kill(null, null);
            LetterView letter = Assert.Single(GodViewSnapshot.Capture().Letters);
            Assert.Equal(new IntVec3(9, 0, 4), letter.Location!.Cell);
        }

        [Fact]
        public void The_game_start_letter_has_a_title_and_the_civilization_a_proper_name()
        {
            Game game = NewSoloGame("playtest-labels");

            Assert.DoesNotContain(Find.LetterStack.LettersListForReading, l => l.label == "game start dialog");
            Assert.Contains(Find.LetterStack.LettersListForReading, l => l.label == ScenPart_GameStartDialog.DefaultTitle);

            Faction player = Find.FactionManager.AllFactionsListForReading.First(f => f.def.isPlayer);
            string[] words = player.name.Split(' ');
            Assert.All(words, w => Assert.True(char.IsUpper(w[0]), "'" + player.name + "' is not capitalised"));
            Assert.DoesNotContain("Fort", player.name);
            _ = game;
        }

        [Theory]
        [InlineData(1, "1 colonist idle")]
        [InlineData(3, "3 colonists idle")]
        public void The_idle_alert_counts_in_words(int idle, string expected)
        {
            var map = new CoreMap(10, 10, TerrainDefOf.Soil);
            var pawns = new List<Thing>();
            for (int i = 0; i < idle; i++) pawns.Add(NewHuman("Idle" + i));
            var context = new AlertContext(map, new List<Pawn>(), 0);

            Assert.Equal(expected, new Alert_ColonistsIdle().GetLabel(context, AlertReport.CulpritsAre(pawns)));
        }
    }
}
