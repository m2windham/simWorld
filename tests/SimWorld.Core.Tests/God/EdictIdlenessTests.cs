using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.God;
using SimWorld.God.View;
using SimWorld.Research;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.God
{
    /// <summary>
    /// An exclusive edict must not leave people idle. The second playtest issued the hunter's mandate in
    /// Sticks and stones and saw ten colonists idle, a bow holder "relaxing" and research frozen; Break the
    /// sod in Agrarian left everyone wandering. RimWorld's <c>JobGiver_Work</c> walks work givers in priority
    /// order and takes the first that yields a job: a priority is an order of preference, never a reason to
    /// stand still while lower work waits.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EdictIdlenessTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public EdictIdlenessTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        private static readonly HashSet<string> Idle = new HashSet<string> { "none", "Wait", "Wait_Wander", "GotoWander", "Wander" };

        private static Settlement Opened(string scenario, string seed)
        {
            Game game = Game.NewGame(DefDatabase<ScenarioDef>.GetNamed(scenario).scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

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

        private static int Count(Dictionary<string, int> jobs, System.Func<string, bool> which) => jobs.Where(kv => which(kv.Key)).Sum(kv => kv.Value);

        private static string Show(Dictionary<string, int> jobs) =>
            string.Join(", ", jobs.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + "=" + kv.Value));

        /// <summary>Idle samples among the settlement's citizens over a window, with or without an edict.</summary>
        private (int idle, int total, Dictionary<string, int> jobs) Run(string scenario, string seed, string? edict, System.Action<CoreMap>? prepare = null)
        {
            Settlement settlement = Opened(scenario, seed);
            CoreMap map = settlement.InteriorMap!;
            prepare?.Invoke(map);
            List<Pawn> citizens = settlement.Citizens.Where(p => p.Spawned && p.Map == map).ToList();
            if (edict != null)
            {
                var issued = GodCommands.IssueEdict(edict);
                Assert.True(issued.Outcome == GodCommandOutcome.Done, edict + " in " + Find.ResearchManager.CurrentEra?.defName + ": " + issued.Reason);
            }
            Sample(citizens, 1000);
            Dictionary<string, int> jobs = Sample(citizens, 4000);
            int idle = Count(jobs, k => Idle.Contains(k));
            int total = jobs.Values.Sum();
            output.WriteLine((edict ?? "no edict") + " " + scenario + "/" + seed + ": idle " + idle + "/" + total + " | " + Show(jobs));
            return (idle, total, jobs);
        }

        /// <summary>Break the sod needs the civilization in the Agrarian age, which is where an Agrarian village
        /// starts (<c>ResearchManager.CurrentEra</c> is the era it is in, docs/design/era-progression.md §1).</summary>
        private static void ReachAgrarian(CoreMap map)
        {
            _ = map;
            Assert.Same(DefDatabase<EraDef>.GetNamed("Agrarian"), Find.ResearchManager.CurrentEra);
        }

        /// <summary>Every wild animal gone: the hunt has nothing to find.</summary>
        private static void NoPrey(CoreMap map)
        {
            foreach (Pawn animal in map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn).OfType<Pawn>().Where(HuntUtility.IsHuntableAnimal).ToList())
            {
                animal.Destroy();
            }
        }

        private static System.Action<CoreMap>? Setup(string name) => name switch
        {
            "agrarian" => ReachAgrarian,
            "no-prey" => NoPrey,
            _ => null,
        };

        [Theory]
        [InlineData("TribalStart", "HuntersMandate", "mandate-idle", "")]
        [InlineData("TribalStart", "HuntersMandate", "mandate-no-prey", "no-prey")]
        [InlineData("AgrarianVillage", "BreakTheSod", "sod-idle", "agrarian")]
        public void An_exclusive_edict_leaves_no_more_people_idle_than_no_edict(string scenario, string edict, string seed, string setup)
        {
            var without = Run(scenario, seed, null, Setup(setup));
            var with = Run(scenario, seed, edict, Setup(setup));
            // Idleness is a share of samples; the edict may not raise it by more than a tenth of the band's time.
            double idleWithout = (double)without.idle / without.total;
            double idleWith = (double)with.idle / with.total;
            Assert.True(idleWith <= idleWithout + 0.10, $"idle {idleWith:P0} under {edict} vs {idleWithout:P0} without");
        }


        /// <summary>The second playtest's Agrarian colony starved beside full stores, downed colonists among
        /// them. Feeding a downed patient is a doctor's routine work, which an exclusive edict used to withhold
        /// from every able hand, and which the edict tier still sits above.</summary>
        [Theory]
        [InlineData(null)]
        [InlineData("BreakTheSod")]
        public void A_downed_hungry_colonist_is_fed_within_a_few_hours_under_an_edict_or_none(string? edict)
        {
            Settlement settlement = Opened("AgrarianVillage", "sod-patient");
            CoreMap map = settlement.InteriorMap!;
            ReachAgrarian(map);
            List<Pawn> citizens = settlement.Citizens.Where(p => p.Spawned && p.Map == map).ToList();
            if (edict != null) Assert.Equal(GodCommandOutcome.Done, GodCommands.IssueEdict(edict).Outcome);
            Sample(citizens, 2500); // food reaches the map and the band settles into its work

            Pawn patient = citizens.First(p => !p.Downed);
            patient.health.ForceDowned = true;
            patient.needs.food!.CurLevelPercentage = 0.05f;
            float before = patient.needs.food.CurLevelPercentage;
            int fedAt = -1;
            for (int i = 0; i < GenDate.TicksPerHour * 6 && fedAt < 0; i++)
            {
                Find.TickManager.DoSingleTick();
                if (patient.needs.food.CurLevelPercentage > before + 0.1f) fedAt = i;
            }
            output.WriteLine((edict ?? "no edict") + ": fed after " + fedAt + " ticks");
            Assert.True(fedAt >= 0, "nobody fed the downed, starving colonist within six hours");
        }
    }
}
