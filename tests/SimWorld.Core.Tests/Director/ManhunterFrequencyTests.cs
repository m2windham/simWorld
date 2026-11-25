using System.Linq;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.God.View;
using SimWorld.Pawns;
using SimWorld.Scenario;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.World;

using Xunit;

namespace SimWorld.Tests.Director
{
    /// <summary>
    /// How often maddened beasts come, on a real generated tribal start. The third playtest's letter pile was
    /// mostly packs: a pack weighed half a raid and could come again the next big-threat roll, and a lone tribe
    /// with no hostile neighbour yet had nothing else in the category, so every big threat was a pack. RimWorld
    /// (rimworldwiki.com "Events", event data table) weighs a raid 9.0 to a pack's 2.0 and waits 15 days before
    /// a pack may repeat.
    /// </summary>
    [Collection("GlobalDefs")]
    public class ManhunterFrequencyTests : ContentTestBase
    {
        public ManhunterFrequencyTests(CoreContentFixture content) : base(content)
        {
            NameUseChecker.Clear();
        }

        private static IncidentDef Def(string name) => DefDatabase<IncidentDef>.GetNamed(name);

        [Fact]
        public void A_pack_does_not_come_again_within_fifteen_days_of_the_last()
        {
            Game game = Game.NewGame(ScenarioDefOf.TribalStart.scenario, "beasts-refire", subdivisionOverride: 3, soloStart: true, bandSize: 20);
            Settlement settlement = game.CivilizationTarget.Seat!;
            Assert.Equal(GodCommandOutcome.Done, GodCommands.OpenSettlement(settlement.tile).Outcome);
            Find.TickManager.DebugSetTicksGame(8 * GenDate.TicksPerDay);

            IncidentDef pack = Def("ManhunterPack");
            IncidentParms Parms() => new IncidentParms { target = game.CivilizationTarget, points = 200f };
            Assert.True(pack.Worker.CanFireNow(Parms()), "a pack can come to a fresh tribe on a real map");
            Assert.True(pack.Worker.TryExecute(Parms()));
            int fired = Find.TickManager.TicksGame;

            Find.TickManager.DebugSetTicksGame(fired + 14 * GenDate.TicksPerDay);
            Assert.False(pack.Worker.CanFireNow(Parms()), "fourteen days on, the narrator may not send another pack");
            Find.TickManager.DebugSetTicksGame(fired + 15 * GenDate.TicksPerDay);
            Assert.True(pack.Worker.CanFireNow(Parms()), "fifteen days on, it may");
        }

        [Fact]
        public void Among_the_big_threats_a_raid_is_more_than_four_times_as_likely_as_a_pack()
        {
            Assert.True(Def("RaidEnemy").baseChance >= 4f * Def("ManhunterPack").baseChance,
                $"raid {Def("RaidEnemy").baseChance} against pack {Def("ManhunterPack").baseChance}");
            Assert.Equal(0f, Def("RaidEnemy").minRefireDays);
        }
    }
}
