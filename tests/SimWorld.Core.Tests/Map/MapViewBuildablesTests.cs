using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Map.View;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Map
{
    /// <summary>
    /// The build list (<see cref="MapViewSnapshot.CaptureBuildables()"/>, host UX request after playtest 4) and the
    /// command it feeds (<see cref="MapCommands.PlaceBlueprint"/>) on real generated settlements: the list is gated
    /// by research and era as the simulation gates, the command accepts everything the list offers and refuses
    /// everything it does not, with the list's own reason.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MapViewBuildablesTests : ContentTestBase
    {
        public MapViewBuildablesTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Settlement Opened(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        /// <summary>Every project of the eras before Bronze finished as setup: a town in the Bronze age that has
        /// not yet learned any of it.</summary>
        private static void IntoBronze()
        {
            ResearchManager research = Find.ResearchManager;
            int bronze = DefDatabase<EraDef>.GetNamed("Bronze").order;
            foreach (EraDef era in DefDatabase<EraDef>.AllDefsListForReading.Where(e => e.order < bronze))
            {
                foreach (ResearchProjectDef p in era.Projects) research.SetProjectFinishedForSetup(p);
            }
            Assert.Equal("Bronze", research.CurrentEra!.defName);
        }

        private static IEnumerable<ThingDef> AllBuildable() =>
            DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.category == ThingCategory.Blueprint && d.entityToBuild is ThingDef)
                .Select(d => (ThingDef)d.entityToBuild!)
                .Distinct();

        /// <summary>A cell the def fits on near the band, clear of pawns.</summary>
        private static IntVec3 FreeCell(CoreMap map, ThingDef def, IntVec3 near)
        {
            for (int r = 0; r < 60; r++)
            {
                foreach (IntVec3 c in CellRect.CenteredOn(near, r).EdgeCells)
                {
                    if (!GenGrid.InBounds(c, map) || !GenConstruct.CanPlaceBlueprintAt(def, c, map, out _)) continue;
                    if (GenAdj.OccupiedRect(c, Rot4.North, def.size).Cells.Any(x => map.thingGrid.ThingsListAt(x).Count > 0)) continue;
                    return c;
                }
            }
            throw new System.InvalidOperationException("no free cell for " + def.defName);
        }

        private static IntVec3 Near(Settlement s, CoreMap map) => s.Citizens.First(p => p.Spawned && p.Map == map).Position;

        [Fact]
        public void A_bronze_age_town_is_offered_its_own_age_and_none_later()
        {
            Settlement settlement = Opened("buildables-bronze");
            IntoBronze();
            BuildCatalogue catalogue = MapViewSnapshot.CaptureBuildables();

            Assert.True(catalogue.HasMap);
            Assert.Equal("Bronze", catalogue.EraDefName);
            int bronze = DefDatabase<EraDef>.GetNamed("Bronze").order;
            foreach (BuildableOption o in catalogue.Options)
            {
                EraDef? era = o.EraDefName == null ? null : DefDatabase<EraDef>.GetNamed(o.EraDefName);
                Assert.True(era == null || era.order <= bronze, o.DefName + " is from " + o.EraDefName + ", a later age");
            }

            // More than the four the host hard-coded: the earlier ages' learned buildings, placeable.
            Assert.True(catalogue.Find("Granary")!.CanPlace, "the granary is learned in the age before");
            Assert.True(catalogue.Find("Bed")!.CanPlace);
            Assert.True(catalogue.Options.Count(o => o.CanPlace) > 4);

            // The age's own, not yet learned: listed, greyed, and the reason names the research.
            BuildableOption granite = catalogue.Find("WallGranite")!;
            Assert.False(granite.CanPlace);
            Assert.Equal("Bronze", granite.EraDefName);
            Assert.Contains(DefDatabase<ResearchProjectDef>.GetNamed("Masonry").LabelCap, granite.Reason);
            Assert.Contains(DefDatabase<ResearchProjectDef>.GetNamed("Masonry").LabelCap, granite.ResearchNeeded);
            Assert.NotNull(catalogue.Find("SmeltingFurnace"));

            // A later age's: not listed at all.
            Assert.Null(catalogue.Find("Bloomery"));
            Assert.Null(catalogue.Find("Horreum"));
            Assert.Null(catalogue.Find("ElectricSmelter"));

            // Learning it opens it, on the list and in the command alike.
            CoreMap map = settlement.InteriorMap!;
            IntVec3 cell = FreeCell(map, Def("WallGranite"), Near(settlement, map));
            MapCommandResult refused = MapCommands.PlaceBlueprint("WallGranite", cell);
            Assert.Equal(MapCommandOutcome.Refused, refused.Outcome);
            Assert.Equal(granite.Reason, refused.Reason);

            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("Masonry"));
            BuildableOption learned = MapViewSnapshot.CaptureBuildables().Find("WallGranite")!;
            Assert.True(learned.CanPlace);
            Assert.Null(learned.Reason);
            Assert.Empty(learned.ResearchNeeded);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.PlaceBlueprint("WallGranite", cell).Outcome);
        }

        /// <summary>The lever test on the build list: what it offers the command accepts, and what it does not
        /// offer the command refuses with the reason — in the first age and in the Bronze age.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void The_command_accepts_every_offer_and_refuses_everything_else(bool bronze)
        {
            Settlement settlement = Opened(bronze ? "buildables-lever-bronze" : "buildables-lever-stone");
            if (bronze) IntoBronze();
            CoreMap map = settlement.InteriorMap!;
            IntVec3 near = Near(settlement, map);
            BuildCatalogue catalogue = MapViewSnapshot.CaptureBuildables();

            var offered = new HashSet<string>(catalogue.Options.Where(o => o.CanPlace).Select(o => o.DefName));
            Assert.NotEmpty(offered);
            foreach (ThingDef def in AllBuildable())
            {
                IntVec3 cell = FreeCell(map, def, near);
                MapCommandResult result = MapCommands.PlaceBlueprint(def.defName, cell);
                if (offered.Contains(def.defName))
                {
                    Assert.True(result.Outcome == MapCommandOutcome.Done, def.defName + " is offered but " + result.Reason);
                }
                else
                {
                    Assert.True(result.Outcome == MapCommandOutcome.Refused, def.defName + " is not offered but " + result.Outcome);
                    Assert.False(string.IsNullOrEmpty(result.Reason));
                    BuildableOption? greyed = catalogue.Find(def.defName);
                    if (greyed != null) Assert.Equal(greyed.Reason, result.Reason);
                }
            }
        }

        [Fact]
        public void An_option_carries_its_size_materials_and_the_stuffs_this_age_can_use()
        {
            Settlement settlement = Opened("buildables-materials");
            CoreMap map = settlement.InteriorMap!;
            BuildCatalogue catalogue = MapViewSnapshot.CaptureBuildables();

            BuildableOption bed = catalogue.Find("Bed")!;
            Assert.Equal(Def("Bed").size.x, bed.Size.x);
            Assert.Equal(Def("Bed").size.z, bed.Size.z);
            Assert.Equal(Def("Bed").costStuffCount, bed.StuffCount);
            Assert.Equal("WoodLog", bed.DefaultStuffDefName);
            Assert.Contains(bed.Stuffs, s => s.DefName == "WoodLog" && s.Count == bed.StuffCount);
            Assert.Null(bed.EraDefName);

            BuildableOption hut = catalogue.Find("StorageHut")!;
            MaterialCost logs = Assert.Single(hut.Cost);
            Assert.Equal("WoodLog", logs.DefName);
            Assert.Equal(Def("StorageHut").costList![0].count, logs.Count);
            Assert.Equal(0, hut.StuffCount);
            Assert.Empty(hut.Stuffs);
            Assert.Null(hut.DefaultStuffDefName);

            // A later age's stuff nobody holds is neither offered nor accepted; once some is held it is both.
            Assert.DoesNotContain(bed.Stuffs, s => s.DefName == "Steel");
            IntVec3 near = Near(settlement, map);
            IntVec3 cell = FreeCell(map, Def("Bed"), near);
            MapCommandResult refused = MapCommands.PlaceBlueprint("Bed", cell, Rot4.North, "Steel");
            Assert.Equal(MapCommandOutcome.Refused, refused.Outcome);
            Assert.Contains(Def("Steel").LabelCap, refused.Reason);

            Thing steel = ThingMaker.MakeThing(Def("Steel"));
            steel.stackCount = 10;
            GenSpawn.Spawn(steel, FreeCell(map, Def("Steel"), near + new IntVec3(6, 0, 0)), map);
            MaterialCost held = MapViewSnapshot.CaptureBuildables().Find("Bed")!.Stuffs.Single(s => s.DefName == "Steel");
            Assert.Equal(10, held.Held);
            Assert.Equal(MapCommandOutcome.Done, MapCommands.PlaceBlueprint("Bed", cell, Rot4.North, "Steel").Outcome);
        }

        [Fact]
        public void With_no_settlement_open_the_list_says_why()
        {
            Opened("buildables-absent");
            GodCommands.ClearSettlementFocus();
            BuildCatalogue catalogue = MapViewSnapshot.CaptureBuildables();
            Assert.False(catalogue.HasMap);
            Assert.False(string.IsNullOrEmpty(catalogue.AbsenceReason));
            Assert.Empty(catalogue.Options);
        }
    }
}
