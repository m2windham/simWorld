using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;
using CorpseThing = SimWorld.Things.Corpse;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// The settlement buries its own (<see cref="GraveyardInitiative"/>, <see cref="GraveyardSiting"/>). A body of
    /// the settlement's own lying on the map gets a grave in the graveyard's rows, far enough from the beds
    /// that nobody sleeps beside the dead; the living get none; the bodies of strangers get one small dumping
    /// stockpile sited out of the way. All of it is derived from what stands on the map, so it survives a save
    /// with nothing of its own to Scribe.
    /// <para/>
    /// The figures in <see cref="GraveyardTuning"/> are this port's own (RimWorld's player decides all of it), so
    /// nothing here asserts one: only the relations they exist to produce — rows, clearance, one dump, bounded
    /// growth.
    /// </summary>
    public class GraveyardInitiativeTests : ContentTestBase
    {
        public GraveyardInitiativeTests(CoreContentFixture content) : base(content)
        {
            CorpseDefGenerator.EnsureGenerated();
            Ours = new Faction(DefDatabase<FactionDef>.GetNamed("PlayerCivilization"), "Ours", "F_Ours");
            Theirs = new Faction(DefDatabase<FactionDef>.GetNamed("RoughOutlanders"), "Theirs", "F_Theirs");
        }

        private Faction Ours { get; }

        private Faction Theirs { get; }

        private static CoreMap NewMap(int size = 60) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private Settlement NewSettlement(Faction? faction = null) =>
            new Settlement(WorldObjectDefOf.Settlement, 0, faction ?? Ours, "Restinghome", 0);

        private static ThingDef GraveDef => GraveDefOf.Grave;

        private Pawn Person(CoreMap map, IntVec3 cell, Faction? faction, string name)
        {
            Pawn p = NewHuman(name);
            p.faction = faction;
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private CorpseThing Dead(CoreMap map, IntVec3 cell, Faction? faction, string name = "Dead")
        {
            Pawn p = Person(map, cell, faction, name);
            p.health.Kill(null, null);
            Assert.NotNull(p.corpse);
            return p.corpse!;
        }

        private static List<Thing> GraveSites(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                .Where(t => t is Blueprint bp && ReferenceEquals(bp.EntityToBuild, GraveDef))
                .Concat(map.listerThings.ThingsOfDef(GraveDef))
                .ToList();

        private static void SpawnBeds(CoreMap map, int count, IntVec3 around)
        {
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell = new IntVec3(around.x - 6 + (i % 5) * 3, 0, around.z - 4 + (i / 5) * 3);
                GenSpawn.Spawn(ThingMaker.MakeThing(ConstructionThingDefOf.Bed), cell, map);
            }
        }

        private static int Chebyshev(IntVec3 a, IntVec3 b) => System.Math.Max(System.Math.Abs(a.x - b.x), System.Math.Abs(a.z - b.z));

        // ---- the living get nothing ----

        [Fact]
        public void A_settlement_with_nobody_dead_places_nothing_and_lays_no_dump()
        {
            CoreMap map = NewMap();
            for (int i = 0; i < 6; i++) Person(map, new IntVec3(28 + i, 0, 30), Ours, "Citizen" + i);
            Settlement settlement = NewSettlement();

            GraveyardInitiative.Run(settlement, map);

            Assert.Empty(GraveSites(map));
            Assert.Null(GraveyardInitiative.DumpOf(map));
        }

        [Fact]
        public void An_animals_body_gets_no_grave_and_no_dump()
        {
            CoreMap map = NewMap();
            var dog = new Pawn(Husky, "Rex");
            GenSpawn.Spawn(dog, new IntVec3(10, 0, 10), map);
            dog.health.Kill(null, null);

            GraveyardInitiative.Run(NewSettlement(), map);

            Assert.Empty(GraveSites(map));
            Assert.Null(GraveyardInitiative.DumpOf(map));
        }

        [Fact]
        public void A_settlement_that_is_not_the_players_is_left_alone()
        {
            CoreMap map = NewMap();
            Dead(map, new IntVec3(10, 0, 10), Theirs, "Theirs");

            GraveyardInitiative.Run(NewSettlement(Theirs), map);

            Assert.Empty(GraveSites(map));
            Assert.Null(GraveyardInitiative.DumpOf(map));
        }

        // ---- its own dead ----

        [Fact]
        public void A_dead_citizen_gets_one_grave_planned_for_them()
        {
            CoreMap map = NewMap();
            CorpseThing body = Dead(map, new IntVec3(30, 0, 30), Ours, "Bob");
            Settlement settlement = NewSettlement();

            GraveyardInitiative.Run(settlement, map);

            Assert.Single(GraveSites(map));
            GraveyardInitiative.Run(settlement, map);
            Assert.Single(GraveSites(map));
            Assert.True(body.Spawned);
        }

        [Fact]
        public void One_grave_per_body_and_no_more_than_the_pass_allows()
        {
            CoreMap map = NewMap();
            for (int i = 0; i < 5; i++) Dead(map, new IntVec3(30 + i, 0, 30), Ours, "Dead" + i);
            Settlement settlement = NewSettlement();

            GraveyardInitiative.Run(settlement, map);
            Assert.Equal(GraveyardTuning.MaxGravesPerPass, GraveSites(map).Count);

            for (int pass = 0; pass < 6; pass++) GraveyardInitiative.Run(settlement, map);
            Assert.Equal(5, GraveSites(map).Count);
        }

        [Fact]
        public void A_grave_that_stands_empty_is_not_planned_for_again()
        {
            CoreMap map = NewMap();
            var grave = (Building_Grave)ThingMaker.MakeThing(GraveDef);
            GenSpawn.Spawn(grave, new IntVec3(5, 0, 5), map);
            Dead(map, new IntVec3(30, 0, 30), Ours, "Bob");

            GraveyardInitiative.Run(NewSettlement(), map);

            Assert.Single(GraveSites(map));
            Assert.Same(grave, GraveSites(map).Single());
        }

        [Fact]
        public void A_grave_set_to_refuse_the_settlements_own_is_no_grave_for_them()
        {
            CoreMap map = NewMap();
            var grave = (Building_Grave)ThingMaker.MakeThing(GraveDef);
            GenSpawn.Spawn(grave, new IntVec3(5, 0, 5), map);
            grave.GetStoreSettings().disallowedSpecialFilters.Add(SpecialThingFilter.CorpsesColonist);
            Dead(map, new IntVec3(30, 0, 30), Ours, "Bob");

            GraveyardInitiative.Run(NewSettlement(), map);

            Assert.Equal(2, GraveSites(map).Count);
        }

        // ---- where: rows, clear of the beds ----

        [Fact]
        public void The_graves_stand_in_rows_on_one_grid_as_a_compact_patch_and_none_is_among_the_beds()
        {
            CoreMap map = NewMap(80);
            IntVec3 middle = new IntVec3(40, 0, 40);
            SpawnBeds(map, 10, middle);
            for (int i = 0; i < 14; i++) Dead(map, new IntVec3(10 + i, 0, 10), Ours, "Dead" + i);
            Settlement settlement = NewSettlement();

            for (int pass = 0; pass < 12; pass++) GraveyardInitiative.Run(settlement, map);

            List<Thing> sites = GraveSites(map);
            Assert.Equal(14, sites.Count);
            Assert.Equal(14, sites.Select(g => g.Position).Distinct().Count());

            // One grid: every grave in the game is on it, so the rows line up whatever order they were dug in.
            foreach (Thing grave in sites)
            {
                Assert.Equal(0, grave.Position.x % GraveyardTuning.ColumnPitch);
                Assert.Equal(0, grave.Position.z % GraveyardTuning.RowPitch);
            }

            // Rows: graves share a z, and there is more than one row. Not a line, and not a scatter.
            IEnumerable<IGrouping<int, Thing>> rows = sites.GroupBy(g => g.Position.z).ToList();
            Assert.True(rows.Count() > 1, "fourteen graves make more than one row");
            Assert.True(rows.Any(r => r.Count() > 1), "a row has more than one grave in it");

            // Compact: the patch they stand in is not much bigger than the graves need. Scattered across the
            // home area they would fill several times this.
            Assert.True(GraveyardSiting.TryGetBounds(map, out CellRect bounds));
            int slotArea = GraveyardTuning.ColumnPitch * GraveyardTuning.RowPitch;
            Assert.True(bounds.Area <= 4 * sites.Count * slotArea,
                "fourteen graves are spread over " + bounds.Area + " cells (" + bounds.Width + "x" + bounds.Height + ")");

            List<IntVec3> bedCells = GraveyardSiting.BedCells(map);
            Assert.NotEmpty(bedCells);
            foreach (Thing grave in sites)
            {
                foreach (IntVec3 gc in GenAdj.OccupiedRect(grave.Position, grave.Rotation, GraveDef.size).Cells)
                {
                    foreach (IntVec3 bc in bedCells)
                    {
                        Assert.True(Chebyshev(gc, bc) >= GraveyardTuning.BedClearance,
                            "grave cell " + gc + " is among the beds (" + bc + ")");
                    }
                }
            }
        }

        [Fact]
        public void The_graveyard_is_near_the_settlement_not_across_the_map()
        {
            CoreMap map = NewMap(120);
            IntVec3 middle = new IntVec3(60, 0, 60);
            SpawnBeds(map, 10, middle);
            Dead(map, new IntVec3(10, 0, 10), Ours, "Bob");

            GraveyardInitiative.Run(NewSettlement(), map);

            IntVec3 grave = GraveSites(map).Single().Position;
            Assert.True(Chebyshev(grave, middle) < 30, "the first grave is " + Chebyshev(grave, middle) + " cells from the beds' middle");
        }

        [Fact]
        public void The_graveyard_is_inside_the_home_area_when_the_settlement_has_one()
        {
            CoreMap map = NewMap(80);
            SpawnBeds(map, 6, new IntVec3(40, 0, 40));
            foreach (IntVec3 c in new CellRect(20, 20, 40, 40).Cells) map.areaManager.Home[c] = true;
            Dead(map, new IntVec3(10, 0, 10), Ours, "Bob");

            GraveyardInitiative.Run(NewSettlement(), map);

            foreach (IntVec3 c in GenAdj.OccupiedRect(GraveSites(map).Single().Position, default, GraveDef.size).Cells)
            {
                Assert.True(map.areaManager.Home[c], "the grave stands outside the home area at " + c);
            }
        }

        [Fact]
        public void A_graveyard_with_no_room_inside_the_home_area_is_laid_outside_it_rather_than_not_at_all()
        {
            CoreMap map = NewMap(80);
            SpawnBeds(map, 6, new IntVec3(40, 0, 40));
            foreach (IntVec3 c in new CellRect(36, 36, 8, 8).Cells) map.areaManager.Home[c] = true;
            Dead(map, new IntVec3(10, 0, 10), Ours, "Bob");

            GraveyardInitiative.Run(NewSettlement(), map);

            Assert.Single(GraveSites(map));
        }

        [Fact]
        public void The_same_game_lays_the_same_graveyard_twice()
        {
            List<IntVec3> Lay()
            {
                CoreMap map = NewMap(80);
                SpawnBeds(map, 10, new IntVec3(40, 0, 40));
                for (int i = 0; i < 6; i++) Dead(map, new IntVec3(10 + i, 0, 10), Ours, "Dead" + i);
                Settlement settlement = NewSettlement();
                for (int pass = 0; pass < 6; pass++) GraveyardInitiative.Run(settlement, map);
                return GraveSites(map).Select(g => g.Position).OrderBy(p => p.z).ThenBy(p => p.x).ToList();
            }

            Assert.Equal(Lay(), Lay());
        }

        // ---- kept by what stands, not by a field ----

        [Fact]
        public void A_saved_and_loaded_map_carries_on_with_the_same_graveyard()
        {
            CoreMap map = NewMap(80);
            SpawnBeds(map, 8, new IntVec3(40, 0, 40));
            for (int i = 0; i < 4; i++) Dead(map, new IntVec3(10 + i, 0, 10), Ours, "Dead" + i);
            Settlement settlement = NewSettlement();
            for (int pass = 0; pass < 2; pass++) GraveyardInitiative.Run(settlement, map);
            Assert.Equal(4, GraveSites(map).Count);
            Assert.True(GraveyardSiting.TryGetBounds(map, out CellRect boundsBefore));

            // The bodies are taken off before the save: a pawn's faction is a reference to something saved
            // elsewhere, and this save is of one map. What is being tested is the graveyard, which is made of
            // the graves and blueprints that stand, not of who lies about.
            foreach (Thing body in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).ToList()) body.Destroy();

            // More deaths, after the save: the lattice has to be found again from what stands.
            string xml = Scribe.SaveToString(map, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            // Five bodies for four graves already planned: one more is wanted, and it has to be the next slot of
            // the same rows, not a fresh graveyard.
            for (int i = 0; i < 5; i++) Dead(loaded, new IntVec3(20 + i, 0, 10), Ours, "Late" + i);
            GraveyardInitiative.Run(settlement, loaded);

            Assert.Equal(5, GraveSites(loaded).Count);
            Assert.True(GraveyardSiting.TryGetBounds(loaded, out CellRect boundsAfter));
            Assert.Equal(5, GraveSites(loaded).Select(g => g.Position).Distinct().Count());
            foreach (Thing g in GraveSites(loaded))
            {
                Assert.Equal(0, g.Position.x % GraveyardTuning.ColumnPitch);
                Assert.Equal(0, g.Position.z % GraveyardTuning.RowPitch);
            }
            // The fifth grave joined the patch the first four made; it did not start another elsewhere.
            Assert.True(boundsAfter.Area <= 3 * boundsBefore.Area, "the graveyard moved: " + boundsBefore + " then " + boundsAfter);
        }

        // ---- strangers ----

        [Fact]
        public void A_strangers_body_gets_one_dumping_stockpile_sited_out_of_the_way()
        {
            CoreMap map = NewMap(80);
            SpawnBeds(map, 10, new IntVec3(40, 0, 40));
            Dead(map, new IntVec3(10, 0, 10), Theirs, "Raider");
            Settlement settlement = NewSettlement();

            GraveyardInitiative.Run(settlement, map);

            Zone_Stockpile? dump = GraveyardInitiative.DumpOf(map);
            Assert.NotNull(dump);
            Assert.Equal(GraveyardTuning.DumpWidth * GraveyardTuning.DumpHeight, dump!.CellCount);
            Assert.Empty(GraveSites(map));

            // Bodies, human ones, and nothing else.
            Assert.True(dump.filter.Allows(CorpseDefGenerator.CorpseDefFor(Human)));
            Assert.False(dump.filter.Allows(CorpseDefGenerator.CorpseDefFor(Husky)));
            Assert.False(dump.filter.Allows(DefDatabase<ThingDef>.GetNamed("WoodLog")));

            // Out of the way: clear of the beds and of the hub.
            IntVec3 hub = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
            foreach (IntVec3 cell in dump.Cells)
            {
                foreach (IntVec3 bc in GraveyardSiting.BedCells(map))
                {
                    Assert.True(Chebyshev(cell, bc) >= GraveyardTuning.DumpBedClearance, "the dump cell " + cell + " is among the beds");
                }
                Assert.True(Chebyshev(cell, hub) >= GraveyardTuning.HubClearance, "the dump cell " + cell + " is on the hub");
            }

            // Once. A second stranger does not lay a second dump.
            Dead(map, new IntVec3(12, 0, 10), Theirs, "Another");
            GraveyardInitiative.Run(settlement, map);
            Assert.Equal(1, map.zoneManager.AllZones.Count(GraveyardInitiative.IsDump));
        }

        [Fact]
        public void The_dump_grows_with_the_dead_and_stops_at_its_bound()
        {
            CoreMap map = NewMap(80);
            Settlement settlement = NewSettlement();
            Dead(map, new IntVec3(10, 0, 10), Theirs, "First");
            GraveyardInitiative.Run(settlement, map);
            Zone_Stockpile dump = GraveyardInitiative.DumpOf(map)!;
            int initial = dump.CellCount;

            for (int i = 0; i < initial + 6; i++) Dead(map, new IntVec3(5 + i, 0, 20), Theirs, "Raider" + i);
            GraveyardInitiative.Run(settlement, map);
            Assert.True(dump.CellCount > initial, "more bodies than cells has to grow the dump");
            Assert.True(dump.CellCount <= GraveyardTuning.MaxDumpCells);

            for (int i = 0; i < 60; i++) Dead(map, new IntVec3(2 + i % 50, 0, 30 + i / 50), Theirs, "Horde" + i);
            for (int pass = 0; pass < 20; pass++) GraveyardInitiative.Run(settlement, map);
            Assert.Equal(GraveyardTuning.MaxDumpCells, dump.CellCount);
        }

        [Fact]
        public void The_dump_is_not_counted_as_room_for_goods_or_as_where_the_huts_go()
        {
            CoreMap map = NewMap(80);
            Dead(map, new IntVec3(10, 0, 10), Theirs, "Raider");
            GraveyardInitiative.Run(NewSettlement(), map);
            Assert.NotNull(GraveyardInitiative.DumpOf(map));

            Assert.Equal(0, SimWorld.Economy.SettlementStockInitiative.EmptyStockpileCells(map));
        }

        // ---- the dead are carried where the settlement sends them ----

        [Fact]
        public void A_citizen_dead_on_the_map_is_dug_in_and_carried_to_by_the_settlements_own_hands()
        {
            CoreMap map = NewMap(60);
            Settlement settlement = NewSettlement();
            Pawn[] workers = new[] { "A", "B", "C" }.Select((n, i) => Person(map, new IntVec3(28 + i, 0, 30), Ours, n)).ToArray();
            foreach (Pawn w in workers) settlement.AddCitizen(w);
            Thing wood = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("WoodLog"));
            wood.stackCount = 40;
            GenSpawn.Spawn(wood, new IntVec3(32, 0, 30), map);
            CorpseThing body = Dead(map, new IntVec3(20, 0, 20), Ours, "Bob");
            Assert.True(body.Spawned);

            for (int tick = 0; tick < 12000 && body.Spawned; tick += 250)
            {
                GraveyardInitiative.Run(settlement, map);
                RunTicks(250, workers);
            }

            Assert.False(body.Spawned, "the citizens should have dug a grave and carried Bob to it");
            Building_Grave grave = (Building_Grave)map.listerThings.ThingsOfDef(GraveDef).Single();
            Assert.Same(body, grave.Corpse);
            Assert.Equal("grave of Bob", grave.Label);
            Assert.Single(GraveSites(map));
        }
    }
}
