using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// A town opens a back street when its first street has no plot left (<see cref="DwellingPlanner.TryOpenStreet"/>,
    /// <see cref="SettlementCompound.BackStreetPlots"/>). The first street was the only one, and the home area the
    /// compound claimed bounded it, so a ruin, an outcrop or the map edge on a few of its plots capped how many
    /// households a town could ever house; on the era arc's seed 201 that held the Classical gate shut. Each test runs
    /// the real construction initiative pass by pass on a flat map, building whatever it plans.
    /// </summary>
    [Collection("GlobalDefs")]
    public class BackStreetTests : ContentTestBase
    {
        public BackStreetTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap() => new CoreMap(120, 120, TerrainDefOf.Soil);

        private static void AddCouples(Settlement settlement, int couples, string tag)
        {
            for (int i = 0; i < couples; i++)
            {
                Pawn a = NewHuman(tag + "A" + i), b = NewHuman(tag + "B" + i);
                settlement.AddCitizen(a);
                settlement.AddCitizen(b);
                Find.FamilyManager.FoundHousehold(a, b, 0);
            }
        }

        private static Settlement Band(int couples)
        {
            Find.ResearchManager.FinishProject(DefDatabase<ResearchProjectDef>.GetNamed("WindbreakShelters"));
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Village", 0);
            AddCouples(settlement, couples, "F");
            return settlement;
        }

        private static void Pass(Settlement settlement, CoreMap map, int pass)
        {
            Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
            SettlementConstructionInitiative.TickSettlement(settlement, map);
        }

        private static void BuildEverythingPlanned(CoreMap map)
        {
            Pawn builder = NewHuman("Builder");
            foreach (Blueprint bp in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>().ToList())
            {
                bp.ReplaceWithFrame().CompleteConstruction(builder);
            }
        }

        /// <summary>Passes, building whatever each plans, until one plans nothing; returns the next pass number.</summary>
        private static int GrowUntilSettled(Settlement settlement, CoreMap map, int from)
        {
            for (int pass = from; pass < from + 400; pass++)
            {
                int before = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count;
                Pass(settlement, map, pass);
                if (map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count == before) return pass + 1;
                BuildEverythingPlanned(map);
            }
            Assert.Fail("still planning after 400 passes");
            return 0;
        }

        /// <summary>Passes until the compound is planned and before any house is; returns the next pass number.</summary>
        private static int UntilCompound(Settlement settlement, CoreMap map)
        {
            for (int pass = 0; pass < 40; pass++)
            {
                Pass(settlement, map, pass);
                if (map.settlementCompound != null)
                {
                    Assert.Empty(DwellingUtility.DwellingsOn(map));
                    return pass + 1;
                }
            }
            Assert.Fail("no compound was planned");
            return 0;
        }

        /// <summary>A ruin's wall, owned by nobody, on the one cell every house on each first-street plot would
        /// cover: the corner at the street (<see cref="DwellingPlanner"/> sets a house at the street edge).</summary>
        private static void RuinTheFirstStreet(CoreMap map)
        {
            ThingDef wall = DefDatabase<ThingDef>.GetNamed("Wall");
            foreach ((CellRect plot, Rot4 rot) in map.settlementCompound!.DwellingPlots(int.MaxValue, map.Size))
            {
                IntVec3 corner = rot == Rot4.South ? new IntVec3(plot.maxX, 0, plot.maxZ) : new IntVec3(plot.minX, 0, plot.minZ);
                if (!GenGrid.InBounds(corner, map)) continue;
                GenSpawn.Spawn(ThingMaker.MakeThing(wall, DefDatabase<ThingDef>.GetNamed("WoodLog")), corner, map);
            }
        }

        private static bool OnFirstStreet(CoreMap map, Dwelling d) =>
            map.settlementCompound!.DwellingPlots(int.MaxValue, map.Size).Any(p => p.Plot.Contains(new IntVec3(d.footprint.minX, 0, d.footprint.minZ)));

        private static int BackStreetOf(CoreMap map, Dwelling d)
        {
            SettlementCompound compound = map.settlementCompound!;
            for (int street = 1; street <= compound.openedStreets; street++)
            {
                if (compound.BackStreetPlots(street, map.Size).Any(p => p.Plot.Contains(new IntVec3(d.footprint.minX, 0, d.footprint.minZ)))) return street;
            }
            return 0;
        }

        private static void AssertEveryHouseholdHoused(Settlement settlement, CoreMap map)
        {
            int households = DwellingUtility.Households(settlement).Count;
            IReadOnlyList<Dwelling> houses = DwellingUtility.DwellingsOn(map);
            Assert.Equal(households, houses.Count);
            Assert.All(houses, d => Assert.True(d.IsComplete(map), "a house at " + d.footprint + " is not whole"));
            Assert.Equal(settlement.Citizens.Count, DwellingUtility.HousedCitizens(settlement, map));
        }

        [Fact]
        public void A_town_whose_first_street_is_ruined_houses_its_households_on_back_streets_as_it_grows()
        {
            CoreMap map = NewMap();
            Settlement settlement = Band(couples: 10);
            int pass = UntilCompound(settlement, map);
            RuinTheFirstStreet(map);

            pass = GrowUntilSettled(settlement, map, pass);
            SettlementCompound compound = map.settlementCompound!;
            AssertEveryHouseholdHoused(settlement, map);
            Assert.All(DwellingUtility.DwellingsOn(map), d => Assert.False(OnFirstStreet(map, d), "a house stands on the ruined street"));
            Assert.All(DwellingUtility.DwellingsOn(map), d => Assert.Equal(1, BackStreetOf(map, d)));

            // The town grows past what one back street's claimed block holds: it opens the next.
            AddCouples(settlement, 16, "G");
            GrowUntilSettled(settlement, map, pass);
            AssertEveryHouseholdHoused(settlement, map);
            Assert.True(compound.openedStreets >= 2, "only " + compound.openedStreets + " back street(s) opened for 26 households");
            Assert.All(DwellingUtility.DwellingsOn(map), d => Assert.NotEqual(0, BackStreetOf(map, d)));
            Assert.All(DwellingUtility.DwellingsOn(map), d => Assert.All(d.footprint.Cells, c => Assert.True(map.areaManager.Home[c])));
        }

        [Fact]
        public void A_town_with_room_on_its_first_street_fills_its_plots_in_order_and_opens_no_other()
        {
            CoreMap map = NewMap();
            Settlement settlement = Band(couples: 10);
            int pass = UntilCompound(settlement, map);
            int homeBefore = -1;

            for (; pass < 400; pass++)
            {
                if (homeBefore < 0 && DwellingUtility.DwellingsOn(map).Count == 0) homeBefore = map.areaManager.Home.TrueCount;
                int before = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count;
                Pass(settlement, map, pass);
                if (map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count == before) break;
                BuildEverythingPlanned(map);
            }

            SettlementCompound compound = map.settlementCompound!;
            AssertEveryHouseholdHoused(settlement, map);
            Assert.Equal(0, compound.openedStreets);
            // On open ground the first street's plots are taken first-come in its own order, as before back streets.
            List<CellRect> expected = compound.DwellingPlots(10, map.Size).Select(p => p.Plot).ToList();
            List<CellRect> actual = DwellingUtility.DwellingsOn(map)
                .Select(d => expected.FirstOrDefault(p => p.Contains(new IntVec3(d.footprint.minX, 0, d.footprint.minZ)))).ToList();
            Assert.Equal(expected, actual);
            Assert.True(homeBefore > 0);
        }

        [Fact]
        public void The_opened_streets_round_trip_through_Scribe_and_the_loaded_town_carries_on_the_same()
        {
            CoreMap original = NewMap();
            Settlement settlement = Band(couples: 10);
            int pass = UntilCompound(settlement, original);
            RuinTheFirstStreet(original);
            pass = GrowUntilSettled(settlement, original, pass);
            Assert.Equal(1, original.settlementCompound!.openedStreets);

            string xml = Scribe.SaveToString(original, "map");
            CoreMap loaded = Scribe.Load<CoreMap>(xml, "map", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            Assert.Equal(1, loaded.settlementCompound!.openedStreets);

            static List<string> Homes(CoreMap map) => DwellingUtility.DwellingsOn(map)
                .Select(d => d.def.defName + "@" + d.footprint + "/" + d.rot + "#" + d.householdId).ToList();
            Assert.Equal(Homes(original), Homes(loaded));

            AddCouples(settlement, 4, "G");
            for (int i = 0; i < 6; i++, pass++)
            {
                Pass(settlement, original, pass);
                Pass(settlement, loaded, pass);
            }
            Assert.Equal(Homes(original), Homes(loaded));
            Assert.Equal(1, loaded.settlementCompound!.openedStreets);
            Assert.Equal(original.areaManager.Home.TrueCount, loaded.areaManager.Home.TrueCount);
        }

        [Fact]
        public void A_back_street_claims_its_ground_once_and_ground_taken_out_of_the_home_area_stays_out()
        {
            CoreMap map = NewMap();
            Settlement settlement = Band(couples: 10);
            int pass = UntilCompound(settlement, map);
            RuinTheFirstStreet(map);
            pass = GrowUntilSettled(settlement, map, pass);
            SettlementCompound compound = map.settlementCompound!;
            Assert.Equal(1, compound.openedStreets);

            // The player takes the rest of the first back street out of the town's bounds.
            CellRect block = compound.BackStreetBlock(1, map.Size);
            var erased = block.Cells.Where(c => DwellingUtility.DwellingAt(map, c) == null && map.areaManager.Home[c]).ToList();
            foreach (IntVec3 c in erased) map.areaManager.Home[c] = false;

            AddCouples(settlement, 2, "G");
            GrowUntilSettled(settlement, map, pass);
            AssertEveryHouseholdHoused(settlement, map);
            Assert.Equal(2, compound.openedStreets);
            Assert.Equal(2, DwellingUtility.DwellingsOn(map).Count(d => BackStreetOf(map, d) == 2));
        }
    }
}
