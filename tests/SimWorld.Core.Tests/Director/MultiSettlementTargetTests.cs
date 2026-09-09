using System.Collections.Generic;
using System.Linq;

using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Economy;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;
using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// <see cref="CivilizationTarget"/> once it has real settlements behind it: the storyteller stops being
    /// told what the civilization is and reads it instead — who lives in it, where its seat is, what it owns,
    /// and which of its towns an incident lands on.
    /// </summary>
    [Collection("GlobalDefs")]
    public class MultiSettlementTargetTests : ContentTestBase
    {
        public MultiSettlementTargetTests(CoreContentFixture content) : base(content)
        {
        }

        private static Settlement Town(string name, int tile, int foundingTick, int statisticalPopulation = 0)
        {
            var settlement = new Settlement(WorldObjectDefOf.Settlement, tile, null, name, foundingTick);
            if (statisticalPopulation > 0) settlement.AddStatisticalPeople(statisticalPopulation);
            return settlement;
        }

        [Fact]
        public void With_no_settlements_the_target_still_answers_from_what_it_was_handed()
        {
            var target = new CivilizationTarget { PlayerWealthForStoryteller = 4200f, Tile = 17 };
            target.pawns.Add(NewHuman());

            Assert.Equal(4200f, target.PlayerWealthForStoryteller);
            Assert.Equal(17, target.Tile);
            Assert.Single(target.PlayerPawnsForStoryteller);
            Assert.Null(target.ChooseTargetSettlement(new RandomStream(1)));
        }

        [Fact]
        public void The_roster_is_every_citizen_of_every_settlement()
        {
            Settlement a = Town("A", 1, 0);
            Settlement b = Town("B", 2, 100);
            a.AddCitizen(NewHuman());
            a.AddCitizen(NewHuman());
            b.AddCitizen(NewHuman());

            var target = new CivilizationTarget { Tile = 99 };
            target.pawns.Add(NewHuman());
            target.SetSettlements(new[] { a, b });

            // The hand-set list is a fallback, not an addition: attaching settlements replaces it outright.
            Assert.Equal(3, target.PlayerPawnsForStoryteller.Count());
        }

        [Fact]
        public void The_seat_is_the_oldest_settlement_whatever_order_they_arrive_in()
        {
            Settlement founded = Town("Founded", 42, 0);
            Settlement later = Town("Later", 7, 5000);

            var target = new CivilizationTarget();
            target.SetSettlements(new[] { later, founded });
            Assert.Equal(42, target.Tile);

            target.SetSettlements(new[] { founded, later });
            Assert.Equal(42, target.Tile);
        }

        [Fact]
        public void Two_settlements_founded_on_the_same_tick_still_pick_the_same_seat_every_time()
        {
            Settlement low = Town("Low", 3, 0);
            Settlement high = Town("High", 8, 0);

            var one = new CivilizationTarget();
            one.SetSettlements(new[] { high, low });
            var other = new CivilizationTarget();
            other.SetSettlements(new[] { low, high });

            Assert.Equal(one.Tile, other.Tile);
        }

        [Fact]
        public void Wealth_is_what_the_settlements_actually_hold_and_grows_when_they_hold_more()
        {
            Settlement a = Town("A", 1, 0);
            Settlement b = Town("B", 2, 0);
            var target = new CivilizationTarget { PlayerWealthForStoryteller = 500f };
            target.SetSettlements(new[] { a, b });

            // Attached settlements own the answer; the number set by hand is no longer consulted.
            Assert.Equal(0f, target.PlayerWealthForStoryteller);

            ThingDef stored = DefDatabase<ThingDef>.AllDefsListForReading
                .First(d => TradeUtility.BaseMarketValue(d) > 0f);
            float unit = TradeUtility.BaseMarketValue(stored);

            a.AddStore(stored, 10);
            Assert.Equal(unit * 10f, target.PlayerWealthForStoryteller, 3);

            b.AddStore(stored, 5);
            Assert.Equal(unit * 15f, target.PlayerWealthForStoryteller, 3);
        }

        [Fact]
        public void An_incident_lands_on_a_settlement_more_often_where_more_people_live()
        {
            Settlement capital = Town("Capital", 1, 0, statisticalPopulation: 900);
            Settlement hamlet = Town("Hamlet", 2, 0, statisticalPopulation: 100);

            var target = new CivilizationTarget();
            target.SetSettlements(new[] { capital, hamlet });

            var rand = new RandomStream(1234);
            int capitalHits = 0;
            const int Rolls = 2000;
            for (int i = 0; i < Rolls; i++)
            {
                if (ReferenceEquals(target.ChooseTargetSettlement(rand), capital)) capitalHits++;
            }

            // Weighted by population, so roughly 9 in 10 — a band rather than a number, since the point is
            // the weighting and not this seed's exact count.
            Assert.InRange(capitalHits / (double)Rolls, 0.85, 0.95);
        }

        [Fact]
        public void A_settlement_that_has_lost_everyone_can_still_be_chosen()
        {
            Settlement occupied = Town("Occupied", 1, 0, statisticalPopulation: 50);
            Settlement empty = Town("Empty", 2, 0);

            var target = new CivilizationTarget();
            target.SetSettlements(new[] { occupied, empty });

            var rand = new RandomStream(77);
            bool everPickedEmpty = false;
            for (int i = 0; i < 5000 && !everPickedEmpty; i++)
            {
                everPickedEmpty = ReferenceEquals(target.ChooseTargetSettlement(rand), empty);
            }

            Assert.True(everPickedEmpty, "an emptied settlement became invisible to the narrator");
        }

        [Fact]
        public void The_same_seed_picks_the_same_places_in_the_same_order()
        {
            var settlements = new List<Settlement>
            {
                Town("A", 1, 0, 300),
                Town("B", 2, 0, 200),
                Town("C", 3, 0, 100),
            };
            var target = new CivilizationTarget();
            target.SetSettlements(settlements);

            var first = new List<string>();
            var firstRand = new RandomStream(99);
            for (int i = 0; i < 50; i++) first.Add(target.ChooseTargetSettlement(firstRand)!.name);

            var second = new List<string>();
            var secondRand = new RandomStream(99);
            for (int i = 0; i < 50; i++) second.Add(target.ChooseTargetSettlement(secondRand)!.name);

            Assert.Equal(first, second);
        }

        [Fact]
        public void A_lone_settlement_is_always_the_one_chosen()
        {
            Settlement only = Town("Only", 5, 0, 40);
            var target = new CivilizationTarget();
            target.SetSettlements(new[] { only });

            var rand = new RandomStream(5);
            for (int i = 0; i < 20; i++) Assert.Same(only, target.ChooseTargetSettlement(rand));
        }

        [Fact]
        public void A_settlement_nobody_has_entered_contributes_no_map_and_the_hook_still_answers()
        {
            Settlement unentered = Town("Unentered", 1, 0, 30);
            var target = new CivilizationTarget();
            target.SetSettlements(new[] { unentered });

            Assert.Null(unentered.InteriorMap);
            Assert.Null(target.MapFor(unentered));
            Assert.Null(target.MapFor(null));
        }
    }
}
