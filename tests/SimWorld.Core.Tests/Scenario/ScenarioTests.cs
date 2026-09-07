using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Scenario
{
    public class ScenarioTests : ContentTestBase
    {
        public ScenarioTests(CoreContentFixture content) : base(content)
        {
            // See QuestTests' constructor note: these Find services aren't covered by Find.Reset().
            Find.LetterStack = new SimWorld.Letters.LetterStack();
            Find.ResearchManager = new ResearchManager();
        }

        private static ScenPartDef Part(string defName) => DefDatabase<ScenPartDef>.GetNamed(defName);

        // ---- content ----

        [Fact]
        public void Scenario_content_loads_with_expected_counts_and_defOfs()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.Equal(9, DefDatabase<ScenPartDef>.DefCount);
            Assert.Equal(3, DefDatabase<global::SimWorld.Scenario.ScenarioDef>.DefCount);

            Assert.NotNull(ScenarioDefOf.TribalStart);
            Assert.Equal("TribalStart", ScenarioDefOf.TribalStart.defName);

            global::SimWorld.Scenario.ScenarioDef landfall = DefDatabase<global::SimWorld.Scenario.ScenarioDef>.GetNamed("Landfall");
            Assert.True(landfall.isScenarioDefault);
            Assert.False(ScenarioDefOf.TribalStart.isScenarioDefault);
        }

        [Fact]
        public void TribalStart_summary_text_lists_parts()
        {
            global::SimWorld.Scenario.Scenario scenario = ScenarioDefOf.TribalStart.scenario;

            string text = scenario.GetFullInformationText().ToLowerInvariant();

            Assert.Contains("5 pawns", text);
            Assert.Contains("player civilization", text);
            Assert.Contains("sticks and stones era", text);
        }

        // ---- PostGameStart ----

        [Fact]
        public void PostGameStart_runs_starting_era_forced_trait_starting_thing_and_game_start_letter()
        {
            Pawn freshPawn = NewHuman("Fresh");
            Pawn alreadyKindPawn = NewHuman("AlreadyKind");
            alreadyKindPawn.story.traits.GainTrait(new Trait(Trait("Kind")));
            Pawn conflictingPawn = NewHuman("Conflicting");
            conflictingPawn.story.traits.GainTrait(new Trait(Trait("Psychopath")));

            var ctx = new ScenarioContext();
            ctx.StartingPawns.Add(freshPawn);
            ctx.StartingPawns.Add(alreadyKindPawn);
            ctx.StartingPawns.Add(conflictingPawn);

            var scenario = new global::SimWorld.Scenario.Scenario { name = "Test" };
            scenario.parts.Add(new ScenPart_StartingEra { def = Part("StartingEra"), era = DefDatabase<EraDef>.GetNamed("Bronze") });
            scenario.parts.Add(new ScenPart_ForcedTrait { def = Part("ForcedTrait"), trait = Trait("Kind"), chance = 1f });
            scenario.parts.Add(new ScenPart_StartingThing_Defined { def = Part("StartingThing_Defined"), thingDef = DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Knife"), count = 2 });
            scenario.parts.Add(new ScenPart_GameStartDialog { def = Part("GameStartDialog"), text = "Welcome to the tribe." });

            scenario.PostGameStart(ctx);

            // StartingEra: every project of every era before Bronze is finished; Bronze's own is not.
            Assert.True(DefDatabase<EraDef>.GetNamed("SticksAndStones").IsComplete);
            Assert.True(DefDatabase<EraDef>.GetNamed("Agrarian").IsComplete);
            Assert.False(DefDatabase<EraDef>.GetNamed("Bronze").IsComplete);
            Assert.Equal(TechLevel.Medieval, ctx.ResearchManager.ResearcherTechLevel);

            // ForcedTrait at chance 1: applies to a pawn without it, leaves one who already has it alone
            // (no duplicate, no exception), and skips a pawn with a conflicting trait.
            Assert.True(freshPawn.story.traits.HasTrait(Trait("Kind")));
            Assert.True(alreadyKindPawn.story.traits.HasTrait(Trait("Kind")));
            Assert.Single(alreadyKindPawn.story.traits.allTraits);
            Assert.False(conflictingPawn.story.traits.HasTrait(Trait("Kind")));

            // StartingThing_Defined: recorded, not spawned (no Things system in scope yet).
            StartingThingRecord recorded = Assert.Single(ctx.StartingThings);
            Assert.Same(DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Knife"), recorded.thingDef);
            Assert.Equal(2, recorded.count);

            // GameStartDialog: one letter landed on the stack.
            Assert.Single(Find.LetterStack.LettersListForReading);
        }

        [Fact]
        public void PostGameStart_sets_player_faction()
        {
            var ctx = new ScenarioContext();
            var part = new ScenPart_PlayerFaction { def = Part("PlayerFaction"), factionDef = DefDatabase<FactionDef>.GetNamed("PlayerCivilization") };

            part.PostGameStart(ctx);

            Assert.Same(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), ctx.PlayerFactionDef);
        }

        // ---- config errors ----

        [Fact]
        public void Scenario_config_errors_on_a_missing_def()
        {
            var scenario = new global::SimWorld.Scenario.Scenario { name = "Broken" };
            scenario.parts.Add(new ScenPart_PlayerFaction { def = Part("PlayerFaction") }); // factionDef left null

            List<string> errors = scenario.ConfigErrors().ToList();

            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Contains("factionDef"));
        }

        // ---- Scribe round trip ----

        [Fact]
        public void Scenario_Scribe_round_trip()
        {
            var scenario = new global::SimWorld.Scenario.Scenario { name = "Round Trip", summary = "S", description = "D" };
            scenario.parts.Add(new ScenPart_StartingEra { def = Part("StartingEra"), era = DefDatabase<EraDef>.GetNamed("Bronze") });
            scenario.parts.Add(new ScenPart_RivalCivilizations { def = Part("RivalCivilizations"), count = new IntRange(2, 5) });

            string xml = Scribe.SaveToString(scenario, "scenario");
            global::SimWorld.Scenario.Scenario loaded = Scribe.Load<global::SimWorld.Scenario.Scenario>(xml, "scenario", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal("Round Trip", loaded.name);
            Assert.Equal("S", loaded.summary);
            Assert.Equal(2, loaded.parts.Count);

            var loadedEra = Assert.IsType<ScenPart_StartingEra>(loaded.parts[0]);
            Assert.Same(DefDatabase<EraDef>.GetNamed("Bronze"), loadedEra.era);

            var loadedRivals = Assert.IsType<ScenPart_RivalCivilizations>(loaded.parts[1]);
            Assert.Equal(new IntRange(2, 5), loadedRivals.count);
        }

        // ---- ScenPart_RivalCivilizations / Scenario.Current ----

        [Fact]
        public void ScenPart_RivalCivilizations_exposes_count_via_scenario_and_current_hook()
        {
            var scenario = new global::SimWorld.Scenario.Scenario { name = "X" };
            scenario.parts.Add(new ScenPart_RivalCivilizations { def = Part("RivalCivilizations"), count = new IntRange(3, 6) });

            global::SimWorld.Scenario.Scenario.Current = scenario;
            try
            {
                Assert.Equal(new IntRange(3, 6), scenario.RivalCivilizationCount);
                Assert.Equal(new IntRange(3, 6), global::SimWorld.Scenario.Scenario.Current!.RivalCivilizationCount);
            }
            finally
            {
                global::SimWorld.Scenario.Scenario.Current = null;
            }
        }

        [Fact]
        public void Scenario_without_a_rival_civilizations_part_reports_zero()
        {
            var scenario = new global::SimWorld.Scenario.Scenario { name = "NoRivals" };
            Assert.Equal(IntRange.Zero, scenario.RivalCivilizationCount);
        }
    }
}
