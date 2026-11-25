using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Research
{
    /// <summary>
    /// The era model (docs/design/era-progression.md section 1): <see cref="ResearchManager.CurrentEra"/> is the era
    /// the civilization is in, kept as state; it leaves it when the era's research is learned and the town meets
    /// the era's conditions, and never goes back. Edicts with timers (section 3) are here too, without a map.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EraGateTests : ContentTestBase
    {
        public EraGateTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static EraDef Era(string name) => DefDatabase<EraDef>.GetNamed(name);

        private static EdictDef Edict(string name) => DefDatabase<EdictDef>.GetNamed(name);

        private static Settlement OpenedTribe(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        // ---- conditions ----

        [Fact]
        public void An_era_learned_waits_for_its_conditions_and_turns_on_the_god_tick_once_they_hold()
        {
            Settlement settlement = OpenedTribe("era-conditions");
            EraDef first = Era("SticksAndStones");
            List<EraCondition> shipped = first.conditions;
            int people = settlement.Citizens.Count(p => !p.Dead);
            var population = new EraCondition_Population { count = people + 5 };
            first.conditions = new List<EraCondition> { population };
            try
            {
                var reached = new List<EraDef>();
                Find.ResearchManager.EraReached += (from, to) => reached.Add(to);
                foreach (ResearchProjectDef p in first.Projects) Find.ResearchManager.FinishProject(p);

                // Learned, but the town is five short: it stays where it is, and the view says what it waits on.
                Assert.True(first.IsComplete);
                Assert.Same(first, Find.ResearchManager.CurrentEra);
                Assert.Empty(reached);
                EraOutlook outlook = GodViewSnapshot.Capture().Era;
                Assert.False(outlook.ConditionsMet);
                EraConditionLine line = Assert.Single(outlook.Conditions);
                Assert.False(line.IsMet);
                Assert.Equal(people, line.Current);
                Assert.Equal(people + 5, line.Target);
                Assert.True(outlook.Progress < 1f);

                // The town meets it: the next god tick turns the era, with no project finishing to prompt it.
                population.count = people;
                for (int i = 0; i <= GodTuning.GodTickIntervalTicks; i++) Find.TickManager.DoSingleTick();

                Assert.Same(Era("Agrarian"), Find.ResearchManager.CurrentEra);
                Assert.Equal(new[] { Era("Agrarian") }, reached);
                Assert.Same(Era("Agrarian").defName, GodViewSnapshot.Capture().Era.CurrentEraDefName);
            }
            finally
            {
                first.conditions = shipped;
            }
        }

        [Fact]
        public void An_era_once_entered_is_not_left_when_its_conditions_lapse()
        {
            // Conditions are absolute and can fall back (a house burns, a famine): the era is kept as state for
            // exactly that reason, so a civilization does not slide down the ladder.
            Settlement settlement = OpenedTribe("era-no-regress");
            foreach (ResearchProjectDef p in Era("SticksAndStones").Projects) Find.ResearchManager.SetProjectFinishedForSetup(p);
            Assert.Same(Era("Agrarian"), Find.ResearchManager.CurrentEra);

            EraDef first = Era("SticksAndStones");
            List<EraCondition> shipped = first.conditions;
            first.conditions = new List<EraCondition> { new EraCondition_Population { count = 1000 } };
            try
            {
                Assert.False(first.GateOpen(EraConditionContext.Now()));
                for (int i = 0; i <= GodTuning.GodTickIntervalTicks; i++) Find.TickManager.DoSingleTick();
                Assert.Same(Era("Agrarian"), Find.ResearchManager.CurrentEra);
            }
            finally
            {
                first.conditions = shipped;
            }
            _ = settlement;
        }

        [Fact]
        public void Every_era_but_the_last_names_a_condition_beside_its_research()
        {
            List<EraDef> eras = DefDatabase<EraDef>.AllDefsListForReading.OrderBy(e => e.order).ToList();
            foreach (EraDef era in eras.Take(eras.Count - 1))
            {
                Assert.NotEmpty(era.conditions);
                Assert.All(era.conditions, c => Assert.False(string.IsNullOrWhiteSpace(c.Label)));
                Assert.Contains(EraUnlocks.Describe(era), l => l.StartsWith("To leave it"));
            }
        }

        // ---- save/load ----

        [Fact]
        public void The_era_a_civilization_is_in_round_trips_and_an_old_save_derives_it()
        {
            var manager = new ResearchManager();
            Find.ResearchManager = manager;
            foreach (ResearchProjectDef p in Era("SticksAndStones").Projects) manager.FinishProject(p);
            Assert.Same(Era("Agrarian"), manager.CurrentEra);

            string xml = Scribe.SaveToString(manager, "research");
            Assert.Contains("<currentEra>Agrarian</currentEra>", xml);
            ResearchManager loaded = Scribe.Load<ResearchManager>(xml, "research", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            Assert.Same(Era("Agrarian"), loaded.CurrentEra);

            // A save from before the era was kept has no such element: the era is derived from its research.
            string legacy = System.Text.RegularExpressions.Regex.Replace(xml, @"\s*<currentEra>[^<]*</currentEra>", "");
            Assert.DoesNotContain("currentEra", legacy);
            ResearchManager old = Scribe.Load<ResearchManager>(legacy, "research", out IReadOnlyList<string> legacyErrors);
            Assert.Empty(legacyErrors);
            Assert.Same(Era("Agrarian"), old.CurrentEra);
        }

        // ---- edict timers ----

        [Fact]
        public void An_edict_with_a_span_lapses_on_its_own_and_then_rests_before_it_can_be_issued_again()
        {
            Find.God = new GodManager();
            EdictDef roofs = Edict("RaiseTheRoofs");
            Assert.True(Find.God.Activate(roofs));
            Assert.Equal((int)(roofs.durationDays * GenDate.TicksPerDay), Find.God.TicksUntilLapse(roofs));

            EdictOption issued = GodViewSnapshot.Capture().Edicts.Single(o => o.DefName == roofs.defName);
            Assert.Equal(roofs.durationDays, issued.DaysRemaining!.Value, 2);
            Assert.Contains("Lasts 5 days.", issued.Effects);
            Assert.Contains(issued.Effects, l => l.StartsWith("Mood -"));

            // A tick short of its span it stands; on the span it lapses, with its own line in the chronicle.
            int span = (int)(roofs.durationDays * GenDate.TicksPerDay);
            Find.TickManager.DebugSetTicksGame(span - 1);
            Find.God.LapseEdictsThatRanTheirCourse(span - 1);
            Assert.True(Find.God.IsActive(roofs));
            Find.TickManager.DebugSetTicksGame(span);
            Find.God.LapseEdictsThatRanTheirCourse(span);
            Assert.False(Find.God.IsIssued(roofs));
            Assert.Contains(Find.Storyteller.Chronicle, e => e.incidentDefName == "Edict ran its course: " + roofs.LabelCap + ".");

            // Resting: refused, and the view says for how long.
            int rest = (int)(roofs.cooldownDays * GenDate.TicksPerDay);
            Assert.Equal(rest, Find.God.CooldownTicksLeft(roofs));
            Assert.False(Find.God.CanActivate(roofs));
            EdictOption resting = GodViewSnapshot.Capture().Edicts.Single(o => o.DefName == roofs.defName);
            Assert.Equal(EdictAvailability.Cooldown, resting.Availability);
            Assert.Equal(roofs.cooldownDays, resting.CooldownDaysLeft, 2);
            Assert.Contains("again", resting.Reason);

            Find.TickManager.DebugSetTicksGame(span + rest);
            Assert.Equal(0, Find.God.CooldownTicksLeft(roofs));
            Assert.True(Find.God.CanActivate(roofs));
        }

        [Fact]
        public void Rescinding_an_edict_early_still_costs_its_rest()
        {
            Find.God = new GodManager();
            EdictDef hunt = Edict("HuntersMandate");
            Assert.True(Find.God.Activate(hunt));
            Assert.True(Find.God.Deactivate(hunt));
            Assert.Equal((int)(hunt.cooldownDays * GenDate.TicksPerDay), Find.God.CooldownTicksLeft(hunt));
            Assert.False(Find.God.CanActivate(hunt));
        }

        [Fact]
        public void A_standing_edict_never_lapses_and_has_no_rest()
        {
            Find.God = new GodManager();
            Find.ResearchManager = new ResearchManager();
            foreach (ResearchProjectDef p in Era("SticksAndStones").Projects) Find.ResearchManager.SetProjectFinishedForSetup(p);
            EdictDef lean = Edict("LeanRations");
            Assert.True(Find.God.Activate(lean));
            Assert.Null(Find.God.TicksUntilLapse(lean));
            Find.God.LapseEdictsThatRanTheirCourse(GenDate.TicksPerDay * 1000);
            Assert.True(Find.God.IsActive(lean));
            Assert.True(Find.God.Deactivate(lean));
            Assert.True(Find.God.CanActivate(lean));
        }

        [Fact]
        public void The_edict_timers_round_trip()
        {
            var god = new GodManager();
            Find.God = god;
            EdictDef roofs = Edict("RaiseTheRoofs");
            EdictDef hunt = Edict("HuntersMandate");
            Find.TickManager.DebugSetTicksGame(1000);
            Assert.True(god.Activate(roofs));
            Assert.True(god.Activate(hunt));
            Assert.True(god.Deactivate(hunt));
            int? lapse = god.TicksUntilLapse(roofs);
            int rest = god.CooldownTicksLeft(hunt);
            Assert.True(rest > 0);

            string xml = Scribe.SaveToString(god, "god");
            GodManager loaded = Scribe.Load<GodManager>(xml, "god", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            Assert.Equal(lapse, loaded.TicksUntilLapse(roofs));
            Assert.Equal(rest, loaded.CooldownTicksLeft(hunt));
        }

        [Fact]
        public void Every_edict_states_its_effects_and_every_era_but_the_first_two_opens_or_keeps_one()
        {
            foreach (EdictDef edict in DefDatabase<EdictDef>.AllDefsListForReading)
            {
                EdictOption option = GodViewSnapshot.Capture().Edicts.Single(o => o.DefName == edict.defName);
                Assert.NotEmpty(option.Effects);
                Assert.False(string.IsNullOrWhiteSpace(option.Description), edict.defName);
            }
            // Every era up to Industrial has an edict of its own era to issue (an era with none is a dead lever).
            foreach (EraDef era in DefDatabase<EraDef>.AllDefsListForReading.Where(e => e.order <= Era("Industrial").order))
            {
                Assert.True(DefDatabase<EdictDef>.AllDefsListForReading.Any(e =>
                        (e.requiredEra == null || e.requiredEra.order <= era.order) && (e.obsoleteEra == null || e.obsoleteEra.order > era.order) &&
                        e.requiredEra != null),
                    era.defName + " has no era edict to issue");
            }
        }
    }
}
