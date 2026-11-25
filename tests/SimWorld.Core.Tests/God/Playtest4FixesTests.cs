using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.God.View;
using SimWorld.Letters;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Quests;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Thoughts;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// The fourth playtest's smaller findings, each on a real generated game: names reused after a load, a baby
    /// with a grown-up's covenant thought, "4 woolly ox", a quest offer that said nothing and came straight
    /// back, the era panel's "(95 of 10)", the earthquake's bare number, steel in a stone-age ruin, no way to
    /// see anyone's gear, and the research agenda working past the project the era was waiting on.
    /// </summary>
    [Collection("GlobalDefs")]
    public class Playtest4FixesTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public Playtest4FixesTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        private static Game NewTribal(string seed) =>
            Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);

        private static (Settlement settlement, CoreMap map, List<Pawn> citizens) Opened(Game game)
        {
            Settlement settlement = game.CivilizationTarget.Seat!;
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            CoreMap map = settlement.InteriorMap!;
            return (settlement, map, settlement.Citizens.Where(p => p.Spawned && p.Map == map).ToList());
        }

        private Game Reload(Game game)
        {
            string xml = Scribe.SaveToString(game, "game");
            Game loaded = Scribe.Load<Game>(xml, "game", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);
            return loaded;
        }

        // ---- names ----

        [Fact]
        public void A_name_given_before_a_save_is_still_taken_after_the_load()
        {
            Game game = NewTribal("names-after-load");
            Pawn founder = game.CivilizationTarget.Seat!.Citizens[0];
            var name = (NameTriple)founder.Name!;
            const string Dead = "Rasmus Gonewick";
            NameUseChecker.AddName(Dead);

            string xml = Scribe.SaveToString(game, "game");
            NameUseChecker.Clear(); // a fresh process: nothing in memory but what the save restores
            Scribe.Load<Game>(xml, "game", out IReadOnlyList<string> errors, Content.Database);
            Assert.Empty(errors);

            Assert.True(NameUseChecker.NameWordIsUsed(name.ToStringFull), "a living citizen's name");
            Assert.True(NameUseChecker.NickIsUsed(name.nick), "a living citizen's nick");
            Assert.True(NameUseChecker.NameWordIsUsed(Dead), "a name given to someone no longer in the game");
        }

        // ---- thoughts by life phase ----

        [Fact]
        public void A_baby_has_no_grown_up_thoughts()
        {
            Game game = NewTribal("baby-thoughts");
            List<Pawn> band = game.CivilizationTarget.Seat!.Citizens.ToList();
            Pawn baby = band[0];
            Pawn adult = band[1];
            baby.ageTracker.DebugSetAge(0.3f);
            Assert.Equal(DevelopmentalStage.Baby, baby.ageTracker.CurLifeStage!.developmentalStage);

            ThoughtDef immodest = DefDatabase<ThoughtDef>.GetNamed("ImmodestExposure");
            Assert.True(ThoughtUtility.ThoughtNullified(baby, immodest), "a baby is not ashamed of being bare");
            Assert.False(ThoughtUtility.ThoughtNullified(adult, immodest) && adult.ageTracker.CurLifeStage!.developmentalStage == DevelopmentalStage.Adult
                && immodest.nullifyingTraits == null && immodest.nullifyingHediffs == null, "the filter is by life phase only");
            Assert.False(immodest.AllowsStage(DevelopmentalStage.Baby));
            Assert.True(immodest.AllowsStage(DevelopmentalStage.Adult));
        }

        // ---- plurals ----

        [Fact]
        public void A_pack_of_woolly_oxen_is_plural()
        {
            PawnKindDef ox = DefDatabase<PawnKindDef>.GetNamed("Muffalo");
            Assert.Equal("woolly ox", ox.LabelFor(1));
            Assert.Equal("woolly oxen", ox.LabelFor(4));
            Assert.Equal("huskies", DefDatabase<PawnKindDef>.GetNamed("Husky").LabelFor(3));
            Assert.Equal("chickens", DefDatabase<PawnKindDef>.GetNamed("Chicken").LabelFor(2));
        }

        // ---- the quest offer ----

        private static (Quest quest, ChoiceLetter letter) OfferLostCaravan()
        {
            QuestScriptDef caravan = DefDatabase<QuestScriptDef>.GetNamed("Quest_LostCaravan");
            foreach (QuestScriptDef other in DefDatabase<QuestScriptDef>.AllDefsListForReading)
            {
                if (other != caravan) Find.QuestManager.Notify_QuestScriptFired(other, Find.TickManager.TicksGame);
            }
            IncidentDef give = DefDatabase<IncidentDef>.GetNamed("GiveQuest_Random");
            Assert.True(give.Worker.TryExecute(new IncidentParms { target = Find.CurrentGame!.CivilizationTarget, points = 100f }));
            Quest quest = Find.QuestManager.QuestsListForReading.Last();
            Assert.Same(caravan, quest.root);
            ChoiceLetter letter = Find.LetterStack.LettersListForReading.OfType<ChoiceLetter>().Last();
            return (quest, letter);
        }

        [Fact]
        public void A_quest_offer_says_what_it_pays_and_risks_and_a_refusal_keeps_it_away()
        {
            Game game = NewTribal("quest-offer");
            (Quest quest, ChoiceLetter letter) = OfferLostCaravan();
            output.WriteLine(letter.text);
            Assert.Contains("Reward: 200 silver and 5 goodwill with the ", letter.text);
            Assert.Contains("Risk: ", letter.text);
            Assert.DoesNotContain("A quest is available", letter.text);

            Assert.Equal(GodCommandOutcome.Done, GodCommands.ResolveLetterChoice(letter.letterID, 1).Outcome);
            Assert.Equal(QuestState.EndedOfferExpired, quest.State);

            QuestScriptDef caravan = quest.root;
            int now = Find.TickManager.TicksGame;
            int refire = (int)(caravan.minRefireDays * GenDate.TicksPerDay);
            Assert.False(Find.QuestManager.CanFire(caravan, now + refire + 1), "a turned-down offer is back as soon as its refire spacing ends");
            Assert.True(Find.QuestManager.CanFire(caravan, now + (int)(caravan.declinedRefireDays * GenDate.TicksPerDay) + 1));

            Game loaded = Reload(game);
            Assert.False(loaded.QuestManager.CanFire(caravan, now + refire + 1), "the refusal is forgotten across a save");
        }

        // ---- the drought letter ----

        [Fact]
        public void The_drought_letter_reads_without_watched_or_not()
        {
            Game game = NewTribal("drought-words");
            IncidentDef drought = DefDatabase<IncidentDef>.GetNamed("Drought");
            Assert.True(drought.Worker.TryExecute(new IncidentParms { target = game.CivilizationTarget }));
            Letter letter = Find.LetterStack.LettersListForReading.Last();
            output.WriteLine(letter.text);
            Assert.DoesNotContain("watched or not", letter.text);
            Assert.Contains("% of normal", letter.text);
        }

        // ---- the era panel's food line ----

        [Fact]
        public void A_met_food_condition_reads_as_met_not_as_95_of_10()
        {
            var line = new EraConditionLine("Food in store for 10 days", 95f, 10f, true, EraConditionUnit.Days);
            Assert.Equal(10f, line.Current);
            Assert.Equal(95f, line.Reading);
            Assert.Equal("Food in store for 10 days: 95 days' worth held", line.Text);
            var short_ = new EraConditionLine("Food in store for 10 days", 4.5f, 10f, false, EraConditionUnit.Days);
            Assert.Equal(4.5f, short_.Current);
        }

        // ---- the earthquake's toll ----

        [Fact]
        public void The_earthquake_letter_names_what_fell_and_who_was_hurt()
        {
            Game game = NewTribal("quake-toll");
            (Settlement settlement, CoreMap map, List<Pawn> citizens) = Opened(game);
            ThingDef wall = DefDatabase<ThingDef>.GetNamed("Wall");
            int placed = 0;
            foreach (IntVec3 c in map.AllCells.Where(c => c.x % 3 == 0 && c.z % 3 == 0 && GenGrid.Standable(c, map) && map.thingGrid.ThingsListAt(c).Count == 0).Take(30))
            {
                Thing w = ThingMaker.MakeThing(wall, DefDatabase<ThingDef>.GetNamed("WoodLog"));
                w.SetFaction(citizens[0].Faction);
                GenSpawn.Spawn(w, c, map);
                placed++;
            }
            Assert.True(DefDatabase<IncidentDef>.GetNamed("Earthquake").Worker.TryExecute(new IncidentParms { target = game.CivilizationTarget }));
            Letter letter = Find.LetterStack.LettersListForReading.Last(l => l.label.StartsWith("Earthquake"));
            output.WriteLine(letter.text);
            Assert.Matches(@"\d+ structures? comes? down: .*walls?", letter.text);
            Assert.Matches(@"(No one was hurt\.|hurt|killed)", letter.text);
        }

        // ---- ruins of the age ----

        [Fact]
        public void A_stone_age_map_has_no_steel_in_its_ruins()
        {
            Game game = NewTribal("simworld-host");
            (Settlement _, CoreMap map, List<Pawn> _) = Opened(game);
            ThingDef steel = DefDatabase<ThingDef>.GetNamed("Steel");
            Assert.NotNull(EraAvailability.EraOf(steel));
            Assert.Empty(map.listerThings.ThingsOfDef(steel));
            Assert.DoesNotContain(map.listerThings.AllThings, t => t.def.category == ThingCategory.Building && t.Stuff == steel);
        }

        // ---- gear ----

        [Fact]
        public void A_citizen_view_lists_their_gear_with_what_it_is_made_of()
        {
            Game game = NewTribal("citizen-gear");
            (Settlement settlement, CoreMap map, List<Pawn> citizens) = Opened(game);
            Pawn armed = citizens.First(p => p.equipment?.Primary != null);
            CitizenView view = GodViewSnapshot.Citizen(armed.thingIDNumber)!;
            Assert.NotEmpty(view.Gear);
            SimWorld.Map.View.GearView weapon = view.Gear[0];
            output.WriteLine(string.Join("; ", view.Gear.Select(g => $"{g.Slot} {g.DefName} '{g.Label}' {g.StuffDefName} {g.EraDefName} {g.HitPointsFraction:0.00}")));
            Assert.Equal(SimWorld.Map.View.GearSlot.Weapon, weapon.Slot);
            Assert.Equal(armed.equipment!.Primary!.def.defName, weapon.DefName);
            if (armed.equipment.Primary.Stuff != null) Assert.StartsWith(armed.equipment.Primary.Stuff.label!, weapon.Label);
            Assert.Equal(armed.apparel!.WornApparel.Count, view.Gear.Count(g => g.Slot == SimWorld.Map.View.GearSlot.Apparel));
        }

        // ---- research toward the era gate ----

        [Fact]
        public void The_agenda_works_the_era_gate_before_cheaper_side_projects()
        {
            NewTribal("agenda-gate");
            int steps = 0, offGateCheaper = 0;
            for (; steps < 60; steps++)
            {
                EraDef? frontier = EraPacing.FrontierEra();
                HashSet<ResearchProjectDef> gate = ResearchAgenda.GateWork(frontier);
                ResearchProjectDef? next = ResearchAgenda.NextProject();
                if (next == null) break;
                List<ResearchProjectDef> startable = Find.ResearchManager.AllProjects
                    .Where(p => p.CanStartNow && !EraPacing.IsPastFrontier(p, frontier)).ToList();
                bool kitLeft = startable.Any(p => ResearchAgenda.Tier(p, frontier, gate) == 0);
                if (!kitLeft && startable.Any(gate.Contains))
                {
                    Assert.True(gate.Contains(next), $"step {steps}: {next.defName} picked while gate work {string.Join(", ", startable.Where(gate.Contains).Select(p => p.defName))} could start");
                    if (startable.Any(p => !gate.Contains(p) && p.baseCost < next.baseCost)) offGateCheaper++;
                }
                Find.ResearchManager.FinishProject(next);
            }
            output.WriteLine($"{steps} projects finished; {offGateCheaper} times a cheaper side project waited for gate work");
            Assert.True(offGateCheaper > 0, "fixture: at some step a side project was cheaper than the gate work picked");
        }
    }
}
