using System.Collections.Generic;
using System.Linq;

using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Things;
using SimWorld.Work;
using SimWorld.World;

using Xunit;

using CoreMap = SimWorld.Map.Map;

namespace SimWorld.Tests.AI
{
    /// <summary>
    /// <see cref="TamingInitiative"/> — this port's replacement for RimWorld's player-painted
    /// <c>Designation.Tame</c>, and the half whose absence kept hunting from ever happening (see that class
    /// and <see cref="WorkGiver_Hunt"/> for the measurement).
    /// <para/>
    /// Everything here is asserted as an ordering, a trend or a band. <see cref="TamingTuning"/> is explicit
    /// that its one figure has no RimWorld counterpart, and the herd size itself is derived arithmetic over
    /// content, so no literal from either is restated below.
    /// </summary>
    public class TamingInitiativeTests : ContentTestBase
    {
        public TamingInitiativeTests(CoreContentFixture content) : base(content)
        {
        }

        private static CoreMap NewMap(int sizeX, int sizeZ) => new CoreMap(sizeX, sizeZ, SimWorld.Map.TerrainDefOf.Soil);

        private static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamed(name);

        private static ThingDef Muffalo => Def("Muffalo");

        private static ThingDef Chicken => Def("Chicken");

        private static WorkGiver_TameAnimals TameGiver =>
            (WorkGiver_TameAnimals)DefDatabase<WorkGiverDef>.GetNamed("TameAnimals").Worker;

        private static WorkGiver_Hunt HuntGiver => (WorkGiver_Hunt)DefDatabase<WorkGiverDef>.GetNamed("Hunt").Worker;

        private static Settlement PlainSettlement(int tile = 0) =>
            new Settlement(WorldObjectDefOf.Settlement, tile, null, "TestSettlement" + tile, 0);

