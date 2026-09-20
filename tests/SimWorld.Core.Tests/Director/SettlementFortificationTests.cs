using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// Fortification's defence term (tracker item <c>director.raids</c> / <c>settlements.structures</c>):
    /// <see cref="SettlementRaidResolver.Muster"/> now reads <see cref="Settlement.StructureCount"/> for
    /// completed walls, closing the gap this lane was scouted against — a settlement walled in granite
    /// defended exactly as well as one standing in the open, because nothing anywhere read what it had built.
    /// </summary>
    public class SettlementFortificationTests : ContentTestBase
    {
        public SettlementFortificationTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
            NameUseChecker.Clear();
        }

        private static ThingDef Wall => DefDatabase<ThingDef>.GetNamed("Wall");

        private static Faction Raiders(string name) =>
            new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), name, "F_" + name);

        private static Settlement Town(string name, int tile, int citizens, int walls)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, null, name, 0);
            for (int i = 0; i < citizens; i++) settlement.AddCitizen(NewHuman(name + i));
            if (walls > 0) settlement.AddStructure(Wall, walls);
            return settlement;
        }

        private static IncidentWorker_RaidEnemy NewWorker()
        {
            var def = new IncidentDef
            {
                defName = "TestFortRaid",
                category = IncidentCategoryDefOf.ThreatBig,
                workerClass = typeof(IncidentWorker_RaidEnemy),
            };
            return (IncidentWorker_RaidEnemy)def.Worker;
        }

        private static CivilizationTarget TargetOf(Settlement settlement)
        {
            var target = new CivilizationTarget();
            target.SetSettlements(new[] { settlement });
            return target;
        }

        private static bool Fire(IncidentWorker_RaidEnemy worker, CivilizationTarget target, Faction faction, float points = 500f) =>
            worker.TryExecute(new IncidentParms { target = target, points = points, faction = faction });

        // -------------------------------------------------------------------------------------------
        // Defence itself.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_walled_settlement_musters_measurably_more_defence_than_an_identical_unwalled_one()
        {
            Settlement unwalled = Town("Open", 1, citizens: 6, walls: 0);
            Settlement walled = Town("Walled", 2, citizens: 6, walls: 20);

            Assert.True(
                SettlementRaidResolver.DefenceStrengthOf(walled) > SettlementRaidResolver.DefenceStrengthOf(unwalled),
                "a walled settlement defended no better than an identical unwalled one");

            // Fortification does not muster a head: the pool casualties are drawn from is the same six
            // citizens either way, walled or not.
            Assert.Equal(SettlementRaidResolver.MusterOf(unwalled), SettlementRaidResolver.MusterOf(walled));
        }

        [Fact]
        public void More_walls_defend_measurably_better_than_fewer_on_an_otherwise_identical_settlement()
        {
            Settlement few = Town("FewWalls", 3, citizens: 5, walls: 4);
            Settlement many = Town("ManyWalls", 4, citizens: 5, walls: 40);

            Assert.True(SettlementRaidResolver.DefenceStrengthOf(many) > SettlementRaidResolver.DefenceStrengthOf(few),
                "forty completed walls defended no better than four");
        }

        // -------------------------------------------------------------------------------------------
        // Raid outcomes: direction over a seed sweep, never a single roll.
        // -------------------------------------------------------------------------------------------

        [Fact]
        public void A_walled_settlement_repels_more_of_the_same_raids_than_an_identical_unwalled_one()
        {
            int unwalledHeld = RepelledOutOf(citizens: 4, walls: 0, seed: 5150);
            int walledHeld = RepelledOutOf(citizens: 4, walls: 30, seed: 5150);

            Assert.True(walledHeld > unwalledHeld,
                "a fortified settlement repelled no more of the same raids than an identical unfortified one (unwalled="
                + unwalledHeld + ", walled=" + walledHeld + ")");
        }

        private static int RepelledOutOf(int citizens, int walls, int seed, int trials = 200)
        {
            Rand.Current = new RandomStream(seed);
            Faction raiders = Raiders("Repeat" + walls);
            int held = 0;
            for (int i = 0; i < trials; i++)
            {
                Settlement town = Town("T" + walls + "_" + i, 100 + i, citizens, walls);
                IncidentWorker_RaidEnemy worker = NewWorker();
                Assert.True(Fire(worker, TargetOf(town), raiders));
                if (worker.LastRaidOutcome!.Value.Repelled) held++;
            }
            return held;
        }

        [Fact]
        public void A_walled_settlement_loses_fewer_people_than_an_identical_unwalled_one_across_the_same_raids()
        {
            int unwalledLost = TotalLivesLostAcross(citizens: 5, walls: 0, seed: 7070);
            int walledLost = TotalLivesLostAcross(citizens: 5, walls: 30, seed: 7070);

            Assert.True(walledLost < unwalledLost,
                "a fortified settlement lost no fewer people than an identical unfortified one across the same raids (unwalled="
                + unwalledLost + ", walled=" + walledLost + ")");
        }

        private static int TotalLivesLostAcross(int citizens, int walls, int seed, int trials = 150, float points = 500f)
        {
            Rand.Current = new RandomStream(seed);
            Faction raiders = Raiders("Cost" + walls);
            int lost = 0;
            for (int i = 0; i < trials; i++)
            {
                Settlement town = Town("L" + walls + "_" + i, 700 + i, citizens, walls);
                IncidentWorker_RaidEnemy worker = NewWorker();
                Assert.True(Fire(worker, TargetOf(town), raiders, points));
                lost += worker.LastRaidOutcome!.Value.TotalLivesLost;
            }
            return lost;
        }
    }
}
