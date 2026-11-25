using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Things;
using SimWorld.Needs;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Thoughts;
using SimWorld.Work;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// The era-progression edicts (docs/design/era-progression.md section 3) do what their text says, on a real
    /// generated settlement with its own band, think trees and stores: the same harness as
    /// <see cref="EdictIdlenessTests"/>. Each is compared with the same seed and no edict, so what is measured is
    /// the edict's own doing.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EdictProgressionMapTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public EdictProgressionMapTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        private static readonly HashSet<string> Joy = new HashSet<string> { "Skygaze", "GoForWalk", "SocialRelax" };
        private static readonly HashSet<string> NotWork = new HashSet<string>
        {
            "none", "Wait", "Wait_Wander", "GotoWander", "Wander", "Ingest", "LayDown", "Skygaze", "GoForWalk", "SocialRelax",
        };

        private static EdictDef Edict(string name) => DefDatabase<EdictDef>.GetNamed(name);

        private static Settlement Opened(string scenario, string seed)
        {
            Game game = Game.NewGame(DefDatabase<ScenarioDef>.GetNamed(scenario).scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        private static List<Pawn> Citizens(Settlement settlement) =>
            settlement.Citizens.Where(p => p.Spawned && p.Map == settlement.InteriorMap).ToList();

        private static Dictionary<string, int> Sample(List<Pawn> citizens, int ticks)
        {
            var jobs = new Dictionary<string, int>();
            for (int i = 0; i < ticks; i++)
            {
                Find.TickManager.DoSingleTick();
                if (i % 250 != 0) continue;
                foreach (Pawn p in citizens)
                {
                    if (p.Dead || !p.Spawned) continue;
                    string k = p.jobs.curJob?.def.defName ?? "none";
                    jobs[k] = jobs.GetValueOrDefault(k) + 1;
                }
            }
            return jobs;
        }

        private static string Show(Dictionary<string, int> jobs) =>
            string.Join(", ", jobs.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "=" + kv.Value));

        private static int Count(Dictionary<string, int> jobs, System.Func<string, bool> which) =>
            jobs.Where(kv => which(kv.Key)).Sum(kv => kv.Value);

        private static void Issue(string edict)
        {
            GodCommandResult issued = GodCommands.IssueEdict(edict);
            Assert.True(issued.Outcome == GodCommandOutcome.Done,
                edict + " in " + Find.ResearchManager.CurrentEra?.defName + ": " + issued.Reason);
        }

        /// <summary>The settlement's job samples over a window, after the jobs already running have finished.</summary>
        private Dictionary<string, int> Window(string scenario, string seed, string? edict, System.Action<Settlement>? prepare = null)
        {
            Settlement settlement = Opened(scenario, seed);
            prepare?.Invoke(settlement);
            List<Pawn> citizens = Citizens(settlement);
            if (edict != null) Issue(edict);
            Sample(citizens, 1000);
            Dictionary<string, int> jobs = Sample(citizens, 4000);
            output.WriteLine((edict ?? "no edict") + " " + scenario + "/" + seed + ": " + Show(jobs));
            return jobs;
        }

        // ---- the era a start puts the civilization in ----

        [Fact]
        public void A_tribal_start_is_in_the_first_era_and_issues_its_edicts_and_not_the_farmers()
        {
            Opened("TribalStart", "era-start-tribal");
            Assert.Same(DefDatabase<EraDef>.GetNamed("SticksAndStones"), Find.ResearchManager.CurrentEra);
            Assert.True(Find.God.CanActivate(Edict("RaiseTheRoofs")));
            Assert.True(Find.God.CanActivate(Edict("HuntersMandate")));
            Assert.False(Find.God.CanActivate(Edict("BreakTheSod")));
            Assert.False(Find.God.CanActivate(Edict("LeanRations")));
        }

        [Fact]
        public void An_agrarian_village_is_in_the_Agrarian_age_and_issues_its_edicts_with_no_setup()
        {
            // The second playtest's village could not issue Break the sod until it had researched farming: the
            // ladder counted the last era finished. A village founded in the Agrarian age is in it.
            Opened("AgrarianVillage", "era-start-village");
            Assert.Same(DefDatabase<EraDef>.GetNamed("Agrarian"), Find.ResearchManager.CurrentEra);
            Assert.Same(DefDatabase<EraDef>.GetNamed("SticksAndStones"), Find.ResearchManager.MasteredEra);
            Issue("BreakTheSod");
            Issue("LeanRations");
            Assert.True(Find.God.CanActivate(Edict("RaiseTheRoofs"))); // a young village still raises its roofs
            Assert.False(Find.God.CanActivate(Edict("PatronageOfLearning")));
        }

        // ---- lean rations ----

        [Fact]
        public void Under_lean_rations_a_hungry_adult_waits_for_real_hunger_before_eating()
        {
            // One adult set just hungry (above urgent): with no edict they go and eat; under lean rations they do
            // not, and their need keeps falling, at RimWorld's hungry rate, for the two hours watched.
            (float before, float after, bool ate) Watch(bool rationed)
            {
                Settlement settlement = Opened("AgrarianVillage", "lean-rations");
                List<Pawn> citizens = Citizens(settlement);
                if (rationed) Issue("LeanRations");
                Sample(citizens, 1000);
                Pawn adult = citizens.First(p => !p.Downed && Edict("LeanRations").Worker.AppliesTo(p));
                var food = adult.needs.food!;
                food.CurLevelPercentage = (food.PercentageThreshHungry + food.PercentageThreshUrgentlyHungry) / 2f + 0.02f;
                Assert.Equal(HungerCategory.Hungry, food.CurCategory);
                float start = food.CurLevelPercentage;
                bool ate = false;
                for (int i = 0; i < GenDate.TicksPerHour * 2; i++)
                {
                    Find.TickManager.DoSingleTick();
                    if (adult.jobs.curJob?.def == JobDefOf.Ingest) ate = true;
                }
                output.WriteLine((rationed ? "rationed" : "free") + ": " + start.ToString("0.000") + " -> " + food.CurLevelPercentage.ToString("0.000") + ", ate " + ate);
                return (start, food.CurLevelPercentage, ate);
            }

            var free = Watch(rationed: false);
            var rationed = Watch(rationed: true);
            Assert.True(free.ate && free.after > free.before, "with no edict a hungry adult should go and eat");
            Assert.False(rationed.ate, "under lean rations a merely hungry adult should not eat");
            Assert.True(rationed.after < rationed.before);
        }

        // ---- the harvest feast ----

        [Fact]
        public void A_harvest_feast_stops_routine_work_for_recreation_and_is_remembered_when_it_ends()
        {
            Dictionary<string, int> ordinary = Window("AgrarianVillage", "harvest-feast", null);
            Dictionary<string, int> feast = Window("AgrarianVillage", "harvest-feast", "HarvestFeast");

            int workOrdinary = Count(ordinary, k => !NotWork.Contains(k));
            int workFeast = Count(feast, k => !NotWork.Contains(k));
            int joyOrdinary = Count(ordinary, Joy.Contains);
            int joyFeast = Count(feast, Joy.Contains);
            Assert.True(workFeast * 2 <= workOrdinary, $"work samples {workFeast} on the feast day vs {workOrdinary} on an ordinary one");
            Assert.True(joyFeast > joyOrdinary * 2 && joyFeast * 4 >= feast.Values.Sum(),
                $"recreation samples {joyFeast} of {feast.Values.Sum()} on the feast day vs {joyOrdinary} on an ordinary one");

            // The day runs its course: the edict lapses on its own, and every citizen remembers it.
            EdictDef harvestFeast = Edict("HarvestFeast");
            Assert.True(Find.God.IsActive(harvestFeast));
            int lapseAt = Find.TickManager.TicksGame + Find.God.TicksUntilLapse(harvestFeast)!.Value;
            Find.TickManager.DebugSetTicksGame(lapseAt);
            Find.God.LapseEdictsThatRanTheirCourse(lapseAt);
            Assert.False(Find.God.IsIssued(harvestFeast));
            Assert.Contains(Find.Storyteller.Chronicle, e => e.incidentDefName == "Edict ran its course: " + harvestFeast.LabelCap + ".");
            Settlement settlement = Find.World!.worldObjects.OfType<Settlement>().First(s => s.InteriorMap != null);
            List<Pawn> living = settlement.Citizens.Where(p => !p.Dead).ToList();
            Assert.All(living, p => Assert.Contains(p.needs.mood!.thoughts.memories.Memories, m => m.def == harvestFeast.completedThought));

            // And it cannot be held again until its rest has run out.
            GodCommandResult again = GodCommands.IssueEdict("HarvestFeast");
            Assert.NotEqual(GodCommandOutcome.Done, again.Outcome);
            EdictOption option = GodViewSnapshot.Capture().Edicts.Single(o => o.DefName == "HarvestFeast");
            Assert.Equal(EdictAvailability.Cooldown, option.Availability);
            Assert.Equal(harvestFeast.cooldownDays, option.CooldownDaysLeft, 1);
        }

        [Fact]
        public void On_a_feast_day_even_the_content_do_no_routine_work()
        {
            // The recreation tier takes anyone below the feast's joy target first; this is the other half, the
            // citizen already content: routine work is closed to them too, so they keep to their leisure.
            static void Content(Settlement settlement)
            {
                foreach (Pawn p in Citizens(settlement))
                {
                    if (p.needs?.joy != null) p.needs.joy.CurLevel = p.needs.joy.MaxLevel;
                }
            }

            Dictionary<string, int> ordinary = Window("AgrarianVillage", "feast-content", null, Content);
            Dictionary<string, int> feast = Window("AgrarianVillage", "feast-content", "HarvestFeast", Content);
            int workOrdinary = Count(ordinary, k => !NotWork.Contains(k));
            int workFeast = Count(feast, k => !NotWork.Contains(k));
            Assert.True(workFeast * 4 <= workOrdinary, $"work samples {workFeast} on the feast day vs {workOrdinary} on an ordinary one");
        }

        // ---- the work pushes ----

        [Fact]
        public void Raise_the_roofs_puts_the_band_to_building()
        {
            // A band that has put building last (every citizen's Construction at the lowest priority) and has
            // walls laid out: the edict puts building before everything else, whatever each citizen prefers.
            static bool Building(string k) => k == "ConstructFinishFrame" || k == "HaulToBuildingSite" || k == "PlaceNoCostFrame";
            Dictionary<string, int> ordinary = Window("TribalStart", "raise-the-roofs", null, Plan);
            Dictionary<string, int> raised = Window("TribalStart", "raise-the-roofs", "RaiseTheRoofs", Plan);
            int before = Count(ordinary, Building), after = Count(raised, Building);
            // Measured on seed raise-the-roofs: 229 building samples against 172, and the hunt halved (112 to 60).
            // Pinned as a band, not the numbers: a fifth more building, paid for out of the hunt.
            Assert.True(after * 5 >= before * 6, $"building samples {after} under the edict vs {before} without");
            Assert.True(raised.GetValueOrDefault("Hunt") < ordinary.GetValueOrDefault("Hunt"), "the hands it takes come from other work");
        }

        [Fact]
        public void Patronage_of_learning_keeps_the_benches_staffed()
        {
            static void InClassical(Settlement settlement)
            {
                Benches(settlement, 3);
                EraDef classical = DefDatabase<EraDef>.GetNamed("Classical");
                foreach (ResearchProjectDef p in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
                {
                    if (p.era != null && p.era.order < classical.order) Find.ResearchManager.SetProjectFinishedForSetup(p);
                }
                Assert.Same(classical, Find.ResearchManager.CurrentEra);
            }

            Dictionary<string, int> ordinary = Window("AgrarianVillage", "patronage", null, InClassical);
            float progressOrdinary = Find.ResearchManager.CurrentProj == null ? 0f : Find.ResearchManager.GetProgress(Find.ResearchManager.CurrentProj);
            Dictionary<string, int> patronised = Window("AgrarianVillage", "patronage", "PatronageOfLearning", InClassical);
            int before = ordinary.GetValueOrDefault("Research"), after = patronised.GetValueOrDefault("Research");
            output.WriteLine("progress without " + progressOrdinary);
            Assert.True(after > before + 10, $"research samples {after} under the edict vs {before} without");
        }

        /// <summary>Built research benches on clear ground near the band: a village founded in the Agrarian age
        /// has none, and the scenario's leap to Classical gives it no time to build them.</summary>
        private static void Benches(Settlement settlement, int count)
        {
            CoreMap map = settlement.InteriorMap!;
            ThingDef bench = DefDatabase<ThingDef>.GetNamed("ResearchBench");
            IntVec3 near = Citizens(settlement)[0].Position;
            int placed = 0;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(near, 25f, useCenter: false))
            {
                if (placed == count) break;
                if (!global::SimWorld.Building.GenConstruct.CanPlaceBlueprintAt(bench, cell, map, out _)) continue;
                if (GenRadial.RadialCellsAround(cell, 2.9f, useCenter: true).Any(c => !GenGrid.InBounds(c, map) || !GenGrid.Standable(c, map))) continue;
                GenSpawn.Spawn(ThingMaker.MakeThing(bench, GenStuff.DefaultStuffFor(bench)), cell, map);
                placed++;
            }
            Assert.Equal(count, placed);
        }

        /// <summary>Walls laid out on clear ground near the band, more than the band can build in the window, so
        /// building is never short of work and what is measured is who turns to it.</summary>
        private static void Plan(Settlement settlement)
        {
            CoreMap map = settlement.InteriorMap!;
            WorkTypeDef construction = DefDatabase<WorkTypeDef>.GetNamed("Construction");
            foreach (Pawn p in Citizens(settlement))
            {
                if (p.workSettings != null && p.workSettings.GetPriority(construction) > 0) p.workSettings.SetPriority(construction, 4);
            }
            ThingDef wall = DefDatabase<ThingDef>.AllDefsListForReading.First(d => d.defName == "Wall");
            // Timber to hand, so building waits on hands and not on the woodcutters.
            ThingDef wood = DefDatabase<ThingDef>.GetNamed("WoodLog");
            int stacks = 0;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(Citizens(settlement)[0].Position, 12f, useCenter: false))
            {
                if (stacks == 12) break;
                if (!GenGrid.Standable(cell, map) || map.thingGrid.ThingsListAt(cell).Count > 0) continue;
                Thing logs = ThingMaker.MakeThing(wood);
                logs.stackCount = wood.stackLimit;
                GenSpawn.Spawn(logs, cell, map);
                stacks++;
            }
            for (int n = 0; n < 30; n++)
            {
                Assert.True(global::SimWorld.Building.SettlementWorksInitiative.TryPlaceBlueprint(map, wall, Citizens(settlement)[0].Position, 20),
                    "no room for a wall blueprint");
            }
        }
    }
}
