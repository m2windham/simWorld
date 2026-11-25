using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.God.View;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Research;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;

using Xunit;
using Xunit.Abstractions;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// Renewal past the first eras (renewal lane 2, <c>docs/design/era-progression.md</c> §7.11). The ten-year arcs
    /// renewed homes in the Agrarian and Bronze ages and then stopped for good: a big town had filled its street and
    /// renewal only ever built on a free plot, a growing town waited out a bed shortage that never ended, and nothing
    /// renewed a store or a workbench at all. Each test runs a real generated settlement with its own band, started
    /// in a later era, with the game ticking and the builders doing the work.
    /// </summary>
    [Collection("GlobalDefs")]
    public class RenewalLaterErasTests : ContentTestBase
    {
        private readonly ITestOutputHelper output;

        public RenewalLaterErasTests(CoreContentFixture content, ITestOutputHelper output) : base(content)
        {
            this.output = output;
            NameUseChecker.Clear();
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ResearchProjectDef Project(string name) => DefDatabase<ResearchProjectDef>.GetNamed(name);

        private sealed class Town
        {
            public Game Game = null!;
            public Settlement Settlement = null!;
            public CoreMap Map => Settlement.InteriorMap!;
            public Faction Faction = null!;
            public SettlementCompound Compound => Map.settlementCompound!;
            public List<Dwelling> Houses = new List<Dwelling>();
        }

        /// <summary>
        /// A tribal band on a generated map whose first <paramref name="houses"/> households live in
        /// <paramref name="rung"/> houses, and a town that has since learned every project of the eras before
        /// <paramref name="era"/> and the <paramref name="learned"/> ones, so it stands in <paramref name="era"/>.
        /// Everybody else sleeps in the dormitory, which has <paramref name="spareBeds"/> beds to spare, and the
        /// stores hold a fortnight's food.
        /// </summary>
        private Town Founded(string seed, int houses, string rung, string era, int spareBeds, params string[] learned)
        {
            CoreMap.ResetMapIdCounter();
            var t = new Town
            {
                Game = Game.NewGame(DefDatabase<ScenarioDef>.GetNamed("TribalStart").scenario, seed, subdivisionOverride: 3, soloStart: true, bandSize: 20),
            };
            t.Settlement = t.Game.World!.worldObjects.OfType<Settlement>().First();
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(t.Settlement.tile).Outcome);
            t.Faction = t.Settlement.Citizens.First(p => p.Spawned).Faction!;
            for (int i = 0; i < 2000 && t.Map.settlementCompound == null; i++) t.Game.TickManager.DoSingleTick();
            Assert.NotNull(t.Map.settlementCompound);

            // The houses were raised when their rung was the newest, the way RenewalHomesTests founds its village.
            ResearchManager research = Find.ResearchManager;
            foreach (ResearchProjectDef p in ShelterKit.Rung(rung).researchPrerequisites) research.SetProjectFinishedForSetup(p);
            List<int> households = DwellingUtility.Households(t.Settlement).Keys.ToList();
            for (int i = 0; i < houses; i++)
            {
                Dwelling? d = ShelterKit.RaiseOnPlot(t.Map, ShelterKit.Rung(rung), households[i], t.Faction);
                Assert.NotNull(d);
                t.Houses.Add(d!);
            }

            int order = DefDatabase<EraDef>.GetNamed(era).order;
            foreach (EraDef e in DefDatabase<EraDef>.AllDefsListForReading.Where(e => e.order < order))
            {
                foreach (ResearchProjectDef p in e.Projects) research.SetProjectFinishedForSetup(p);
            }
            foreach (string p in learned) research.SetProjectFinishedForSetup(Project(p));
            Assert.Equal(era, research.CurrentEra!.defName);

            BedEveryone(t, spareBeds);
            t.Settlement.AddStore(Def("MealSimple"), 600);
            DwellingUtility.AssignHomes(t.Settlement, t.Map);
            DwellingPlanner.Plan(t.Settlement, t.Map, dormitoryCovered: false); // ids, completion ticks, the founding house
            return t;
        }

        /// <summary>Dormitory beds, finished, far from the street, until every citizen has a bed and
        /// <paramref name="spare"/> more stand empty.</summary>
        private static void BedEveryone(Town t, int spare)
        {
            CoreMap map = t.Map;
            int wanted = t.Settlement.Citizens.Count(p => !p.Dead) + spare;
            CellRect plot = t.Compound.DwellingPlots(1, map.Size).First().Plot;
            var centre = new IntVec3(plot.minX + plot.width / 2, 0, plot.minZ + plot.height / 2);
            foreach (IntVec3 c in map.AllCells.OrderByDescending(c => (c - centre).LengthHorizontalSquared).ThenBy(c => c.x).ThenBy(c => c.z))
            {
                if (map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count >= wanted) return;
                if (c.x < 2 || c.z < 2 || c.x > map.Size.x - 3 || c.z > map.Size.z - 3) continue;
                if (!GenConstruct.CanPlaceBlueprintAt(ConstructionThingDefOf.Bed, c, map, out _)) continue;
                if (!GenAdj.OccupiedRect(c, Rot4.North, ConstructionThingDefOf.Bed.size).ExpandedBy(1).Cells.All(n => GenGrid.Standable(n, map) && map.thingGrid.ThingsListAt(n).Count == 0)) continue;
                if (!SettlementReach.CanReach(map, SettlementReach.BandCell(map), c)) continue;
                Thing bed = ThingMaker.MakeThing(ConstructionThingDefOf.Bed, Def("WoodLog"));
                bed.SetFaction(t.Faction);
                GenSpawn.Spawn(bed, c, map);
            }
            Assert.Fail("no room for the dormitory beds");
        }

        /// <summary>Moves the clock to the tick before the next daily pass and runs that one tick.</summary>
        private static void PassNow(Town t)
        {
            int day = RenewalTuning.PassIntervalTicks;
            int next = (Find.TickManager.TicksGame / day + 1) * day;
            Find.TickManager.DebugSetTicksGame(next - 1);
            t.Game.TickManager.DoSingleTick();
        }

        private static Dwelling? HomeOf(CoreMap map, int household) =>
            DwellingUtility.DwellingsOn(map).FirstOrDefault(d => d.householdId == household);

        /// <summary>Beds outside every house, less the citizens no finished house sleeps: below zero, somebody has
        /// nowhere to sleep.</summary>
        private static int SpareDormitoryBeds(Town t)
        {
            int dormitory = t.Map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count(b => !DwellingUtility.IsDwellingBed(b));
            return dormitory - (t.Settlement.Citizens.Count(p => !p.Dead) - DwellingUtility.HousedCitizens(t.Settlement, t.Map));
        }

        // ---- homes in a later era, on a full street ----

        [Fact]
        public void A_medieval_town_with_no_free_plot_rebuilds_a_bronze_age_house_on_its_own_plot()
        {
            Town t = Founded("renewal2-medieval", 4, "House_Chalco", "Medieval", spareBeds: 20, "GuildHalls");
            CoreMap map = t.Map;
            DwellingDef target = DwellingUtility.PreferredDwellingDef()!;
            Assert.Equal("House_Medieval", target.defName);

            // The street is full: the home area is the four houses' own plots and nothing else, as it is in a town
            // that has built along its whole street.
            List<CellRect> plots = t.Compound.DwellingPlots(int.MaxValue, map.Size)
                .Select(p => p.Plot).Where(p => t.Houses.Any(h => p.Contains(new IntVec3(h.footprint.minX, 0, h.footprint.minZ)))).ToList();
            Assert.Equal(4, plots.Count);
            foreach (IntVec3 c in map.AllCells.ToList()) map.areaManager.Home[c] = plots.Any(p => p.Contains(c));
            Assert.Null(DwellingPlanner.TryFindPlot(map, t.Compound, target));
            Assert.Null(RenewalInitiative.WaitReason(t.Settlement, map));

            PassNow(t);
            Dwelling? old = t.Houses.FirstOrDefault(d => d.replacedById != 0);
            Assert.True(old != null, "no house was renewed on a full street");
            Dwelling replacement = t.Compound.DwellingById(old!.replacedById)!;
            int household = replacement.householdId;
            Assert.Equal("House_Medieval", replacement.def.defName);
            Assert.False(old.footprint.ClipInsideRect(replacement.footprint).IsEmpty, "the new house is not on the old one's plot");
            Assert.True(old.beingDemolished);
            Assert.NotEqual(Pawn_RelationsTracker.None, household);

            int days = 0;
            for (int i = 0; i < 30 * GenDate.TicksPerDay && !(replacement.IsComplete(map) && !t.Compound.dwellings.Contains(old)); i++)
            {
                t.Game.TickManager.DoSingleTick();
                if (i % 250 != 0) continue;
                Assert.True(HomeOf(map, household) != null, "household " + household + " lost its claim on tick " + Find.TickManager.TicksGame);
                Assert.True(SpareDormitoryBeds(t) >= 0, "somebody had no bed on tick " + Find.TickManager.TicksGame);
                days = i / GenDate.TicksPerDay;
            }
            output.WriteLine("rebuilt in place by tick " + Find.TickManager.TicksGame + " (" + days + " whole days)");

            Assert.DoesNotContain(old, t.Compound.dwellings);
            Assert.True(replacement.IsComplete(map), "the new house never stood whole");
            Assert.Same(replacement, HomeOf(map, household));
            Assert.Equal(0, replacement.replacesId);
            Assert.True(replacement.Beds(map).Count >= 5);
        }

        // ---- homes while the town grows ----

        [Fact]
        public void A_crowded_household_is_rebuilt_bigger_while_newcomers_keep_the_beds_short()
        {
            Town t = Founded("renewal-crowding", 3, "House_Paleo", "Agrarian", spareBeds: 0, "Shelter");
            CoreMap map = t.Map;
            Assert.Equal("House_Neo", DwellingUtility.PreferredDwellingDef()!.defName);
            // The newer house is the crowded one: the older is the rung's oldest unkept house, which the old quarter
            // keeps (7.6) once renewal reaches the rung's last two.
            Dwelling roomy = t.Houses[1], crowded = t.Houses[2];

            // One household outgrows its two beds; the other is down to one member.
            int[] housed = t.Houses.Select(d => d.householdId).ToArray();
            List<Pawn> others = t.Settlement.Citizens.Where(p => !p.Dead && !housed.Contains(p.relations.familyId)).ToList();
            foreach (Pawn p in others.Take(3)) p.relations.familyId = crowded.householdId;
            foreach (Pawn p in t.Settlement.Citizens.Where(p => p.relations.familyId == roomy.householdId).Skip(1).ToList())
            {
                p.relations.familyId = others.Skip(3).First().relations.familyId;
            }

            // The town grows: two babies are born with no bed for them yet.
            for (int b = 0; b < 2; b++)
            {
                Pawn baby = PawnGenerator.GenerateNewborn(PawnKindDefOf.Tribesperson, faction: t.Settlement.faction);
                baby.relations.familyId = crowded.householdId;
                t.Settlement.AddCitizen(baby);
                Pawn mother = t.Settlement.Citizens.First(p => p.Spawned && !p.Downed);
                IntVec3 cell = map.AllCells.OrderBy(c => (c - mother.Position).LengthHorizontalSquared)
                    .First(c => GenGrid.Standable(c, map) && map.thingGrid.ThingsListAt(c).Count == 0);
                GenSpawn.Spawn(baby, cell, map);
            }
            Assert.Equal(RenewalInitiative.BedsShort, RenewalInitiative.WaitReason(t.Settlement, map));
            Assert.True(DwellingUtility.Households(t.Settlement)[crowded.householdId] > crowded.def.BedCount);

            int bedsBefore = map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count;
            Assert.Same(crowded, RenewalInitiative.TryRenewOne(t.Settlement, map));
            Assert.True(crowded.replacedById != 0, "the crowded household's house was not renewed while beds were short");
            Assert.Equal(0, roomy.replacedById);

            // The new house goes up beside the old, so beds are only ever added on the way.
            Dwelling replacement = t.Compound.DwellingById(crowded.replacedById)!;
            for (int i = 0; i < 20 * GenDate.TicksPerDay && !replacement.IsComplete(map); i++)
            {
                t.Game.TickManager.DoSingleTick();
                if (i % 250 == 0) Assert.True(map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count >= bedsBefore, "a bed was lost on tick " + Find.TickManager.TicksGame);
            }
            Assert.True(replacement.IsComplete(map), "the bigger house never stood whole");
            Assert.True(map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count > bedsBefore);
        }

        // ---- stores and workbenches ----

        [Fact]
        public void A_medieval_town_renews_its_storage_hut_and_rebuilds_a_wooden_smithy_from_what_its_ledger_holds()
        {
            Town t = Founded("renewal2-structures", 2, "House_Chalco", "Medieval", spareBeds: 0, "GuildHalls", "Feudalism");
            CoreMap map = t.Map;
            Assert.Equal("TitheBarn", StorageForms.Newest().defName);

            // A storage hut and a wooden smithy with a standing bill, outside the houses.
            IntVec3 band = SettlementReach.BandCell(map)!.Value;
            ThingDef barn = StorageForms.Newest();
            List<IntVec3> open = map.AllCells.Where(c => c.x > 3 && c.z > 3 && c.x < map.Size.x - 4 && c.z < map.Size.z - 4)
                .Where(c => GenConstruct.CanPlaceBlueprintAt(barn, c, map, out _) && GenConstruct.CanPlaceBlueprintAt(Def("Smithy"), c, map, out _))
                .Where(c => DwellingUtility.DwellingAt(map, c) == null && SettlementReach.CanReach(map, band, c))
                .Where(c => GenAdj.OccupiedRect(c, Rot4.North, new IntVec2(1, 1)).ExpandedBy(2).Cells.All(n => GenGrid.Standable(n, map) && map.thingGrid.ThingsListAt(n).Count == 0 && DwellingUtility.DwellingAt(map, n) == null))
                .OrderBy(c => (c - band).LengthHorizontalSquared).ThenBy(c => c.x).ThenBy(c => c.z).ToList();
            IntVec3 hutCell = open[0];
            IntVec3 smithyCell = open.First(c => (c - hutCell).LengthHorizontalSquared > 25);
            Thing hut = ThingMaker.MakeThing(ConstructionThingDefOf.StorageHut);
            hut.SetFaction(t.Faction);
            GenSpawn.Spawn(hut, hutCell, map);
            ThingDef smithyDef = Def("Smithy");
            var smithy = (ThingWithComps)ThingMaker.MakeThing(smithyDef, Def("WoodLog"));
            smithy.SetFaction(t.Faction);
            GenSpawn.Spawn(smithy, smithyCell, map);

            // What the town holds is in its ledger: cut granite and steel, more than the map has.
            t.Settlement.AddStore(Def("BlocksGranite"), 400);
            t.Settlement.AddStore(Def("Steel"), 400);

            PassNow(t);
            Assert.True(map.thingGrid.ThingsListAt(hutCell).FirstOrDefault(x => x.def.category == ThingCategory.Blueprint) is Thing site && GenConstruct.ThingToReplace(site) == hut, "no store planned over the hut");

            for (int i = 0; i < 20 * GenDate.TicksPerDay; i++)
            {
                t.Game.TickManager.DoSingleTick();
                if (i % GenDate.TicksPerDay != 0) continue;
                if (!StorageForms.IsStorage(map.edificeGrid[hutCell]?.def) || ReferenceEquals(map.edificeGrid[hutCell]!.def, ConstructionThingDefOf.StorageHut)) continue;
                if (map.edificeGrid[smithyCell] is Thing s && ReferenceEquals(s.def, smithyDef) && !ReferenceEquals(s.Stuff, Def("WoodLog"))) break;
            }

            Thing? store = map.edificeGrid[hutCell];
            Thing? bench = map.edificeGrid[smithyCell];
            output.WriteLine("store " + store?.def.defName + " in " + store?.Stuff?.defName + "; smithy in " + bench?.Stuff?.defName + " on day " + Find.TickManager.TicksGame / GenDate.TicksPerDay);
            Assert.True(store != null && StorageForms.IsStorage(store.def), "the store is gone, not renewed");
            int now = Find.ResearchManager.CurrentEra!.order;
            Assert.True(StructureRenewal.FormEraOrder(store!) >= now - 1, "the store is still in an old form: " + store!.def.defName);
            Assert.True(bench != null && ReferenceEquals(bench.def, smithyDef), "the smithy is gone, not rebuilt");
            Assert.True(StructureRenewal.FormEraOrder(bench!) > 0, "the smithy was not rebuilt in a better material: " + bench!.Stuff?.defName);

            RenewalSummary summary = GodViewSnapshot.Capture().Era.Renewal;
            Assert.True(summary.Categories.Single(c => c.Category == "Storage").InCurrentOrPreviousEra >= 1);
        }

        [Fact]
        public void A_bench_rebuilt_in_place_keeps_its_bills()
        {
            Town t = Founded("renewal2-bills", 1, "House_Chalco", "Medieval", spareBeds: 0, "GuildHalls");
            CoreMap map = t.Map;
            IntVec3 band = SettlementReach.BandCell(map)!.Value;
            ThingDef smithyDef = Def("Smithy");
            IntVec3 cell = map.AllCells.Where(c => DwellingUtility.DwellingAt(map, c) == null && GenConstruct.CanPlaceBlueprintAt(smithyDef, c, map, out _))
                .OrderBy(c => (c - band).LengthHorizontalSquared).ThenBy(c => c.x).ThenBy(c => c.z).First();
            var smithy = (ThingWithComps)ThingMaker.MakeThing(smithyDef, Def("WoodLog"));
            smithy.SetFaction(t.Faction);
            GenSpawn.Spawn(smithy, cell, map);
            List<Bill> bills = smithyDef.AllRecipes.Take(2).Select(r => (Bill)new Bill_Production(r) { targetCount = 3 }).ToList();
            foreach (Bill b in bills) smithy.GetComp<CompBillGiver>()!.BillStack.AddBill(b);

            Blueprint? site = GenConstruct.PlaceReplacement(smithyDef, Def("BlocksGranite"), cell, map);
            Assert.NotNull(site);
            Pawn builder = t.Settlement.Citizens.First(p => p.Spawned && !p.Dead);
            site!.ReplaceWithFrame().CompleteConstruction(builder);

            var rebuilt = (ThingWithComps)map.edificeGrid[cell]!;
            Assert.NotSame(smithy, rebuilt);
            Assert.True(smithy.Destroyed);
            Assert.Same(Def("BlocksGranite"), rebuilt.Stuff);
            BillStack stack = rebuilt.GetComp<CompBillGiver>()!.BillStack;
            Assert.Equal(bills, stack.Bills);
            Assert.All(stack.Bills, b => Assert.Same(stack, b.billStack));
        }
    }
}
