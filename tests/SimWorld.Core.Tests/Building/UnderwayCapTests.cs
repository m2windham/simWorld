using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// The cap on houses going up (<see cref="DwellingTuning.MaxUnderway"/>, read through
    /// <see cref="DwellingUtility.CountUnderway"/>): new houses and renewals together never pass it, and a finished
    /// house being repaired holds none of it. Counted, two quake-shaken houses whose stone walls stalled held the cap
    /// for hundreds of days, and the town built no house and renewed none while its households moved into the
    /// dormitory (integ-r11, seeds 20:99 and 20:101).
    /// </summary>
    [Collection("GlobalDefs")]
    public class UnderwayCapTests : ContentTestBase
    {
        public UnderwayCapTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ResearchProjectDef Project(string name) => DefDatabase<ResearchProjectDef>.GetNamed(name);

        /// <summary>Houses that have never stood whole: the ones going up, renewals' replacements among them.</summary>
        private static int NeverFinished(CoreMap map) =>
            DwellingUtility.DwellingsOn(map).Count(d => !d.beingDemolished && d.completedTick == global::SimWorld.Building.Building.UnknownBuiltTick && !d.IsComplete(map));

        private static void AssertUnderCap(CoreMap map) =>
            Assert.InRange(NeverFinished(map), 0, DwellingTuning.MaxUnderway);

        /// <summary>Knocks one wall out of each of <paramref name="houses"/>, as a quake does.</summary>
        private static void Shake(CoreMap map, IEnumerable<Dwelling> houses)
        {
            foreach (Dwelling d in houses)
            {
                map.edificeGrid[d.Parts.First(p => p.Kind == DwellingPartKind.Wall).Cell]!.Destroy(DestroyMode.KillFinalize);
                Assert.False(d.IsComplete(map));
            }
        }

        // ---- new houses: a band on a bare map ----

        private static Settlement Band(int couples)
        {
            Find.ResearchManager.FinishProject(Project("WindbreakShelters"));
            var settlement = new Settlement(WorldObjectDefOf.Settlement, 0, null, "Village", 0);
            for (int i = 0; i < couples; i++)
            {
                Pawn a = NewHuman("A" + i), b = NewHuman("B" + i);
                settlement.AddCitizen(a);
                settlement.AddCitizen(b);
                Find.FamilyManager.FoundHousehold(a, b, 0);
            }
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

        /// <summary>Passes, building whatever each one planned, until a pass plans nothing; the cap is checked after
        /// every pass.</summary>
        private static void GrowUntilSettled(Settlement settlement, CoreMap map)
        {
            for (int pass = 0; pass < 400; pass++)
            {
                int before = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count;
                Pass(settlement, map, pass);
                AssertUnderCap(map);
                if (map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Count == before) return;
                BuildEverythingPlanned(map);
            }
            Assert.Fail("still planning after 400 passes");
        }

        [Fact]
        public void Houses_under_repair_do_not_hold_back_a_new_house()
        {
            var map = new CoreMap(120, 120, TerrainDefOf.Soil);
            Settlement settlement = Band(couples: 10);
            GrowUntilSettled(settlement, map);
            List<Dwelling> shaken = DwellingUtility.DwellingsOn(map).Take(DwellingTuning.MaxUnderway).ToList();
            Assert.All(shaken, d => Assert.NotEqual(global::SimWorld.Building.Building.UnknownBuiltTick, d.completedTick));
            Shake(map, shaken);
            Find.FamilyManager.FoundHousehold(settlement.Citizens[0], settlement.Citizens[3], 0);

            Pass(settlement, map, 1000);

            Assert.All(shaken, d => Assert.False(d.IsComplete(map)));
            Assert.Equal(11, DwellingUtility.DwellingsOn(map).Count);
            Assert.Equal(1, DwellingUtility.CountUnderway(map));
            AssertUnderCap(map);
        }

        // ---- renewal: a generated settlement, as RenewalHomesTests founds one ----

        /// <summary>A tribal band whose first <paramref name="houses"/> houses stand as windbreak shelters, a bed for
        /// everybody, food in store, and the town moved on into the Agrarian age with its longhouse learned: every
        /// house but the founding one is due for renewal, and most households still have none.</summary>
        private static (Settlement Settlement, List<Dwelling> Houses) Founded(string seed, int houses)
        {
            CoreMap.ResetMapIdCounter();
            Game game = Game.NewGame(DefDatabase<ScenarioDef>.GetNamed("TribalStart").scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            Faction faction = settlement.Citizens.First(p => p.Spawned).Faction!;
            CoreMap map = settlement.InteriorMap!;
            for (int i = 0; i < 2000 && map.settlementCompound == null; i++) game.TickManager.DoSingleTick();
            Assert.NotNull(map.settlementCompound);

            ResearchManager research = Find.ResearchManager;
            research.SetProjectFinishedForSetup(Project("WindbreakShelters"));
            research.SetProjectFinishedForSetup(Project("PitDwellings"));
            List<int> households = DwellingUtility.Households(settlement).Keys.ToList();
            var raised = new List<Dwelling>();
            for (int i = 0; i < houses; i++) raised.Add(ShelterKit.RaiseOnPlot(map, ShelterKit.Rung("House_Paleo"), households[i], faction)!);
            foreach (ResearchProjectDef p in DefDatabase<EraDef>.GetNamed("SticksAndStones").Projects) research.SetProjectFinishedForSetup(p);
            research.SetProjectFinishedForSetup(Project("Shelter"));
            Assert.Equal("House_Neo", DwellingUtility.PreferredDwellingDef()!.defName);

            BedEveryone(settlement, map, faction);
            settlement.AddStore(Def("MealSimple"), 600);
            DwellingUtility.AssignHomes(settlement, map);
            DwellingPlanner.Plan(settlement, map, dormitoryCovered: false); // ids, completion ticks, the founding house
            Assert.Null(RenewalInitiative.WaitReason(settlement, map));
            return (settlement, raised);
        }

        private static void BedEveryone(Settlement settlement, CoreMap map, Faction faction)
        {
            int citizens = settlement.Citizens.Count(p => !p.Dead);
            CellRect plot = map.settlementCompound!.DwellingPlots(1, map.Size).First().Plot;
            var centre = new IntVec3(plot.minX + plot.width / 2, 0, plot.minZ + plot.height / 2);
            foreach (IntVec3 c in map.AllCells.OrderByDescending(c => (c - centre).LengthHorizontalSquared).ThenBy(c => c.x).ThenBy(c => c.z))
            {
                if (map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count >= citizens) return;
                if (c.x < 2 || c.z < 2 || c.x > map.Size.x - 3 || c.z > map.Size.z - 3) continue;
                if (!GenConstruct.CanPlaceBlueprintAt(ConstructionThingDefOf.Bed, c, map, out _)) continue;
                if (!GenAdj.OccupiedRect(c, Rot4.North, ConstructionThingDefOf.Bed.size).ExpandedBy(1).Cells.All(n => GenGrid.Standable(n, map) && map.thingGrid.ThingsListAt(n).Count == 0)) continue;
                Thing bed = ThingMaker.MakeThing(ConstructionThingDefOf.Bed, Def("WoodLog"));
                bed.SetFaction(faction);
                GenSpawn.Spawn(bed, c, map);
            }
            Assert.Fail("no room for the dormitory beds");
        }

        [Fact]
        public void A_renewal_waits_while_new_houses_fill_the_cap()
        {
            (Settlement settlement, _) = Founded("underway-cap-renewal", 4);
            CoreMap map = settlement.InteriorMap!;

            DwellingPlanner.Plan(settlement, map, dormitoryCovered: true);
            Assert.Equal(DwellingTuning.MaxUnderway, NeverFinished(map));

            Assert.Null(RenewalInitiative.TryRenewOne(settlement, map));
            AssertUnderCap(map);
        }

        [Fact]
        public void Houses_under_repair_do_not_hold_back_a_renewal_and_its_replacement_counts_toward_the_cap()
        {
            (Settlement settlement, List<Dwelling> houses) = Founded("underway-cap-repair", 4);
            CoreMap map = settlement.InteriorMap!;
            Shake(map, houses.Take(DwellingTuning.MaxUnderway));
            DwellingPlanner.Plan(settlement, map, dormitoryCovered: false); // the walls planned back, nothing new
            Assert.Equal(0, NeverFinished(map));

            Dwelling? renewed = RenewalInitiative.TryRenewOne(settlement, map);
            Assert.NotNull(renewed);
            Assert.Equal(1, NeverFinished(map));

            // New houses for the households without one fill only what the replacement left of the cap.
            DwellingPlanner.Plan(settlement, map, dormitoryCovered: true);
            Assert.Equal(DwellingTuning.MaxUnderway, NeverFinished(map));
            Assert.Null(RenewalInitiative.TryRenewOne(settlement, map));
            AssertUnderCap(map);
        }
    }
}
