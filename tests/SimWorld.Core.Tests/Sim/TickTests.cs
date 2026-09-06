using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Sim;
using Xunit;

namespace SimWorld.Tests.Sim
{
    internal sealed class Ticker : ITickable
    {
        public int TickId { get; }
        public bool Destroyed { get; set; }
        public TickerType TickerType { get; }
        public List<int> TickedAt { get; } = new List<int>();
        public Action? OnTick { get; set; }
        public int Ticks => TickedAt.Count;
        private readonly Func<int> now;

        public Ticker(int id, TickerType type, Func<int> now)
        {
            TickId = id;
            TickerType = type;
            this.now = now;
        }

        public void Tick() => Record();
        public void TickRare() => Record();
        public void TickLong() => Record();

        private void Record()
        {
            TickedAt.Add(now());
            OnTick?.Invoke();
        }
    }

    public class TickListTests
    {
        [Fact]
        public void Rare_list_ticks_each_entry_once_per_interval_in_its_bucket()
        {
            int t = 0;
            var list = new TickList(TickerType.Rare);
            var tickers = Enumerable.Range(0, 500).Select(i => new Ticker(i, TickerType.Rare, () => t)).ToList();
            tickers.ForEach(list.RegisterThing);
            Assert.Equal(500, list.Count);
            Assert.Equal(GenTicks.TickRareInterval, list.TickInterval);

            for (t = 1; t <= 500; t++) list.Tick(t);

            Assert.All(tickers, k => Assert.Equal(2, k.Ticks));
            Assert.All(tickers, k => Assert.All(k.TickedAt, at => Assert.Equal(k.TickId % 250, at % 250)));
        }

        [Fact]
        public void Normal_list_ticks_every_tick_and_long_every_2000()
        {
            int t = 0;
            var normal = new TickList(TickerType.Normal);
            var lng = new TickList(TickerType.Long);
            var a = new Ticker(0, TickerType.Normal, () => t);
            var b = new Ticker(0, TickerType.Long, () => t);
            normal.RegisterThing(a);
            lng.RegisterThing(b);
            for (t = 1; t <= 4000; t++) { normal.Tick(t); lng.Tick(t); }
            Assert.Equal(4000, a.Ticks);
            Assert.Equal(new[] { 2000, 4000 }, b.TickedAt);
        }

        [Fact]
        public void Registration_during_a_tick_takes_effect_next_tick()
        {
            int t = 0;
            var list = new TickList(TickerType.Normal);
            var late = new Ticker(0, TickerType.Normal, () => t);
            var early = new Ticker(0, TickerType.Normal, () => t) { OnTick = () => list.RegisterThing(late) };
            early.OnTick = () => { if (!list.Contains(late)) list.RegisterThing(late); };
            list.RegisterThing(early);

            t = 1; list.Tick(t);
            Assert.Empty(late.TickedAt);
            Assert.True(list.Contains(late));
            t = 2; list.Tick(t);
            Assert.Equal(new[] { 2 }, late.TickedAt);
        }

        [Fact]
        public void Deregistration_during_a_tick_is_honoured_afterwards()
        {
            int t = 0;
            var list = new TickList(TickerType.Normal);
            Ticker? self = null;
            self = new Ticker(0, TickerType.Normal, () => t) { OnTick = () => list.DeregisterThing(self!) };
            list.RegisterThing(self);
            t = 1; list.Tick(t);
            t = 2; list.Tick(t);
            Assert.Equal(new[] { 1 }, self.TickedAt);
            Assert.False(list.Contains(self));
            Assert.Equal(0, list.Count);
        }

        [Fact]
        public void Destroyed_entries_are_skipped_and_dropped()
        {
            int t = 0;
            var list = new TickList(TickerType.Normal);
            var k = new Ticker(0, TickerType.Normal, () => t);
            list.RegisterThing(k);
            t = 1; list.Tick(t);
            k.Destroyed = true;
            t = 2; list.Tick(t);
            Assert.Equal(new[] { 1 }, k.TickedAt);
            Assert.Equal(0, list.Count);
        }

        [Fact]
        public void Negative_ids_land_in_a_valid_bucket()
        {
            int t = 0;
            var list = new TickList(TickerType.Rare);
            var k = new Ticker(-3, TickerType.Rare, () => t);
            Assert.Equal(247, list.BucketOf(k));
            list.RegisterThing(k);
            for (t = 1; t <= 250; t++) list.Tick(t);
            Assert.Equal(new[] { 247 }, k.TickedAt);
        }

