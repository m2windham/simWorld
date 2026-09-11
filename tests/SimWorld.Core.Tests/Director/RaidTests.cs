using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>director.raids: IncidentWorker_RaidEnemy actually generating a squad, end to end.</summary>
    public class RaidTests : ContentTestBase
    {
        public RaidTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static FactionDef TribalDef => DefDatabase<FactionDef>.GetNamed("TribalCivilization");
        private static FactionDef PlayerDef => DefDatabase<FactionDef>.GetNamed("PlayerCivilization");

        private static Faction NewFaction(FactionDef def, string name) => new Faction(def, name, "F_" + name);

        private static global::SimWorld.Director.IncidentWorker_RaidEnemy NewWorker()
        {
            var def = new global::SimWorld.Director.IncidentDef
            {
                defName = "TestRaid",
                category = global::SimWorld.Director.IncidentCategoryDefOf.ThreatBig,
                workerClass = typeof(global::SimWorld.Director.IncidentWorker_RaidEnemy),
            };
            return (global::SimWorld.Director.IncidentWorker_RaidEnemy)def.Worker;
        }

        // ---- content ----

        [Fact]
        public void RaidEnemy_content_uses_the_real_worker()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.Same(typeof(global::SimWorld.Director.IncidentWorker_RaidEnemy), global::SimWorld.Director.IncidentDefOf.RaidEnemy.workerClass);
        }

        // ---- CanFireNow / faction resolution ----

        [Fact]
        public void CanFireNow_false_without_any_hostile_faction()
        {
            Faction player = NewFaction(PlayerDef, "Player");
            Find.FactionManager.Add(player);
            var target = new global::SimWorld.Director.CivilizationTarget();
            var parms = new global::SimWorld.Director.IncidentParms { target = target, points = 200f };

            Assert.False(NewWorker().CanFireNow(parms));
        }

        [Fact]
        public void CanFireNow_true_once_a_hostile_faction_exists()
        {
            Faction player = NewFaction(PlayerDef, "Player");
            Faction rough = NewFaction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Rough");
            player.SetRelationDirect(rough, FactionRelationKind.Hostile, -100);
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(rough);

            var target = new global::SimWorld.Director.CivilizationTarget();
            var parms = new global::SimWorld.Director.IncidentParms { target = target, points = 200f };

            Assert.True(NewWorker().CanFireNow(parms));
        }

        // ---- squad generation end to end ----

        [Fact]
        public void TryExecuteWorker_generates_a_squad_attributes_it_and_records_it()
        {
            Faction player = NewFaction(PlayerDef, "Player");
            Faction rough = NewFaction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Rough");
            player.SetRelationDirect(rough, FactionRelationKind.Hostile, -100);
            Find.FactionManager.Add(player);
            Find.FactionManager.Add(rough);
            Rand.Current = new RandomStream(555);
            Pawn.ResetThingIdCounter();

            var target = new global::SimWorld.Director.CivilizationTarget();
            var parms = new global::SimWorld.Director.IncidentParms { target = target, points = 400f };
            global::SimWorld.Director.IncidentWorker_RaidEnemy worker = NewWorker();

            bool result = worker.TryExecute(parms);

            Assert.True(result);
            Assert.NotNull(worker.LastRaidPawns);
            Assert.NotEmpty(worker.LastRaidPawns!);
            Assert.Same(rough, worker.LastRaidFaction);
            Assert.NotNull(worker.LastRaidStrategy);
            Assert.All(worker.LastRaidPawns!, p => Assert.Same(rough, p.faction));
            Assert.Same(rough, parms.faction);
            Assert.True(target.StoryState.HasFired(worker.def)); // TryExecute recorded the firing on the target
        }

        [Fact]
        public void TryExecuteWorker_pins_the_faction_when_parms_already_names_one()
        {
            Faction tribal = NewFaction(TribalDef, "PinnedTribal");
            Rand.Current = new RandomStream(9);
            Pawn.ResetThingIdCounter();

            var target = new global::SimWorld.Director.CivilizationTarget();
            var parms = new global::SimWorld.Director.IncidentParms { target = target, points = 300f, faction = tribal };

            Assert.True(NewWorker().TryExecute(parms));
        }

        [Fact]
        public void TryExecuteWorker_false_when_the_faction_has_no_pawnGroupMakers()
        {
            // A faction whose def has no pawnGroupMakers at all (the player's own) can never generate a squad.
            Faction player = NewFaction(PlayerDef, "PlayerNoRaid");
            var target = new global::SimWorld.Director.CivilizationTarget();
            var parms = new global::SimWorld.Director.IncidentParms { target = target, points = 300f, faction = player };

            Assert.False(NewWorker().TryExecute(parms));
        }

        [Fact]
        public void Determinism_same_seed_yields_the_same_squad()
        {
            Faction rough = NewFaction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "RoughDet");

            Rand.Current = new RandomStream(4242);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            var targetA = new global::SimWorld.Director.CivilizationTarget();
            var parmsA = new global::SimWorld.Director.IncidentParms { target = targetA, points = 350f, faction = rough };
            global::SimWorld.Director.IncidentWorker_RaidEnemy workerA = NewWorker();
            Assert.True(workerA.TryExecute(parmsA));

            Rand.Current = new RandomStream(4242);
            Pawn.ResetThingIdCounter();
            NameUseChecker.Clear();
            var targetB = new global::SimWorld.Director.CivilizationTarget();
            var parmsB = new global::SimWorld.Director.IncidentParms { target = targetB, points = 350f, faction = rough };
            global::SimWorld.Director.IncidentWorker_RaidEnemy workerB = NewWorker();
            Assert.True(workerB.TryExecute(parmsB));

            Assert.Equal(
                workerA.LastRaidPawns!.Select(p => p.kindDef!.defName),
                workerB.LastRaidPawns!.Select(p => p.kindDef!.defName));
            Assert.Equal(
                workerA.LastRaidPawns!.Select(p => p.Name!.ToStringFull),
                workerB.LastRaidPawns!.Select(p => p.Name!.ToStringFull));
            Assert.Same(workerA.LastRaidStrategy, workerB.LastRaidStrategy);
        }

        // ---- points scaling by strategy ----

        /// <summary>
        /// End-to-end proof that RaidStrategyDef.pointsFactor actually reaches squad generation: buckets 200
        /// real firings by which strategy TryExecuteWorker picked, and confirms the strategy with the higher
        /// pointsFactor bought a stronger average squad. Uses real content (ImmediateAttack pointsFactor=1.0,
        /// Siege pointsFactor=0.8) against OutlanderCivilization, the only faction eligible for both.
        /// </summary>
        [Fact]
        public void RaidStrategy_pointsFactor_scales_the_squad_TryExecuteWorker_actually_generates()
        {
            Faction outlander = NewFaction(DefDatabase<FactionDef>.GetNamed("OutlanderCivilization"), "OutlanderScale");
            Rand.Current = new RandomStream(31);
            Pawn.ResetThingIdCounter();

            var byFactor = new Dictionary<float, List<float>>();
            for (int i = 0; i < 200; i++)
            {
                var target = new global::SimWorld.Director.CivilizationTarget();
                var parms = new global::SimWorld.Director.IncidentParms { target = target, points = 500f, faction = outlander };
                global::SimWorld.Director.IncidentWorker_RaidEnemy worker = NewWorker();
                Assert.True(worker.TryExecute(parms));

                float totalPower = worker.LastRaidPawns!.Sum(p => p.kindDef!.combatPower);
                float factor = worker.LastRaidStrategy!.pointsFactor;
                if (!byFactor.TryGetValue(factor, out List<float>? list))
                {
                    list = new List<float>();
                    byFactor[factor] = list;
                }
                list.Add(totalPower);
            }

            Assert.True(byFactor.Count >= 2, "expected to observe more than one raid strategy across 200 trials on an Industrial faction");
            List<float> factorsAscending = byFactor.Keys.OrderBy(f => f).ToList();
            float lowFactorAvg = byFactor[factorsAscending[0]].Average();
            float highFactorAvg = byFactor[factorsAscending[factorsAscending.Count - 1]].Average();
            Assert.True(highFactorAvg > lowFactorAvg,
                "expected the higher-pointsFactor strategy to buy a stronger average squad (low=" + lowFactorAvg + ", high=" + highFactorAvg + ")");
        }

        // ---- map edge placement, when a map is wired ----

        [Fact]
        public void When_a_map_is_wired_the_squad_walks_in_from_one_map_edge()
        {
            // RimWorld's PawnsArrivalModeWorker_EdgeWalkIn: one entry cell for the group, then each pawn is
            // placed on a walkable cell within CellFinder.RandomClosewalkCellNear's radius of it. The port
            // used to compute the edge cell once and spawn the whole squad on that single cell — see
            // UnwatchedRaidTests.Raiders_no_longer_all_spawn_on_one_cell.
            Faction rough = NewFaction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "RoughMap");
            var map = new global::SimWorld.Map.Map(30, 30, TerrainDefOf.Soil);
            var target = new global::SimWorld.Director.CivilizationTarget { Map = map };
            Rand.Current = new RandomStream(17);
            Pawn.ResetThingIdCounter();

            var parms = new global::SimWorld.Director.IncidentParms { target = target, points = 200f, faction = rough };
            global::SimWorld.Director.IncidentWorker_RaidEnemy worker = NewWorker();

            Assert.True(worker.TryExecute(parms));
            Assert.NotEmpty(worker.LastRaidPawns!);
            foreach (Pawn pawn in worker.LastRaidPawns!)
            {
                Assert.True(pawn.Spawned);
                Assert.Same(map, pawn.Map);
                int toNearestEdge = Math.Min(
                    Math.Min(pawn.Position.x, pawn.Position.z),
                    Math.Min(map.Size.x - 1 - pawn.Position.x, map.Size.z - 1 - pawn.Position.z));
                Assert.True(toNearestEdge <= global::SimWorld.Director.IncidentWorker_RaidEnemy.ClosewalkRadius,
                    "expected " + pawn.Position + " to be within one group entry of a map edge (size " + map.Size + ")");
            }
        }

        [Fact]
        public void Without_a_wired_map_the_squad_is_generated_but_never_spawned()
        {
            Faction rough = NewFaction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "RoughNoMap");
            var target = new global::SimWorld.Director.CivilizationTarget(); // Map left null, as every game today leaves it
            Rand.Current = new RandomStream(18);
            Pawn.ResetThingIdCounter();

            var parms = new global::SimWorld.Director.IncidentParms { target = target, points = 200f, faction = rough };
            global::SimWorld.Director.IncidentWorker_RaidEnemy worker = NewWorker();

            Assert.True(worker.TryExecute(parms));
            Assert.NotEmpty(worker.LastRaidPawns!);
            Assert.All(worker.LastRaidPawns!, p => Assert.False(p.Spawned));
        }
    }
}
