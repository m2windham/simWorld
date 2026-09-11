using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Things
{
    /// <summary>
    /// Fire (system: fire — RimWorld's <c>Verse.Fire</c>): what burns, what a fire does to it, where it
    /// spreads, and what puts it out. Tuned numbers are pinned as bands and trends, never as literals — the
    /// constants on <see cref="Fire"/> are RimWorld's shape recalled, not sourced (CLAUDE.md).
    /// </summary>
    public class FireTests : ContentTestBase
    {
        public FireTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ, TerrainDef? fill = null) =>
            new CoreMap(sizeX, sizeZ, fill ?? TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Thing SpawnThing(CoreMap map, IntVec3 cell, string defName, ThingDef? stuff = null)
        {
            Thing t = ThingMaker.MakeThing(Def(defName), stuff);
            GenSpawn.Spawn(t, cell, map);
            return t;
        }

        /// <summary>A fire put straight onto a cell, bypassing <see cref="FireUtility.TryStartFireIn"/>'s
        /// "is there anything to burn" check — for the cases that are about what a fire does once lit.</summary>
        private static Fire IgniteDirect(CoreMap map, IntVec3 cell, float size = Fire.MinFireSize)
        {
            var fire = (Fire)ThingMaker.MakeThing(FireThingDefOf.Fire);
            fire.fireSize = size;
            GenSpawn.Spawn(fire, cell, map);
            return fire;
        }

        /// <summary>Advances the shared clock without needing a pawn; fires register their own tickability
        /// when they spawn, so they tick exactly as they would in a running game.</summary>
        private static void RunMapTicks(int ticks)
        {
            for (int i = 0; i < ticks; i++) Find.TickManager.DoSingleTick();
        }

        private static List<Fire> FiresOn(CoreMap map) =>
            FireUtility.AllFires(map).Cast<Fire>().ToList();

        // ---- content ----

        [Fact]
        public void Fire_content_loads_and_every_fire_def_is_bound()
        {
            Assert.Empty(Content.Result.Errors);
            Assert.NotNull(FireThingDefOf.Fire);
            Assert.NotNull(FireStatDefOf.Flammability);
            Assert.NotNull(FireDamageDefOf.Flame);
            Assert.IsType<DamageWorker_Flame>(FireDamageDefOf.Flame.Worker);
            Assert.Equal(typeof(Fire), FireThingDefOf.Fire.thingClass);
        }

        /// <summary>
        /// The question this lane was told to answer rather than assume: does the shipped content actually
        /// contain anything that can catch fire? A fire system nothing can catch is a dormant feature.
        /// </summary>
        [Fact]
        public void Shipped_content_contains_both_things_that_burn_and_things_that_do_not()
        {
            ThingDef[] burns = { Def("Wall"), Def("Door"), Def("WildPlant"), Def("Plant_Potato"), Def("WoodLog"), Def("Cloth"), Def("Bed"), Def("Human") };
            ThingDef[] doesNot = { Def("Sandstone"), Def("Granite"), Def("MineableSteel"), Def("Steel"), Def("ChunkGranite"), Def("BlocksSandstone"), FireThingDefOf.Fire };

            foreach (ThingDef def in burns)
            {
                Assert.True(def.BaseFlammability >= FireUtility.MinFlammability,
                    def.defName + " should be flammable but its BaseFlammability is " + def.BaseFlammability + ".");
            }
            foreach (ThingDef def in doesNot)
            {
                Assert.True(def.BaseFlammability < FireUtility.MinFlammability,
                    def.defName + " should not be flammable but its BaseFlammability is " + def.BaseFlammability + ".");
            }
        }

        /// <summary>The material decides, not only the Def — RimWorld's own mechanism, and the reason a
        /// settlement's building choices are a fire-risk decision at all.</summary>
        [Fact]
        public void A_wall_built_of_stone_does_not_burn_while_the_same_wall_built_of_wood_does()
        {
            CoreMap map = NewMap(8, 8);
            Thing wooden = SpawnThing(map, new IntVec3(2, 0, 2), "Wall", Def("WoodLog"));
            Thing stone = SpawnThing(map, new IntVec3(4, 0, 4), "Wall", Def("BlocksSandstone"));
            Thing steel = SpawnThing(map, new IntVec3(6, 0, 6), "Wall", Def("Steel"));

            Assert.True(FireUtility.FlammabilityOf(wooden) >= FireUtility.MinFlammability);
            Assert.True(FireUtility.FlammabilityOf(stone) < FireUtility.MinFlammability);
            Assert.True(FireUtility.FlammabilityOf(steel) < FireUtility.MinFlammability);
        }

        // ---- burning what it stands on ----

        [Fact]
        public void Fire_on_a_flammable_building_damages_it_over_time()
        {
            CoreMap map = NewMap(10, 10);
            Thing wall = SpawnThing(map, new IntVec3(5, 0, 5), "Wall");
            Assert.True(FireUtility.TryStartFireIn(wall.Position, map, Fire.MinFireSize));

            int whole = wall.HitPoints;
            RunMapTicks(600);
            int afterSome = wall.HitPoints;
            RunMapTicks(600);

            Assert.True(afterSome < whole, "A burning wall should be losing hit points.");
            Assert.True(wall.HitPoints < afterSome, "It should keep losing them for as long as it burns.");
        }

        [Fact]
        public void Fire_burns_a_flammable_thing_away_entirely_and_then_goes_out_for_want_of_fuel()
        {
            CoreMap map = NewMap(10, 10);
            Thing plant = SpawnThing(map, new IntVec3(5, 0, 5), "WildPlant");
            Assert.True(FireUtility.TryStartFireIn(plant.Position, map, Fire.MinFireSize));

            RunMapTicks(20000);

            Assert.True(plant.Destroyed, "A burning plant should eventually be destroyed.");
            Assert.Empty(FiresOn(map));
        }

        [Fact]
        public void A_fire_with_nothing_under_it_goes_out_on_its_next_pass()
        {
            CoreMap map = NewMap(10, 10);
            Fire fire = IgniteDirect(map, new IntVec3(5, 0, 5));

            // Bare soil holds no fuel. This is what stops fire walking across an empty field, and it is a
            // rule rather than a special case: flammabilityMax falls to zero and the fire dies.
            RunMapTicks(Fire.ComplexCalcsInterval + 1);

            Assert.True(fire.Destroyed);
        }

        [Fact]
        public void Fire_grows_while_it_has_fuel_and_stops_at_its_cap()
        {
            CoreMap map = NewMap(10, 10);
            Thing wall = SpawnThing(map, new IntVec3(5, 0, 5), "Wall");
            FireUtility.TryStartFireIn(wall.Position, map, Fire.MinFireSize);
            Fire fire = FiresOn(map).Single();

            float atBirth = fire.fireSize;
            RunMapTicks(900);
            float grown = fire.fireSize;
            RunMapTicks(9000);

            Assert.True(grown > atBirth, "A fed fire should grow.");
            Assert.True(fire.fireSize > grown, "And keep growing.");
            Assert.Equal(Fire.MaxFireSize, fire.fireSize, 3);
        }

        [Fact]
        public void A_bigger_fire_does_more_damage_per_pass_than_a_small_one()
        {
            int DamageDoneAt(float fireSize)
            {
                CoreMap map = NewMap(10, 10);
                Thing wall = SpawnThing(map, new IntVec3(5, 0, 5), "Wall");
                IgniteDirect(map, wall.Position, fireSize);
                int whole = wall.HitPoints;
                // Short enough that the small fire cannot grow into the big one's size band.
                RunMapTicks(900);
                return whole - wall.HitPoints;
            }

            int small = DamageDoneAt(Fire.MinFireSize);
            int big = DamageDoneAt(Fire.MaxFireSize);

            Assert.True(small > 0, "Even the smallest fire should do some damage.");
            Assert.True(big > small, $"A big fire should burn harder: {big} vs {small}.");
        }

        // ---- spread ----

        [Fact]
        public void Fire_spreads_to_flammable_neighbours()
        {
            CoreMap map = NewMap(12, 12);
            var centre = new IntVec3(5, 0, 5);
            SpawnThing(map, centre, "Wall");
            foreach (IntVec3 offset in GenAdj.AdjacentCells)
            {
                SpawnThing(map, centre + offset, "Wall");
            }
            IgniteDirect(map, centre, Fire.MinFireSize);

            RunMapTicks(6000);

            Assert.True(FiresOn(map).Count > 1, "A grown fire surrounded by wooden walls should have spread.");
        }

        [Fact]
        public void Fire_does_not_spread_to_neighbours_that_cannot_burn()
        {
            CoreMap map = NewMap(12, 12);
            var centre = new IntVec3(5, 0, 5);
            SpawnThing(map, centre, "Wall");
            foreach (IntVec3 offset in GenAdj.AdjacentCells)
            {
                // Natural rock: not flammable, and nothing else is within spark range either.
                SpawnThing(map, centre + offset, "Granite");
            }
            IgniteDirect(map, centre, Fire.MaxFireSize);

            RunMapTicks(6000);

            Assert.Single(FiresOn(map));
        }

        [Fact]
        public void Fire_never_starts_where_there_is_nothing_to_burn_or_on_water()
        {
            CoreMap map = NewMap(10, 10);
            var bare = new IntVec3(2, 0, 2);
            var water = new IntVec3(4, 0, 4);
            map.terrainGrid.SetTerrain(water, TerrainDefOf.WaterShallow);
            SpawnThing(map, water, "WildPlant");

            Assert.False(FireUtility.TryStartFireIn(bare, map, Fire.MinFireSize));
            Assert.False(FireUtility.TryStartFireIn(water, map, Fire.MinFireSize),
                "Water extinguishes fire, whatever is floating in it.");
            Assert.Empty(FiresOn(map));
        }

        [Fact]
        public void Only_one_fire_ever_occupies_a_cell()
        {
            CoreMap map = NewMap(10, 10);
            Thing wall = SpawnThing(map, new IntVec3(5, 0, 5), "Wall");

            Assert.True(FireUtility.TryStartFireIn(wall.Position, map, Fire.MinFireSize));
            Assert.False(FireUtility.TryStartFireIn(wall.Position, map, Fire.MinFireSize));
            Assert.Single(FiresOn(map));
        }

        /// <summary>Spread is a random draw, and every draw goes through the seeded stream — so the same
        /// seed always burns the same way (CLAUDE.md: determinism is a feature).</summary>
        [Fact]
        public void Spread_is_deterministic_for_a_given_seed()
        {
            List<IntVec3> BurntCellsWithSeed(int seed)
            {
                Find.TickManager = new TickManager();
                Rand.Current = new RandomStream(seed);
                Pawn.ResetThingIdCounter();

                CoreMap map = NewMap(14, 14);
                var centre = new IntVec3(6, 0, 6);
                for (int x = 3; x <= 9; x++)
                {
                    for (int z = 3; z <= 9; z++) SpawnThing(map, new IntVec3(x, 0, z), "Wall");
                }
                IgniteDirect(map, centre, Fire.MinFireSize);
                RunMapTicks(5000);
                return FiresOn(map).Select(f => f.Position).OrderBy(c => c.x).ThenBy(c => c.z).ToList();
            }

            List<IntVec3> first = BurntCellsWithSeed(4242);
            List<IntVec3> second = BurntCellsWithSeed(4242);
            List<IntVec3> other = BurntCellsWithSeed(99);

            Assert.True(first.Count > 1, "The setup should actually have spread, or this proves nothing.");
            Assert.Equal(first, second);
            Assert.NotEqual(first, other);
        }

        // ---- pawns ----

        /// <summary>
        /// An animal is used rather than a colonist on purpose: a colonist who catches fire beats it out
        /// before it can do much (see the test below), which is right but makes a poor probe. The point here
        /// is that fire damage lands as real <c>Burn</c> hediffs through <c>DamageWorker_Flame</c> and the
        /// ordinary health tracker, not as a second damage path of fire's own.
        /// </summary>
        [Fact]
        public void A_pawn_on_fire_takes_burn_injuries_through_the_real_health_pipeline()
        {
            CoreMap map = NewMap(10, 10);
            var dog = new Pawn(Husky, "Kindling");
            GenSpawn.Spawn(dog, new IntVec3(5, 0, 5), map);

            Assert.True(dog.TryAttachFire(Fire.MinFireSize));
            Assert.True(dog.IsBurning());

            RunTicks(900, dog);

            HediffDef burn = DefDatabase<HediffDef>.GetNamed("Burn");
            List<Hediff> burns = dog.health.hediffSet.hediffs.Where(h => h.def == burn).ToList();
            Assert.NotEmpty(burns);
            Assert.All(burns, h => Assert.True(h.Severity > 0f));
            Assert.True(dog.health.summaryHealth.SummaryHealthPercent < 1f, "The burns should register as real injury.");
        }

        /// <summary>
        /// Emergent, and worth pinning because it falls out of the parts rather than being written anywhere:
        /// the fire riding a citizen is a fire on their own cell, firefighting is emergency work, so the very
        /// next thing they do is beat it out. RimWorld reaches the same place by a dedicated
        /// <c>JobGiver_ExtinguishSelf</c>; here the ordinary work giver already covers it.
        /// </summary>
        [Fact]
        public void A_citizen_who_catches_fire_beats_it_out_themselves()
        {
            CoreMap map = NewMap(10, 10);
            Pawn citizen = NewHuman("Kindling");
            GenSpawn.Spawn(citizen, new IntVec3(5, 0, 5), map);
            citizen.TryAttachFire(Fire.MinFireSize);

            RunTicks(600, citizen);

            Assert.False(citizen.IsBurning());
            Assert.Empty(FiresOn(map));
        }

        [Fact]
        public void A_fire_riding_a_pawn_follows_them_and_dies_with_them()
        {
            CoreMap map = NewMap(10, 10);
            Pawn pawn = NewHuman("Torch");
            GenSpawn.Spawn(pawn, new IntVec3(2, 0, 2), map);
            pawn.TryAttachFire(Fire.MinFireSize);
            Fire fire = FiresOn(map).Single();

            pawn.Position = new IntVec3(7, 0, 7);
            RunTicks(2, pawn);
            Assert.Equal(pawn.Position, fire.Position);

            pawn.Destroy();
            RunMapTicks(2);
            Assert.True(fire.Destroyed, "A fire has nothing to burn once what it was riding is gone.");
        }

        [Fact]
        public void A_thing_already_alight_does_not_catch_a_second_fire()
        {
            CoreMap map = NewMap(10, 10);
            Pawn pawn = NewHuman("Torch");
            GenSpawn.Spawn(pawn, new IntVec3(5, 0, 5), map);

            Assert.True(pawn.TryAttachFire(Fire.MinFireSize));
            Assert.False(pawn.TryAttachFire(Fire.MinFireSize));
            Assert.Single(FiresOn(map));
        }

        /// <summary>The live ignition chain: something in this codebase that can actually reach a map and set
        /// it alight, so the whole system is not waiting on a system that does not exist.</summary>
        [Fact]
        public void An_incendiary_IED_sets_the_pawn_who_triggers_it_alight()
        {
            CoreMap map = NewMap(12, 12);
            var cell = new IntVec3(6, 0, 6);
            Thing trap = SpawnThing(map, cell, "TrapIEDIncendiary");
            Pawn pawn = NewHuman("Unlucky");
            GenSpawn.Spawn(pawn, cell, map);

            RunTicks(5, pawn);

            Assert.True(trap.Destroyed, "The charge should have gone off.");
            Assert.True(pawn.IsBurning(), "Flame damage should have set the pawn alight.");
        }

        // ---- going out ----

        [Fact]
        public void Rain_puts_out_a_fire_under_open_sky_and_leaves_a_roofed_one_alone()
        {
            CoreMap map = NewMap(12, 12);
            var open = new IntVec3(3, 0, 3);
            var sheltered = new IntVec3(8, 0, 8);
            SpawnThing(map, open, "Wall");
            SpawnThing(map, sheltered, "Wall");
            map.roofGrid.SetRoof(sheltered, RoofDefOf.RoofConstructed);
            FireUtility.TryStartFireIn(open, map, Fire.MinFireSize);
            FireUtility.TryStartFireIn(sheltered, map, Fire.MinFireSize);

            // A downpour, rolled once per complex-calc interval as the ported method would be driven.
            int extinguished = 0;
            for (int i = 0; i < 200 && extinguished == 0; i++)
            {
                extinguished = FireUtility.ExtinguishFiresFromRain(map, 1f);
            }

            Assert.Equal(1, extinguished);
            Assert.Single(FiresOn(map));
            Assert.Equal(sheltered, FiresOn(map).Single().Position);
        }

        [Fact]
        public void No_rain_puts_out_nothing()
        {
            CoreMap map = NewMap(10, 10);
            Thing wall = SpawnThing(map, new IntVec3(5, 0, 5), "Wall");
            FireUtility.TryStartFireIn(wall.Position, map, Fire.MinFireSize);

            for (int i = 0; i < 200; i++) Assert.Equal(0, FireUtility.ExtinguishFiresFromRain(map, 0f));
            Assert.Single(FiresOn(map));
        }

        [Fact]
        public void A_fire_standing_on_water_goes_out()
        {
            CoreMap map = NewMap(10, 10);
            var cell = new IntVec3(5, 0, 5);
            SpawnThing(map, cell, "WildPlant");
            Fire fire = IgniteDirect(map, cell, Fire.MinFireSize);
            map.terrainGrid.SetTerrain(cell, TerrainDefOf.WaterShallow);

            RunMapTicks(Fire.ComplexCalcsInterval + 1);

            Assert.True(fire.Destroyed);
        }

        // ---- Scribe ----

        [Fact]
        public void A_burning_map_round_trips_through_scribe()
        {
            CoreMap map = NewMap(12, 12);
            var wallCell = new IntVec3(4, 0, 4);
            SpawnThing(map, wallCell, "Wall");
            FireUtility.TryStartFireIn(wallCell, map, 1.2f);

            Pawn pawn = NewHuman("Smoulder");
            GenSpawn.Spawn(pawn, new IntVec3(8, 0, 8), map);
            pawn.TryAttachFire(0.4f);

            Assert.Equal(2, FiresOn(map).Count);

            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            List<Fire> fires = FiresOn(loaded);
            Assert.Equal(2, fires.Count);

            Fire onWall = fires.Single(f => f.parent == null);
            Assert.Equal(wallCell, onWall.Position);
            Assert.Equal(1.2f, onWall.fireSize, 3);
            Assert.True(onWall.Spawned);

            Fire onPawn = fires.Single(f => f.parent != null);
            Pawn loadedPawn = loaded.mapPawns.AllPawns.Single(p => p.name == "Smoulder");
            Assert.Same(loadedPawn, onPawn.parent);
            Assert.Equal(0.4f, onPawn.fireSize, 3);
            Assert.True(loadedPawn.IsBurning(), "A pawn who was alight when the game was saved is still alight.");
        }

        [Fact]
        public void A_reloaded_fire_keeps_burning_where_it_left_off()
        {
            CoreMap map = NewMap(12, 12);
            var cell = new IntVec3(4, 0, 4);
            SpawnThing(map, cell, "Wall");
            FireUtility.TryStartFireIn(cell, map, 1f);

            CoreMap loaded = Scribe.Load<CoreMap>(Scribe.SaveToString(map, "map"), "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Thing wall = loaded.edificeGrid[cell]!;
            int whole = wall.HitPoints;
            RunMapTicks(900);

            Assert.True(wall.HitPoints < whole, "The loaded fire should still be burning the wall it was on.");
        }
    }
}