        [Fact]
        public void Never_has_no_list()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TickList(TickerType.Never));
        }
    }

    public class TickManagerTests
    {
        [Fact]
        public void Single_tick_runs_phases_in_order()
        {
            var tm = new TickManager();
            var log = new List<string>();
            tm.PreTickers.Add(t => log.Add("pre"));
            tm.PostTickers.Add(t => log.Add("post"));
            var normal = new Ticker(0, TickerType.Normal, () => tm.TicksGame) { OnTick = () => log.Add("normal") };
            var rare = new Ticker(0, TickerType.Rare, () => tm.TicksGame) { OnTick = () => log.Add("rare") };
            var lng = new Ticker(0, TickerType.Long, () => tm.TicksGame) { OnTick = () => log.Add("long") };
            var never = new Ticker(0, TickerType.Never, () => tm.TicksGame) { OnTick = () => log.Add("never") };
            tm.RegisterAllTickabilityFor(normal);
            tm.RegisterAllTickabilityFor(rare);
            tm.RegisterAllTickabilityFor(lng);
            tm.RegisterAllTickabilityFor(never);

            tm.DebugSetTicksGame(1999);
            tm.DoSingleTick();

            Assert.Equal(2000, tm.TicksGame);
            Assert.Equal(new[] { "pre", "normal", "rare", "long", "post" }, log);
            tm.DeRegisterAllTickabilityFor(normal);
            tm.DoSingleTick();
            Assert.Equal(1, normal.Ticks);
        }

        [Fact]
        public void Absolute_ticks_include_the_start_offset()
        {
            var tm = new TickManager(GenDate.TicksPerDay * 3);
            tm.DoSingleTick();
            Assert.Equal(1, tm.TicksGame);
            Assert.Equal(GenDate.TicksPerDay * 3 + 1, tm.TicksAbs);
        }

        [Theory]
        [InlineData(TimeSpeed.Paused, 0f)]
        [InlineData(TimeSpeed.Normal, 1f)]
        [InlineData(TimeSpeed.Fast, 3f)]
        [InlineData(TimeSpeed.Superfast, 6f)]
        [InlineData(TimeSpeed.Ultrafast, 15f)]
        public void Tick_rate_multiplier_per_speed(TimeSpeed speed, float expected)
        {
            var tm = new TickManager { CurTimeSpeed = speed };
            Assert.Equal(expected, tm.TickRateMultiplier);
        }

        [Fact]
        public void Danger_forces_normal_speed_for_800_ticks()
        {
            var tm = new TickManager { CurTimeSpeed = TimeSpeed.Ultrafast };
            tm.Slower.SignalForceNormalSpeed(tm.TicksGame);
            Assert.Equal(1f, tm.TickRateMultiplier);
            tm.DebugSetTicksGame(799);
            Assert.Equal(1f, tm.TickRateMultiplier);
            tm.DebugSetTicksGame(800);
            Assert.Equal(15f, tm.TickRateMultiplier);
        }

        [Fact]
        public void Real_time_converts_to_ticks_at_each_speed()
        {
            foreach (var (speed, perSecond) in new[] { (TimeSpeed.Normal, 60), (TimeSpeed.Fast, 180), (TimeSpeed.Superfast, 360), (TimeSpeed.Ultrafast, 900) })
            {
                var tm = new TickManager { CurTimeSpeed = speed };
                int ran = 0;
                for (int frame = 0; frame < 600; frame++) ran += tm.TickManagerUpdate(1f / 60f);
                Assert.InRange(ran, perSecond * 10 - 3, perSecond * 10 + 3);
                Assert.Equal(ran, tm.TicksGame);
            }
        }

        [Fact]
        public void A_slow_frame_is_capped_and_its_backlog_dropped()
        {
            var tm = new TickManager { CurTimeSpeed = TimeSpeed.Normal };
            Assert.Equal(2, tm.TickManagerUpdate(1f));
            Assert.Equal(0f, tm.RealTimeToTickThrough);
            Assert.Equal(1, tm.TickManagerUpdate(1f / 60f));
        }

        [Fact]
        public void Paused_runs_nothing_and_toggle_restores_the_previous_speed()
        {
            var tm = new TickManager { CurTimeSpeed = TimeSpeed.Fast };
            tm.Pause();
            Assert.True(tm.Paused);
            Assert.Equal(0, tm.TickManagerUpdate(1f));
            tm.TogglePaused();
            Assert.Equal(TimeSpeed.Fast, tm.CurTimeSpeed);
            tm.TogglePaused();
            Assert.Equal(TimeSpeed.Paused, tm.CurTimeSpeed);
            tm.TogglePaused();
            Assert.Equal(TimeSpeed.Fast, tm.CurTimeSpeed);
        }

        [Fact]
        public void Clock_state_round_trips_through_Scribe()
        {
            var tm = new TickManager(500) { CurTimeSpeed = TimeSpeed.Superfast };
            tm.DebugSetTicksGame(1234);
            tm.Pause();

            string xml = Scribe.SaveToString(tm, "tickManager");
            var loaded = Scribe.Load<TickManager>(xml, "tickManager", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(1234, loaded.TicksGame);
            Assert.Equal(1734, loaded.TicksAbs);
            Assert.Equal(TimeSpeed.Paused, loaded.CurTimeSpeed);
            loaded.TogglePaused();
            Assert.Equal(TimeSpeed.Superfast, loaded.CurTimeSpeed);
        }
    }
}
