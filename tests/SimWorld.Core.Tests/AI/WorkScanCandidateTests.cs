using System.Collections.Generic;
using System.Linq;
using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Filth;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using Xunit;
using CoreFilth = SimWorld.Filth.Filth;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// Five work scans narrow what they offer to the candidates their own <c>HasJobOnThing</c> could accept (tick-cost
    /// lane; RimWorld walks a lister of exactly such things — <c>ListerHaulables</c>, <c>ListerBuildingsRepairable</c>,
    /// <c>listerFilthInHomeArea</c>, the growing zones — rather than every thing of a kind). The scan takes the
    /// nearest accepted candidate, the earliest-listed on a tie, so the narrowing leaves the job a pawn gets alone
    /// only while two things hold, and these tests hold both: everything <c>HasJobOnThing</c> accepts is still
    /// offered, and what is offered keeps the lister's order. Each test also pins what the narrowing drops, so a
    /// filter that quietly let everything through again would fail here too.
    /// </summary>
    public class WorkScanCandidateTests : ContentTestBase
    {
        public WorkScanCandidateTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static T Giver<T>(string defName) where T : WorkGiver_Scanner =>
            (T)DefDatabase<WorkGiverDef>.GetNamed(defName).Worker;

        private static Pawn SpawnPawn(CoreMap map)
        {
            Pawn p = NewHuman("Worker");
            GenSpawn.Spawn(p, new IntVec3(1, 0, 1), map);
            return p;
        }

        private static Plant SpawnPlant(CoreMap map, string defName, IntVec3 cell, float growth, Zone_Growing? field)
        {
            if (field != null) map.zoneManager.AddCell(field, cell);
            var plant = (Plant)GenSpawn.Spawn(ThingMaker.MakeThing(Def(defName)), cell, map);
            plant.Growth = growth;
            return plant;
        }

        /// <summary>Everything the giver accepts in <paramref name="pool"/> is offered, and the offer is a subsequence
        /// of <paramref name="pool"/> in its own order. Returns the offer.</summary>
        private static List<Thing> AssertSafeOffer(WorkGiver_Scanner giver, Pawn pawn, IReadOnlyList<Thing> pool)
        {
            List<Thing> offered = giver.PotentialWorkThingsGlobal(pawn).ToList();
            int j = 0;
            foreach (Thing t in offered)
            {
                while (j < pool.Count && !ReferenceEquals(pool[j], t)) j++;
                Assert.True(j < pool.Count, $"{t} is offered out of the lister's order, or is not in it");
                j++;
            }
            foreach (Thing t in pool)
            {
                if (giver.HasJobOnThing(pawn, t)) Assert.Contains(t, offered);
            }
            return offered;
        }

        [Fact]
        public void Harvest_and_forage_offer_only_what_could_be_picked_today()
        {
            // No food on the map: the settlement is short, so an unripe food crop past its minimum growth is work.
            var map = new CoreMap(14, 14, SimWorld.Map.TerrainDefOf.Soil);
            Pawn grower = SpawnPawn(map);
            var field = new Zone_Growing { label = "field", plantDefToGrow = Def("Plant_Rice") };
            map.zoneManager.RegisterZone(field);
            Plant ripe = SpawnPlant(map, "Plant_Rice", new IntVec3(5, 0, 5), 1f, field);
            Plant unripe = SpawnPlant(map, "Plant_Rice", new IntVec3(6, 0, 5), 0.8f, field);
            Plant seedling = SpawnPlant(map, "Plant_Rice", new IntVec3(7, 0, 5), 0.3f, field);
            Plant fieldTree = SpawnPlant(map, "Plant_TreePoplar", new IntVec3(8, 0, 5), 0.5f, field);
            Plant wildRipe = SpawnPlant(map, "Plant_Berry", new IntVec3(10, 0, 10), 1f, null);
            Plant wildUnripe = SpawnPlant(map, "Plant_Berry", new IntVec3(11, 0, 10), 0.8f, null);
            Plant wildSeedling = SpawnPlant(map, "Plant_Berry", new IntVec3(12, 0, 10), 0.3f, null);
            Plant wildTree = SpawnPlant(map, "Plant_TreePoplar", new IntVec3(12, 0, 12), 1f, null);
            IReadOnlyList<Thing> plants = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);

            Assert.True(HuntingInitiative.ShortOfFood(map));
            List<Thing> harvest = AssertSafeOffer(Giver<WorkGiver_GrowerHarvest>("GrowerHarvest"), grower, plants);
            Assert.Equal(new Thing[] { ripe, unripe, fieldTree }, harvest);
            Assert.True(Giver<WorkGiver_GrowerHarvest>("GrowerHarvest").HasJobOnThing(grower, unripe));

            List<Thing> forage = AssertSafeOffer(Giver<WorkGiver_ForageWild>("ForageWild"), grower, plants);
            Assert.Equal(new Thing[] { wildRipe, wildUnripe }, forage);
            Assert.True(Giver<WorkGiver_ForageWild>("ForageWild").HasJobOnThing(grower, wildUnripe));
            Assert.DoesNotContain(seedling, harvest.Concat(forage));
            Assert.DoesNotContain(wildSeedling, harvest.Concat(forage));
            Assert.DoesNotContain(wildTree, harvest.Concat(forage));
        }

        [Fact]
        public void Repair_offers_only_damaged_buildings()
        {
            var map = new CoreMap(10, 10, SimWorld.Map.TerrainDefOf.Soil);
            Pawn builder = SpawnPawn(map);
            foreach (IntVec3 c in map.AllCells) map.areaManager.Home[c] = true;
            Thing whole = GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall"), Def("WoodLog")), new IntVec3(4, 0, 4), map);
            Thing damaged = GenSpawn.Spawn(ThingMaker.MakeThing(Def("Wall"), Def("WoodLog")), new IntVec3(5, 0, 4), map);
            damaged.HitPoints = damaged.MaxHitPoints - 10;
            // RimWorld repairs only its own faction's buildings; these are the builder's, whatever that is.
            if (builder.Faction != null)
            {
                whole.SetFaction(builder.Faction);
                damaged.SetFaction(builder.Faction);
            }
            var giver = Giver<WorkGiver_Repair>("Repair");

            List<Thing> offered = AssertSafeOffer(giver, builder, map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial));
            Assert.Equal(new[] { damaged }, offered);
            Assert.True(giver.HasJobOnThing(builder, damaged));
            Assert.DoesNotContain(whole, offered);
        }

        [Fact]
        public void Haul_offers_only_what_is_not_already_stored()
        {
            var map = new CoreMap(10, 10, SimWorld.Map.TerrainDefOf.Soil);
            Pawn hauler = SpawnPawn(map);
            var stockpile = new Zone_Stockpile();
            map.zoneManager.RegisterZone(stockpile);
            map.zoneManager.AddCell(stockpile, new IntVec3(7, 0, 7));
            map.zoneManager.AddCell(stockpile, new IntVec3(8, 0, 7));
            stockpile.filter.SetAllow(Def("WoodLog"), true);
            Thing stored = GenSpawn.Spawn(ThingMaker.MakeThing(Def("WoodLog")), new IntVec3(7, 0, 7), map);
            Thing loose = GenSpawn.Spawn(ThingMaker.MakeThing(Def("WoodLog")), new IntVec3(3, 0, 3), map);
            var giver = Giver<WorkGiver_Haul>("HaulGeneral");

            List<Thing> offered = AssertSafeOffer(giver, hauler, map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver));
            Assert.Equal(new[] { loose }, offered);
            Assert.True(giver.HasJobOnThing(hauler, loose));
            Assert.DoesNotContain(stored, offered);
        }

        [Fact]
        public void Cleaning_offers_only_settled_filth_where_the_settlement_cleans()
        {
            var map = new CoreMap(12, 12, SimWorld.Map.TerrainDefOf.Soil);
            Pawn cleaner = SpawnPawn(map);
            foreach (IntVec3 c in new CellRect(0, 0, 6, 12).Cells) map.areaManager.Home[c] = true;
            FilthMaker.TryMakeFilth(new IntVec3(3, 0, 3), map, FilthDefOf.Filth_Dirt, "test");
            FilthMaker.TryMakeFilth(new IntVec3(9, 0, 3), map, FilthDefOf.Filth_Dirt, "test");
            for (int i = 0; i < WorkGiver_CleanFilth.MinTicksSinceThickened + 1; i++) Find.TickManager.DoSingleTick();
            FilthMaker.TryMakeFilth(new IntVec3(4, 0, 8), map, FilthDefOf.Filth_Dirt, "test");
            IReadOnlyList<Thing> filth = map.listerThings.ThingsInGroup(ThingRequestGroup.Filth);
            Thing settled = filth.Single(f => f.Position == new IntVec3(3, 0, 3));
            var giver = Giver<WorkGiver_CleanFilth>("CleanFilth");

            List<Thing> offered = AssertSafeOffer(giver, cleaner, filth);
            Assert.Equal(new[] { settled }, offered);
            Assert.True(giver.HasJobOnThing(cleaner, settled));
            // The fresh pile in the home area and the settled one outside it are there and are not offered.
            Assert.Contains(filth, f => f.Position == new IntVec3(4, 0, 8) && ((CoreFilth)f).TicksSinceThickened < WorkGiver_CleanFilth.MinTicksSinceThickened);
            Assert.Contains(filth, f => f.Position == new IntVec3(9, 0, 3));
        }
    }
}