        private static Pawn SpawnAnimal(CoreMap map, ThingDef race, IntVec3 cell, string name = "Beast")
        {
            var p = new Pawn(race, name);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        private static Thing SpawnItem(CoreMap map, IntVec3 cell, string defName, int count)
        {
            Thing thing = ThingMaker.MakeThing(Def(defName));
            thing.stackCount = count;
            GenSpawn.Spawn(thing, cell, map);
            return thing;
        }

        /// <summary>Puts <paramref name="nutrition"/> worth of ordinary meals on the map, which is the only
        /// food a hunter or a handler can actually reach — see <see cref="HuntingInitiative.NutritionReachable"/>.</summary>
        private static void FeedTheMap(CoreMap map, float nutrition)
        {
            float per = Def("MealSimple").ingestible!.nutrition;
            SpawnItem(map, new IntVec3(0, 0, 0), "MealSimple", (int)(nutrition / per) + 1);
        }

        private static Pawn SpawnCitizen(CoreMap map, Settlement settlement, IntVec3 cell, string name)
        {
            Pawn p = NewHuman(name);
            settlement.AddCitizen(p);
            GenSpawn.Spawn(p, cell, map);
            return p;
        }

        // ---- the gate itself ----

        [Fact]
        public void A_settlement_short_of_food_keeps_no_livestock_and_hunts_instead()
        {
            // The defect this class exists for, stated as the two gates disagreeing before and agreeing now.
            CoreMap map = NewMap(12, 12);
            Settlement settlement = PlainSettlement();
            SpawnCitizen(map, settlement, new IntVec3(1, 0, 1), "Eater");
            SpawnAnimal(map, Chicken, new IntVec3(6, 0, 6));

            Assert.True(HuntingInitiative.WantsMeat(settlement, map), "An empty map is a settlement short of food.");
            Assert.False(TamingInitiative.WantsLivestock(settlement, map),
                "While the settlement is short of food the animals on its land are meat, not livestock.");
            Assert.Equal(0, TamingInitiative.HerdWanted(settlement, map));
        }

        [Fact]
        public void Only_a_surplus_buys_livestock_and_a_bigger_surplus_buys_more()
        {
            CoreMap map = NewMap(12, 12);
            Settlement settlement = PlainSettlement();
            SpawnCitizen(map, settlement, new IntVec3(1, 0, 1), "Eater");
            SpawnAnimal(map, Chicken, new IntVec3(6, 0, 6));

            // Exactly the larder the settlement wants and not a crumb more: nothing is spare, so nothing is
            // kept. This is the boundary the hunting gate sits on from the other side.
            FeedTheMap(map, HuntingInitiative.NutritionWanted(settlement, map));
            Assert.False(HuntingInitiative.WantsMeat(settlement, map));
            Assert.Equal(0, TamingInitiative.HerdWanted(settlement, map));

            // A surplus worth one animal's keep buys one animal.
            FeedTheMap(map, TamingInitiative.FeedCostPerHead(map));
            int small = TamingInitiative.HerdWanted(settlement, map);
            Assert.True(small > 0, "Food spare over the larder is what a settlement keeps livestock on.");
            Assert.True(TamingInitiative.WantsLivestock(settlement, map));

            // And more surplus buys more — a trend, never a count.
            FeedTheMap(map, TamingInitiative.FeedCostPerHead(map) * 5f);
            Assert.True(TamingInitiative.HerdWanted(settlement, map) > small);
        }

        [Fact]
        public void A_herd_that_already_matches_the_surplus_stops_the_taming_work()
        {
            CoreMap map = NewMap(20, 20);
            Settlement settlement = PlainSettlement();
            Pawn handler = SpawnCitizen(map, settlement, new IntVec3(1, 0, 1), "Handler");
            SpawnAnimal(map, Chicken, new IntVec3(6, 0, 6), "Wild");

            FeedTheMap(map, HuntingInitiative.NutritionWanted(settlement, map)
                + TamingInitiative.FeedCostPerHead(map) * 3f);
            int wanted = TamingInitiative.HerdWanted(settlement, map);
            Assert.True(wanted > 0);
            Assert.False(TameGiver.ShouldSkip(handler));

            // Stand the herd up. Faction, not tameness, is what makes an animal somebody's livestock — the
            // same line HuntUtility.IsHuntableAnimal and WildAnimalSpawner both draw.
            SimWorld.Factions.Faction owner = NewOwner();
            for (int i = 0; i < wanted; i++)
            {
                Pawn head = SpawnAnimal(map, Chicken, new IntVec3(10 + (i % 8), 0, 10 + (i / 8)), "Livestock" + i);
                head.faction = owner;
            }

            Assert.Equal(wanted, TamingInitiative.TameAnimalCount(map));
            Assert.False(TamingInitiative.WantsLivestock(settlement, map),
                "A settlement that already keeps what it can feed does not take on another head.");
            Assert.True(TameGiver.ShouldSkip(handler));
        }

        [Fact]
        public void A_heavier_animal_costs_more_of_the_surplus_than_a_lighter_one()
        {
            // The cost per head is the animal's own HungerRate, not a flat figure — so a map of muffalo
            // supports a smaller herd on the same surplus than a map of chickens. An ordering, not a number.
            CoreMap chickens = NewMap(10, 10);
            SpawnAnimal(chickens, Chicken, new IntVec3(4, 0, 4));
            CoreMap muffalo = NewMap(10, 10);
            SpawnAnimal(muffalo, Muffalo, new IntVec3(4, 0, 4));

            Assert.True(TamingInitiative.FeedCostPerHead(muffalo) > TamingInitiative.FeedCostPerHead(chickens));

            // And a map with nothing left to tame answers with a full eater's keep rather than dividing by
            // zero — the conservative direction, fewer heads wanted rather than infinitely many.
            CoreMap bare = NewMap(10, 10);
            Assert.True(TamingInitiative.FeedCostPerHead(bare) > 0f);
        }

        [Fact]
        public void Livestock_is_counted_by_who_owns_it_never_by_who_is_standing_there()
        {
            CoreMap map = NewMap(12, 12);
            SpawnAnimal(map, Chicken, new IntVec3(4, 0, 4), "Wild");
            Pawn owned = SpawnAnimal(map, Chicken, new IntVec3(5, 0, 5), "Owned");
            Pawn human = NewHuman("Person");
            GenSpawn.Spawn(human, new IntVec3(1, 0, 1), map);

            Assert.Equal(0, TamingInitiative.TameAnimalCount(map));

            owned.faction = NewOwner();
            Assert.Equal(1, TamingInitiative.TameAnimalCount(map));
            Assert.False(TamingInitiative.IsTameable(owned), "Somebody's livestock is nobody's taming work.");
            Assert.True(TamingInitiative.IsTameable(map.mapPawns.AllPawnsSpawned.First(p => p.Label == "Wild")));
        }

        // ---- the ordering that was the whole defect ----

        [Fact]
        public void A_generalist_hunts_when_the_settlement_is_hungry_and_tames_when_it_is_not()
        {
            // This is the measurement corrected. Handling still outranks Hunting in content — nothing here
            // touches naturalPriority — but the higher-priority giver now correctly finds no work in the
            // state where the lower one must run, so the same pawn on the same map does the opposite thing
            // purely because the settlement's larder changed.
            CoreMap map = NewMap(14, 14);
            Settlement settlement = PlainSettlement();
            Pawn generalist = SpawnCitizen(map, settlement, new IntVec3(1, 0, 1), "Generalist");
            generalist.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def("Bow_Short")));
            SpawnAnimal(map, Chicken, new IntVec3(8, 0, 8));

