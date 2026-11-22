using SimWorld.Conditions;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Tests.Content;
using SimWorld.Weather;
using SimWorld.World;

using Xunit;

using CoreWorld = SimWorld.World.World;

namespace SimWorld.Tests.Conditions
{
    /// <summary>
    /// When a heat wave or a cold snap may come, how long it stays and how it comes on, as RimWorld 1.0 has them
    /// (<see cref="IncidentWorker_SeasonalTemperature"/>, <see cref="GameCondition_TemperatureOffset"/>). Before
    /// this a cold snap could break out in a desert summer, a wave could last eight days, and it began at full
    /// strength between one tick and the next.
    /// </summary>
    public class SeasonalTemperatureTests : ContentTestBase
    {
        public SeasonalTemperatureTests(CoreContentFixture content) : base(content)
        {
        }

        /// <summary>A civilization seated on a tile whose mean for the season is <paramref name="seasonal"/> now, past
        /// the opening days.</summary>
        private static CivilizationTarget SeatedWhere(float seasonal)
        {
            WorldGrid grid = WorldGrid.Generate(1);
            var world = new CoreWorld { grid = grid };
            Find.World = world;
            Find.TickManager.DebugSetTicksGame(GenDate.TicksPerDay * 20);
            const int Tile = 3;
            MapClimate climate = MapClimate.ForTile(world, Tile)!;
            grid.Tiles[Tile].temperature =
                seasonal - GenTemperature.OffsetFromSeasonCycle(Find.TickManager.TicksAbs, climate.latitude, climate.longitude);
            var target = new CivilizationTarget();
            target.SetSettlements(new[] { new Settlement(WorldObjectDefOf.Settlement, Tile, null, "Seat", 0) });
            return target;
        }

        private static bool CanFire(IncidentDef incident, CivilizationTarget target) =>
            incident.Worker.CanFireNow(new IncidentParms { target = target });

        // RimWorld: IncidentWorker_HeatWave, SeasonalTemp >= 20; IncidentWorker_ColdSnap, 0 < SeasonalTemp < 15.
        [Theory]
        [InlineData(30f, true, false)]
        [InlineData(20.5f, true, false)]
        [InlineData(19.5f, false, false)]
        [InlineData(15.5f, false, false)]
        [InlineData(14.5f, false, true)]
        [InlineData(0.5f, false, true)]
        [InlineData(-0.5f, false, false)]
        public void Each_is_chosen_only_in_its_own_season_at_the_seat(float seasonal, bool heatWave, bool coldSnap)
        {
            CivilizationTarget target = SeatedWhere(seasonal);
            Assert.Equal(heatWave, CanFire(ConditionIncidentDefOf.HeatWave, target));
            Assert.Equal(coldSnap, CanFire(ConditionIncidentDefOf.ColdSnap, target));
        }

        [Fact]
        public void Neither_comes_in_the_first_five_days_nor_again_within_thirty()
        {
            CivilizationTarget hot = SeatedWhere(30f);
            Find.TickManager.DebugSetTicksGame(GenDate.TicksPerDay * 5 - 1);
            Assert.False(CanFire(ConditionIncidentDefOf.HeatWave, hot));
            Find.TickManager.DebugSetTicksGame(GenDate.TicksPerDay * 5);
            Assert.True(CanFire(ConditionIncidentDefOf.HeatWave, hot));

            Assert.True(ConditionIncidentDefOf.HeatWave.Worker.TryExecute(new IncidentParms { target = hot }));
            Find.World!.gameConditionManager.ActiveConditions[0].duration = 0;
            Find.World.gameConditionManager.GameConditionManagerTick();
            Assert.Empty(Find.World.gameConditionManager.ActiveConditions);
            Find.TickManager.DebugSetTicksGame(GenDate.TicksPerDay * 35 - 1);
            Assert.False(CanFire(ConditionIncidentDefOf.HeatWave, hot));
            Find.TickManager.DebugSetTicksGame(GenDate.TicksPerDay * 35);
            Assert.True(CanFire(ConditionIncidentDefOf.HeatWave, hot));

            CivilizationTarget cool = SeatedWhere(10f);
            Find.TickManager.DebugSetTicksGame(GenDate.TicksPerDay * 4);
            Assert.False(CanFire(ConditionIncidentDefOf.ColdSnap, cool));
            Assert.Equal(ConditionIncidentDefOf.HeatWave.minRefireDays, ConditionIncidentDefOf.ColdSnap.minRefireDays);
        }

        /// <summary>RimWorld's own lengths (<c>Incidents_Map_Misc.xml</c>).</summary>
        [Fact]
        public void Both_last_a_day_and_a_half_to_three_and_a_half()
        {
            foreach (GameConditionDef def in new[] { GameConditionDefOf.HeatWave, GameConditionDefOf.ColdSnap })
            {
                Assert.Equal(1.5f, def.durationDays.min);
                Assert.Equal(3.5f, def.durationDays.max);
            }
        }

        [Fact]
        public void It_comes_on_and_goes_off_over_a_few_hours()
        {
            Find.World = new CoreWorld();
            GameCondition wave = GameConditionMaker.MakeConditionForDays(GameConditionDefOf.HeatWave, 2f);
            Find.World.gameConditionManager.RegisterCondition(wave);
            float full = GameConditionDefOf.HeatWave.temperatureOffset;
            int lerp = GameCondition_TemperatureOffset.LerpTicks;
            int start = wave.startTick;

            float At(int ticks)
            {
                Find.TickManager.DebugSetTicksGame(start + ticks);
                return wave.TemperatureOffset();
            }

            Assert.Equal(0f, At(0), 3);
            Assert.Equal(full / 2f, At(lerp / 2), 3);
            Assert.Equal(full, At(lerp), 3);
            Assert.Equal(full, At(wave.duration - lerp), 3);
            Assert.Equal(full / 4f, At(wave.duration - lerp / 4), 3);
            Assert.Equal(0f, At(wave.duration), 3);
        }

        [Fact]
        public void A_permanent_one_comes_on_and_stays()
        {
            Find.World = new CoreWorld();
            GameCondition snap = GameConditionMaker.MakeCondition(GameConditionDefOf.ColdSnap, GameCondition.PermanentDuration);
            Find.World.gameConditionManager.RegisterCondition(snap);
            Find.TickManager.DebugSetTicksGame(snap.startTick + GameCondition_TemperatureOffset.LerpTicks / 2);
            Assert.Equal(GameConditionDefOf.ColdSnap.temperatureOffset / 2f, snap.TemperatureOffset(), 3);
            Find.TickManager.DebugSetTicksGame(snap.startTick + GenDate.TicksPerYear);
            Assert.Equal(GameConditionDefOf.ColdSnap.temperatureOffset, snap.TemperatureOffset(), 3);
        }
    }
}
