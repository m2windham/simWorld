using System.Collections.Generic;
using System.Linq;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.World;
using Xunit;
using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.Building
{
    /// <summary>
    /// How many storage huts a settlement wants. The target used to be one hut per 50 units in
    /// <see cref="Settlement.Stores"/>, uncapped; a hut holds nothing, so seed 777 of the mapdump bench built 97
    /// by day 6 and 453 by day 20 for 26 people, about 11,000 wood on markers, and the count rose with every
    /// harvest. It is now sized by people (<see cref="StorageHutTarget"/>).
    /// <para/>
    /// Everything here is a relation, never the tuned literal: more people want more huts, more goods alone want
    /// none, a founding band wants a handful, and the watched and unwatched paths want the same number. The
    /// number the relations hold for (<see cref="ConstructionInitiativeTuning.CitizensPerStorageHut"/>) can be
    /// retuned without touching a line here.
    /// </summary>
    public class StorageHutTargetTests : ContentTestBase
    {
        public StorageHutTargetTests(CoreContentFixture content) : base(content)
        {
        }

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static CoreMap NewMap(int size) => new CoreMap(size, size, SimWorld.Map.TerrainDefOf.Soil);

        private Settlement Town(int citizens, int stored, int tile = 0)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, null, "Town" + tile, 0);
            for (int i = 0; i < citizens; i++) settlement.AddCitizen(NewHuman("Citizen" + i));
            if (stored > 0) settlement.AddStore(Def("WoodLog"), stored);
            return settlement;
        }

        private static int HutBlueprints(CoreMap map) =>
            map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)
                .OfType<Blueprint>()
                .Count(bp => bp.EntityToBuild == ConstructionThingDefOf.StorageHut);

        // ---- the relations ----

        [Fact]
        public void Nothing_in_store_wants_no_huts_however_many_people()
        {
            foreach (int citizens in new[] { 0, 1, 26, 500 }) Assert.Equal(0, StorageHutTarget.For(citizens, totalStored: 0));
        }

        [Fact]
        public void Anything_in_store_wants_at_least_one_hut_even_with_nobody_on_the_roster()
        {
            Assert.True(StorageHutTarget.For(citizens: 0, totalStored: 1) >= 1);
            Assert.True(StorageHutTarget.For(citizens: 1, totalStored: 1) >= 1);
        }

        [Fact]
        public void More_people_want_more_huts()
        {
            int previous = StorageHutTarget.For(1, totalStored: 10);
            for (int citizens = 2; citizens <= 400; citizens++)
            {
                int target = StorageHutTarget.For(citizens, totalStored: 10);
                Assert.True(target >= previous, "Adding a person lowered the target at " + citizens + " citizens.");
                previous = target;
            }

            // And it is not flat: each CitizensPerStorageHut more people is at least one more hut.
            int step = ConstructionInitiativeTuning.CitizensPerStorageHut;
            for (int citizens = 1; citizens <= 200; citizens += 7)
            {
                Assert.True(StorageHutTarget.For(citizens + step, 10) > StorageHutTarget.For(citizens, 10),
                    step + " more people on top of " + citizens + " should want another hut.");
            }
        }

        [Fact]
        public void More_goods_alone_want_no_more_huts()
        {
            foreach (int citizens in new[] { 1, 5, 26, 40, 200 })
            {
                int atFirst = StorageHutTarget.For(citizens, totalStored: 1);
                Assert.True(atFirst >= 1);
                foreach (int stored in new[] { 10, 1_000, 1_000_000 })
                {
                    Assert.Equal(atFirst, StorageHutTarget.For(citizens, stored));
                }
            }
        }

        [Fact]
        public void A_founding_band_wants_a_handful_of_huts_whatever_it_has_gathered()
        {
            // The old rule asked a band of 26 for 453 huts by day 20. "A handful" is the claim, so the bound is
            // generous on purpose: it is far below the failure and above anything the tuning could reasonably be.
            const int handful = 10;
            for (int band = SettlementTuning.FoundingBandRange.min; band <= SettlementTuning.FoundingBandRange.max; band++)
            {
                int target = StorageHutTarget.For(band, totalStored: 5_000_000);
                Assert.InRange(target, 1, handful);
            }
            Assert.InRange(StorageHutTarget.For(26, totalStored: 5_000_000), 1, handful);
        }

        [Fact]
        public void Only_real_citizens_count_never_the_statistical_cohort()
        {
            Settlement band = Town(citizens: 3, stored: 10);
            int before = StorageHutTarget.For(band);
            band.AddStatisticalPeople(5000);

            Assert.Equal(before, StorageHutTarget.For(band));
            Assert.Equal(StorageHutTarget.For(3, 10), StorageHutTarget.For(band));
        }

        // ---- the watched path ----

        [Fact]
        public void A_watched_settlement_plans_exactly_the_huts_its_people_want_and_a_bigger_harvest_plans_no_more()
        {
            Settlement settlement = Town(citizens: 26, stored: 10);
            CoreMap map = NewMap(100);
            int wanted = StorageHutTarget.For(settlement);
            Assert.True(wanted >= 2, "test setup: a band of 26 should want more than one hut");

            void Passes(int from, int count)
            {
                for (int pass = from; pass < from + count; pass++)
                {
                    Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
                    SettlementConstructionInitiative.TickSettlement(settlement, map);
                }
            }

            Passes(0, 60); // beds and walls first, three a pass, then the huts
            Assert.Equal(wanted, HutBlueprints(map));

            // The same people hoard a hundred thousand more units. Nothing about that is a reason for another shed.
            settlement.AddStore(Def("WoodLog"), 100_000);
            Passes(60, 60);
            Assert.Equal(wanted, HutBlueprints(map));
        }

        [Fact]
        public void A_surplus_of_huts_already_standing_is_left_alone_and_none_are_added()
        {
            // What a save from before this change holds: far more huts than the people want.
            Settlement settlement = Town(citizens: 10, stored: 5000);
            CoreMap map = NewMap(80);
            int wanted = StorageHutTarget.For(settlement);
            int standing = wanted * 6 + 5;
            for (int i = 0; i < standing; i++)
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(ConstructionThingDefOf.StorageHut), new IntVec3(4 + (i % 12) * 3, 0, 4 + (i / 12) * 3), map);
            }

            for (int pass = 0; pass < 60; pass++)
            {
                Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
                SettlementConstructionInitiative.TickSettlement(settlement, map);
            }

            Assert.Equal(0, HutBlueprints(map)); // nothing more asked for
            Assert.Equal(standing, map.listerThings.ThingsOfDef(ConstructionThingDefOf.StorageHut).Count); // nothing demolished
        }

        // ---- the unwatched path agrees ----

        [Fact]
        public void A_settlement_nobody_has_opened_wants_the_same_huts_as_one_that_is_being_watched()
        {
            foreach (int citizens in new[] { 1, 4, 5, 6, 12, 26 })
            {
                // Watched: the real initiative, on a real (open) map.
                Settlement watched = Town(citizens, stored: 400, tile: 1);
                CoreMap map = NewMap(100);
                for (int pass = 0; pass < 80; pass++)
                {
                    Find.TickManager.DebugSetTicksGame(pass * ConstructionInitiativeTuning.IntervalTicks);
                    SettlementConstructionInitiative.TickSettlement(watched, map);
                }

                // Unwatched: no map at all, the structures ledger.
                Settlement unwatched = Town(citizens, stored: 400, tile: 2);
                for (int pass = 0; pass < 80; pass++) AbstractSettlementConstruction.Run(unwatched);

                int wanted = StorageHutTarget.For(unwatched);
                Assert.Equal(wanted, HutBlueprints(map));
                Assert.Equal(wanted, unwatched.StructureCount(ConstructionThingDefOf.StorageHut));
            }
        }

        [Fact]
        public void An_unwatched_settlement_stops_at_the_target_however_much_it_gathers()
        {
            Settlement unwatched = Town(citizens: 26, stored: 10);
            for (int pass = 0; pass < 80; pass++) AbstractSettlementConstruction.Run(unwatched);
            int wanted = StorageHutTarget.For(unwatched);
            Assert.Equal(wanted, unwatched.StructureCount(ConstructionThingDefOf.StorageHut));

            unwatched.AddStore(Def("WoodLog"), 1_000_000);
            for (int pass = 0; pass < 80; pass++) AbstractSettlementConstruction.Run(unwatched);
            Assert.Equal(wanted, unwatched.StructureCount(ConstructionThingDefOf.StorageHut));
        }

        [Fact]
        public void More_people_want_more_huts_on_the_unwatched_path_too()
        {
            int Built(int citizens)
            {
                Settlement s = Town(citizens, stored: 10, tile: citizens);
                for (int pass = 0; pass < 200; pass++) AbstractSettlementConstruction.Run(s);
                return s.StructureCount(ConstructionThingDefOf.StorageHut);
            }

            Assert.True(Built(40) > Built(5));
        }
    }
}
