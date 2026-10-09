using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using Xunit;

namespace SimWorld.Tests.Factions
{
    /// <summary>
    /// <see cref="FactionManager.FactionManagerTick"/> copied the whole faction list on every tick of the game
    /// to call <see cref="Faction.FactionTick"/> on each, which returns at once on every tick but a
    /// goodwill-check tick. It now copies only on those. RimWorld's own loop does not copy at all
    /// (<c>josh-m/rw-decompile/RimWorld/FactionManager.cs</c>); the snapshot is this port's guard against a
    /// faction leaving the list mid-tick and is kept. What has to hold is that no faction's goodwill moves
    /// differently, which these pin against the old every-tick loop.
    /// </summary>
    public class FactionTickGateTests : ContentTestBase
    {
        public FactionTickGateTests(CoreContentFixture content) : base(content)
        {
            Find.FactionManager = new FactionManager();
        }

        private static FactionDef PlayerDef => DefDatabase<FactionDef>.GetNamed("PlayerCivilization");
        private static FactionDef TribalDef => DefDatabase<FactionDef>.GetNamed("TribalCivilization");
        private static FactionDef OutlanderDef => DefDatabase<FactionDef>.GetNamed("OutlanderCivilization");
        private static FactionDef RoughDef => DefDatabase<FactionDef>.GetNamed("RoughOutlanders");

        private static Faction NewFaction(FactionDef def, string name) => new Faction(def, name, "F_" + name);

        /// <summary>The player and a handful of rivals of every temper, all neutral to the player and to each other.</summary>
        private static List<Faction> NewWorldOfFactions()
        {
            Find.FactionManager = new FactionManager();
            var all = new List<Faction>
            {
                NewFaction(PlayerDef, "Player"),
                NewFaction(TribalDef, "TribalA"),
                NewFaction(TribalDef, "TribalB"),
                NewFaction(OutlanderDef, "OutlanderA"),
                NewFaction(RoughDef, "Raiders"),
            };
            foreach (Faction a in all)
            {
                foreach (Faction b in all)
                {
                    if (!ReferenceEquals(a, b)) a.SetRelationDirect(b, FactionRelationKind.Neutral, 0);
                }
                Find.FactionManager.Add(a);
            }
            return all;
        }

        private static List<int> Goodwills(List<Faction> factions) =>
            factions.SelectMany(a => factions.Where(b => !ReferenceEquals(a, b)).Select(b => a.GoodwillWith(b))).ToList();

        [Fact]
        public void Goodwill_drifts_on_a_check_tick_and_does_not_on_the_ticks_between()
        {
            List<Faction> all = NewWorldOfFactions();
            Faction player = all[0];
            Faction tribal = all[1];

            Find.TickManager.DebugSetTicksGame(Faction.GoodwillCheckInterval * 400 + 1);
            Find.FactionManager.FactionManagerTick();
            Assert.Equal(0, tribal.GoodwillWith(player));

            // A rival below its target gains goodwill by the day; give it enough days of check ticks to move a point.
            int before = tribal.GoodwillWith(player);
            for (int k = 1; k <= 400; k++)
            {
                Find.TickManager.DebugSetTicksGame(Faction.GoodwillCheckInterval * k);
                Find.FactionManager.FactionManagerTick();
            }
            Assert.True(tribal.GoodwillWith(player) > before, "a check tick must still run the drift");
        }

        [Fact]
        public void Running_the_manager_every_tick_moves_every_goodwill_exactly_as_the_old_every_tick_loop_did()
        {
            const int Ticks = 6 * Faction.GoodwillCheckInterval * 40;

            // New: the manager's own tick, called on every tick.
            List<Faction> viaManager = NewWorldOfFactions();
            Rand.Current = new RandomStream(4141);
            for (int t = 0; t <= Ticks; t++)
            {
                Find.TickManager.DebugSetTicksGame(t);
                Find.FactionManager.FactionManagerTick();
            }
            List<int> actual = Goodwills(viaManager);

            // Old: copy the list and tick every faction, on every tick.
            List<Faction> viaEveryTick = NewWorldOfFactions();
            Rand.Current = new RandomStream(4141);
            for (int t = 0; t <= Ticks; t++)
            {
                Find.TickManager.DebugSetTicksGame(t);
                var snapshot = new List<Faction>(Find.FactionManager.AllFactionsListForReading);
                for (int i = 0; i < snapshot.Count; i++) snapshot[i].FactionTick();
                DiplomacyAI.Tick(Find.FactionManager);
            }
            List<int> expected = Goodwills(viaEveryTick);

            Assert.True(expected.Any(g => g != 0), "the old loop moved no goodwill at all, so this compares nothing");
            Assert.Equal(expected, actual);
        }
    }
}
