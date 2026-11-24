using System.Collections.Generic;
using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Map.View;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using Xunit;
using CoreMap = SimWorld.Map.Map;
using EraAvailability = global::SimWorld.Research.EraAvailability;

namespace SimWorld.Tests.Research
{
    /// <summary>
    /// A stockpile's goods list shows what the era can have. The playtest's stone-age granary listed bronze,
    /// iron and a longsword.
    /// </summary>
    [Collection("GlobalDefs")]
    public class EraAvailabilityTests : ContentTestBase
    {
        public EraAvailabilityTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static Settlement OpenedSettlement(string seed)
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            return settlement;
        }

        [Fact]
        public void A_things_era_is_read_from_what_makes_it()
        {
            // Bronze is smelted under Bronze research, iron later, the longsword later still.
            Assert.NotNull(EraAvailability.EraOf(Def("Bronze")));
            Assert.True(EraAvailability.EraOf(Def("Bronze"))!.order < EraAvailability.EraOf(Def("Iron"))!.order);
            Assert.True(EraAvailability.EraOf(Def("Iron"))!.order <= EraAvailability.EraOf(Def("MeleeWeapon_LongSword"))!.order);
            // Gathered, dug or felled: no era of its own.
            Assert.Null(EraAvailability.EraOf(Def("WoodLog")));
            Assert.Null(EraAvailability.EraOf(Def("ChunkSandstone")));
        }

        [Fact]
        public void A_stone_age_stockpile_lists_no_bronze_iron_or_longsword_but_keeps_its_own_goods()
        {
            Settlement settlement = OpenedSettlement("stone-age-granary");
            CoreMap map = settlement.InteriorMap!;
            foreach (string name in new[] { "Bronze", "Iron", "MeleeWeapon_LongSword" })
            {
                Assert.False(EraAvailability.IsAvailableInCurrentEra(Def(name)), name);
            }

            List<IntVec3> cells = map.AllCells.Where(c => GenGrid.Standable(c, map) && map.zoneManager.ZoneAt(c) == null).Take(4).ToList();
            Assert.Equal(MapCommandOutcome.Done, MapCommands.MarkStockpile(cells).Outcome);
            ZoneView pile = MapViewSnapshot.CaptureLevers(map).Zones.Single(z => z.Kind == ZoneKind.Stockpile && z.Cells.Contains(cells[0]));
            var listed = new HashSet<string>(pile.Allowed.Select(o => o.DefName));

            Assert.DoesNotContain("Bronze", listed);
            Assert.DoesNotContain("Iron", listed);
            Assert.DoesNotContain("MeleeWeapon_LongSword", listed);
            Assert.Contains("WoodLog", listed);
            Assert.Contains("ChunkSandstone", listed);

            // Hidden from the list, not refused by the filter: a bronze ingot a trader brings is still stored.
            Zone_Stockpile zone = Assert.IsType<Zone_Stockpile>(map.zoneManager.ZoneAt(cells[0]));
            Assert.True(zone.filter.Allows(Def("Bronze")));
        }

        [Fact]
        public void Reaching_the_era_brings_its_things_into_view()
        {
            OpenedSettlement("bronze-age-granary");
            ThingDef bronze = Def("Bronze");
            var bronzeEra = EraAvailability.EraOf(bronze)!;
            foreach (var project in DefDatabase<global::SimWorld.Research.ResearchProjectDef>.AllDefsListForReading.Where(p => p.era != null && p.era.order < bronzeEra.order))
            {
                Find.ResearchManager.SetProjectFinishedForSetup(project);
            }
            Assert.True(EraAvailability.WorkingEra()!.order >= bronzeEra.order);
            Assert.True(EraAvailability.IsAvailableInCurrentEra(bronze));
        }
    }
}
