using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defence;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using SimWorld.World.Gen;
using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Defence
{
    /// <summary>
    /// Defence that scales with threat and era (system: combat): a watch of the best fighters and a trap line
    /// round the village, each about as many as the raid the storyteller would buy now, with the trap the era
    /// allows.
    /// </summary>
    public class SettlementDefenceTests : ContentTestBase
    {
        public SettlementDefenceTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int size = 60) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private static void Finish(string project) =>
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed(project));

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Faction NewFaction(string name) =>
            new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), name, "F_" + name);

        private static List<Pawn> Band(CoreMap map, int n)
        {
            var band = new List<Pawn>();
            for (int i = 0; i < n; i++)
            {
                Pawn p = NewHuman("C" + i);
                GenSpawn.Spawn(p, new IntVec3(2 + i, 0, 2), map);
                band.Add(p);
            }
            return band;
        }

        private static int TrapBlueprints(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>()
                .Count(b => b.EntityToBuild == DefenceDefOf.TrapSpike || b.EntityToBuild == DefenceDefOf.TrapIED);

        // ---- sized by the threat ----

        [Fact]
        public void The_raid_expected_grows_with_the_threat_and_is_nothing_with_no_threat()
        {
            Assert.Equal(0, SettlementDefence.RaidersExpected(0f));
            int last = 0;
            foreach (float points in new[] { 35f, 147f, 400f, 750f, 3000f })
            {
                int n = SettlementDefence.RaidersExpected(points);
                Assert.True(n >= 1 && n >= last, points + " points: " + n);
                last = n;
            }
            Assert.True(SettlementDefence.RaidersExpected(750f) > SettlementDefence.RaidersExpected(147f));
        }

        [Fact]
        public void The_watch_grows_with_the_threat_takes_the_armed_first_and_never_a_pacifist()
        {
            CoreMap map = NewMap();
            List<Pawn> band = Band(map, 10);
            band[7].equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def("MeleeWeapon_Knife"), Def("Flint")));
            band[3].story.adulthood = DefDatabase<BackstoryDef>.GetNamed("TribalElder"); // incapable of violence

            List<Pawn> small = SettlementDefence.WatchFor(map, 60f);
            List<Pawn> large = SettlementDefence.WatchFor(map, 300f);
            List<Pawn> huge = SettlementDefence.WatchFor(map, 10000f);

            Assert.Equal(SettlementDefence.RaidersExpected(60f), small.Count);
            Assert.True(large.Count > small.Count);
            Assert.Same(band[7], small[0]);
            Assert.Equal(9, huge.Count); // every fighter, and only fighters
            Assert.DoesNotContain(band[3], huge);
            Assert.Empty(SettlementDefence.WatchFor(map, 0f));
        }

        // ---- the trap line, by era ----

        [Fact]
        public void The_trap_is_the_eras_spike_trap_then_an_IED_once_the_settlement_has_the_steel()
        {
            CoreMap map = NewMap();
            Assert.Null(SettlementDefence.BestTrapDef(map));

            Finish("Trapping");
            Assert.Same(DefenceDefOf.TrapSpike, SettlementDefence.BestTrapDef(map));

            Finish("Gunpowder");
            Assert.Same(DefenceDefOf.TrapSpike, SettlementDefence.BestTrapDef(map)); // no steel yet
            Thing steel = ThingMaker.MakeThing(Def("Steel"));
            steel.stackCount = 50;
            GenSpawn.Spawn(steel, new IntVec3(30, 0, 30), map);
            Assert.Same(DefenceDefOf.TrapIED, SettlementDefence.BestTrapDef(map));
        }

        [Fact]
        public void The_trap_line_is_laid_one_a_pass_on_the_ring_up_to_the_raid_expected_and_no_further()
        {
            CoreMap map = NewMap();
            map.settlementCompound = SettlementCompound.WithInterior(new CellRect(26, 26, 4, 4));
            Finish("Trapping");
            HashSet<IntVec3> ring = SettlementDefence.TrapRing(map).ToHashSet();
            Assert.NotEmpty(ring);

            const int Wanted = 5;
            for (int pass = 0; pass < Wanted; pass++)
            {
                Assert.True(SettlementDefence.EnsureTraps(map, Wanted));
                Assert.Equal(pass + 1, TrapBlueprints(map));
            }
            Assert.False(SettlementDefence.EnsureTraps(map, Wanted));
            Assert.Equal(Wanted, TrapBlueprints(map));
            Assert.All(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint), b => Assert.Contains(b.Position, ring));

            // A larger threat lays more.
            Assert.True(SettlementDefence.EnsureTraps(map, Wanted * 2));
            Assert.Equal(Wanted + 1, TrapBlueprints(map));
        }

        [Fact]
        public void No_trap_is_laid_before_the_era_knows_one()
        {
            CoreMap map = NewMap();
            map.settlementCompound = SettlementCompound.WithInterior(new CellRect(26, 26, 4, 4));
            Assert.False(SettlementDefence.EnsureTraps(map, 5));
            Assert.Equal(0, TrapBlueprints(map));
        }

        [Fact]
        public void A_built_trap_never_springs_on_its_own_people_and_does_on_a_stranger()
        {
            CoreMap map = NewMap(20);
            Faction ours = NewFaction("Ours");
            Faction theirs = NewFaction("Theirs");

            var trap = (ThingWithComps)ThingMaker.MakeThing(DefenceDefOf.TrapSpike);
            trap.SetFaction(ours); // what Frame.CompleteConstruction leaves: the builder's faction on the building
            GenSpawn.Spawn(trap, new IntVec3(10, 0, 10), map);
            CompTrap comp = trap.GetComp<CompTrap>()!;

            Pawn citizen = NewHuman("Citizen");
            citizen.SetFaction(ours);
            GenSpawn.Spawn(citizen, new IntVec3(10, 0, 10), map);
            comp.CompTick();
            Assert.True(comp.armed);
            citizen.DeSpawn();

            Pawn raider = NewHuman("Raider");
            raider.SetFaction(theirs);
            GenSpawn.Spawn(raider, new IntVec3(10, 0, 10), map);
            comp.CompTick();
            Assert.False(comp.armed);
        }

        [Fact]
        public void A_citizen_builds_the_trap_and_it_stands_armed_and_owned()
        {
            CoreMap map = NewMap(20);
            Faction ours = NewFaction("Ours");
            Finish("Trapping");
            GenSpawn.Spawn(ThingMaker.MakeThing(GenConstruct.BlueprintDefFor(DefenceDefOf.TrapSpike)!), new IntVec3(10, 0, 10), map);
            Thing wood = ThingMaker.MakeThing(Def("WoodLog"));
            wood.stackCount = 30;
            GenSpawn.Spawn(wood, new IntVec3(6, 0, 10), map);
            Pawn builder = NewHuman("Builder");
            builder.SetFaction(ours);
            GenSpawn.Spawn(builder, new IntVec3(4, 0, 10), map);
            builder.workSettings.DisableAll();
            builder.workSettings.SetPriority(SimWorld.Work.WorkTypeDefOf.Construction, 3);

            RunTicks(6000, builder);

            Thing trap = Assert.Single(map.listerThings.ThingsOfDef(DefenceDefOf.TrapSpike));
            Assert.Same(ours, trap.Faction);
            Assert.True(((ThingWithComps)trap).GetComp<CompTrap>()!.armed, "the trap sprang on the citizen who built it");
        }

        // ---- the watch in the running game ----

        [Fact]
        public void A_citizen_on_watch_goes_looking_for_the_enemy_and_one_off_watch_waits_to_be_cornered()
        {
            SimWorld.World.World world = WorldGenerator.GenerateWorld("defence-watch", 0.3f, OverallRainfall.Normal,
                OverallTemperature.Normal, OverallPopulation.Normal, "Test", 4, soloStart: true);
            Faction faction = world.factions.First();
            int tile = Enumerable.Range(0, world.grid.TilesCount).First(i => !world.grid.Tiles[i].WaterCovered);
            Settlement settlement = SettlementFounder.Found(world, tile, faction, SettlementTuning.FoundingBandRange.min, new RandomStream(7));
            CoreMap map = settlement.EnterMap(world);
            Find.World = world;

            var saved = SettlementDefence.ThreatPointsOf;
            try
            {
                // A founding band comes armed (its hunting bows), and an armed citizen already goes looking for
                // the enemy. Disarm it, so the watch is what decides who does.
                foreach (Pawn p in settlement.Citizens)
                {
                    while (p.equipment.Primary is ThingWithComps eq) p.equipment.RemoveEquipment(eq);
                }
                int armed = settlement.Citizens.Count(p => p.Spawned && AttackVerbUtility.HasEquippedWeapon(p));
                Assert.Equal(0, armed);
                float threat = (armed + 1) * SimWorld.Director.RaidResolutionTuning.DefaultDefenderCombatPower;
                SettlementDefence.ThreatPointsOf = _ => threat;
                List<Pawn> watch = SettlementDefence.WatchFor(map, threat);
                Assert.Equal(armed + 1, watch.Count);
                Pawn onWatch = watch.First(p => !AttackVerbUtility.HasEquippedWeapon(p));
                Pawn offWatch = settlement.Citizens.First(p => p.Spawned && !watch.Contains(p) && !AttackVerbUtility.HasEquippedWeapon(p));

                Assert.True(SettlementDefence.IsOnWatch(onWatch));
                Assert.False(SettlementDefence.IsOnWatch(offWatch));
                Assert.Equal(CombatAITuning.TargetAcquireRadius, CombatPostureUtility.TargetAcquireRadiusFor(onWatch));
                Assert.Equal(CombatAITuning.MeleeReachCells, CombatPostureUtility.TargetAcquireRadiusFor(offWatch));

                // No threat, no watch.
                SettlementDefence.ThreatPointsOf = _ => 0f;
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + SettlementDefence.IntervalTicks);
                Assert.False(SettlementDefence.IsOnWatch(onWatch));
            }
            finally
            {
                SettlementDefence.ThreatPointsOf = saved;
            }
        }

        // ---- save and load ----

        [Fact]
        public void A_saved_trap_line_loads_back_armed_and_the_next_pass_adds_nothing()
        {
            CoreMap map = NewMap();
            map.settlementCompound = SettlementCompound.WithInterior(new CellRect(26, 26, 4, 4));
            Finish("Trapping");
            foreach (IntVec3 c in SettlementDefence.TrapRing(map).Take(3))
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(DefenceDefOf.TrapSpike), c, map);
            }
            Assert.Equal(3, SettlementDefence.CountTrapLine(map));

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Assert.Equal(3, SettlementDefence.CountTrapLine(loaded));
            Assert.False(SettlementDefence.EnsureTraps(loaded, 3));
        }
    }
}