            Assert.True(TameGiver.ShouldSkip(generalist), "A hungry settlement's handler has no taming work...");
            Assert.False(HuntGiver.ShouldSkip(generalist), "... and its hunter does.");

            FeedTheMap(map, HuntingInitiative.NutritionWanted(settlement, map)
                + TamingInitiative.FeedCostPerHead(map) * 2f);

            Assert.False(TameGiver.ShouldSkip(generalist), "A fed settlement's handler may keep livestock...");
            Assert.True(HuntGiver.ShouldSkip(generalist), "... and its hunter leaves the wildlife alone.");
        }

        [Fact]
        public void The_two_gates_are_never_both_open()
        {
            // Walked across the whole range rather than sampled at two points: whatever the larder holds,
            // a settlement is hunting or keeping livestock, never both at once.
            CoreMap map = NewMap(14, 14);
            Settlement settlement = PlainSettlement();
            SpawnCitizen(map, settlement, new IntVec3(1, 0, 1), "Eater");
            SpawnAnimal(map, Chicken, new IntVec3(8, 0, 8));

            for (int step = 0; step < 12; step++)
            {
                bool meat = HuntingInitiative.WantsMeat(settlement, map);
                bool stock = TamingInitiative.WantsLivestock(settlement, map);
                Assert.False(meat && stock, "step " + step + ": a settlement cannot be hunting and taming at once");
                FeedTheMap(map, TamingInitiative.FeedCostPerHead(map));
            }
        }

        // ---- scribe round trip ----

        [Fact]
        public void The_gate_holds_no_state_and_answers_the_same_after_a_save_and_load()
        {
            // TamingInitiative has nothing to Scribe by construction — it re-derives every answer from the
            // map, the roster and the ledger on each job search, exactly as HuntingInitiative does. What a
            // round trip has to show is that this is true: the state it reads survives, so the answer does.
            CoreMap map = NewMap(14, 14);
            SpawnAnimal(map, Chicken, new IntVec3(8, 0, 8), "Wild");
            Pawn owned = SpawnAnimal(map, Chicken, new IntVec3(9, 0, 9), "Owned");
            SimWorld.Factions.Faction owner = NewOwner();
            owned.faction = owner;
            Pawn person = NewHuman("Person");
            GenSpawn.Spawn(person, new IntVec3(1, 0, 1), map);
            FeedTheMap(map, HuntingInitiative.NutritionWanted(null, map)
                + TamingInitiative.FeedCostPerHead(map) * 4f);

            int herdBefore = TamingInitiative.HerdWanted(null, map);
            int countBefore = TamingInitiative.TameAnimalCount(map);
            bool wantsBefore = TamingInitiative.WantsLivestock(null, map);
            Assert.True(herdBefore > 0);
            Assert.True(wantsBefore);

            // Map and owner together: Pawn.faction saves as a cross-reference, so a faction outside the saved
            // graph comes back unresolved. Same shape as AttackTargetsCacheTests' own save root.
            var root = new SaveRoot { map = map, factions = new List<SimWorld.Factions.Faction> { owner } };
            string xml = Scribe.SaveToString(root, "root");
            Pawn.ResetThingIdCounter();
            SaveRoot loadedRoot = Scribe.Load<SaveRoot>(xml, "root", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            CoreMap loaded = loadedRoot.map!;

            Assert.Equal(countBefore, TamingInitiative.TameAnimalCount(loaded));
            Assert.Equal(herdBefore, TamingInitiative.HerdWanted(null, loaded));
            Assert.Equal(wantsBefore, TamingInitiative.WantsLivestock(null, loaded));
            Assert.Equal(TamingInitiative.FeedCostPerHead(map), TamingInitiative.FeedCostPerHead(loaded), 4);
        }

        private static SimWorld.Factions.Faction NewOwner() =>
            new SimWorld.Factions.Faction(
                DefDatabase<SimWorld.Factions.FactionDef>.GetNamed("OutlanderCivilization"), "Owner", "F_Owner");

        /// <summary>A map and the factions its pawns point at, saved as one graph.</summary>
        private sealed class SaveRoot : IExposable
        {
            public CoreMap? map;

            public List<SimWorld.Factions.Faction> factions = new List<SimWorld.Factions.Faction>();

            public void ExposeData()
            {
                CoreMap? m = map;
                Scribe_Deep.Look(ref m, "map");
                map = m;
                List<SimWorld.Factions.Faction>? f = factions;
                Scribe_Collections.Look(ref f, "factions", LookMode.Deep);
                factions = f ?? new List<SimWorld.Factions.Faction>();
            }
        }
    }
}
