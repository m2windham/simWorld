using System;
using System.Collections.Generic;

namespace SimWorld.Sim
{
    /// <summary>
    /// The simulation clock (RimWorld: <c>Verse.TickManager</c>). Owns game time, speed, the three tick
    /// lists, and the real-time → tick accumulator. Order inside one tick:
    /// pre-tickers (world, map pre-tick) → normal list → rare list → long list → post-tickers (map post-tick,
    /// director). Rendering never advances time; only <see cref="DoSingleTick"/> does.
    /// </summary>
    public sealed class TickManager : IExposable
    {
        /// <summary>Upper bound on ticks per frame relative to speed, so a hitch cannot run away.</summary>
        public const float MaxTicksPerFrameFactor = 2f;

        private int ticksGameInt;
        private int gameStartAbsTick;
        private TimeSpeed curTimeSpeed = TimeSpeed.Normal;
        private TimeSpeed prePauseTimeSpeed = TimeSpeed.Normal;
        private float realTimeToTickThrough;

        private readonly TickList tickListNormal = new TickList(TickerType.Normal);
        private readonly TickList tickListRare = new TickList(TickerType.Rare);
        private readonly TickList tickListLong = new TickList(TickerType.Long);

        public TimeSlower Slower { get; } = new TimeSlower();

        /// <summary>Run before the tick lists each tick, in order (world tick, map pre-tick).</summary>
        public List<Action<int>> PreTickers { get; } = new List<Action<int>>();

        /// <summary>Run after the tick lists each tick, in order (map post-tick, director, autosave).</summary>
        public List<Action<int>> PostTickers { get; } = new List<Action<int>>();

        public TickManager()
        {
        }

        /// <summary>Starts the clock at an absolute tick so calendar dates can differ from tick 0.</summary>
        public TickManager(int startAbsTick)
        {
            gameStartAbsTick = startAbsTick;
        }

        /// <summary>Ticks since this game began.</summary>
        public int TicksGame => ticksGameInt;

        /// <summary>Absolute calendar tick (game start offset + ticks game).</summary>
        public int TicksAbs => ticksGameInt + gameStartAbsTick;

        public int GameStartAbsTick => gameStartAbsTick;

        public TimeSpeed CurTimeSpeed
        {
            get => curTimeSpeed;
            set => curTimeSpeed = value;
        }

        public bool Paused => curTimeSpeed == TimeSpeed.Paused;

        public bool NotPaused => !Paused;

        public float RealTimeToTickThrough => realTimeToTickThrough;

        /// <summary>Ticks per real second divided by 60: 0 / 1 / 3 / 6 / 15, or 1 while a danger forces normal speed.</summary>
        public float TickRateMultiplier
        {
            get
            {
                switch (curTimeSpeed)
                {
                    case TimeSpeed.Paused: return 0f;
                    case TimeSpeed.Normal: return 1f;
                    case TimeSpeed.Fast: return Slower.ForcedNormalSpeed(ticksGameInt) ? 1f : 3f;
                    case TimeSpeed.Superfast: return Slower.ForcedNormalSpeed(ticksGameInt) ? 1f : 6f;
                    case TimeSpeed.Ultrafast: return Slower.ForcedNormalSpeed(ticksGameInt) ? 1f : 15f;
                    default: return -1f;
                }
            }
        }

        public void Pause()
        {
            if (curTimeSpeed != TimeSpeed.Paused)
            {
                prePauseTimeSpeed = curTimeSpeed;
                curTimeSpeed = TimeSpeed.Paused;
            }
        }

        public void TogglePaused()
        {
            if (curTimeSpeed != TimeSpeed.Paused)
            {
                Pause();
            }
            else
            {
                curTimeSpeed = prePauseTimeSpeed == TimeSpeed.Paused ? TimeSpeed.Normal : prePauseTimeSpeed;
            }
        }

        public void RegisterAllTickabilityFor(ITickable tickable)
        {
            if (tickable == null) throw new ArgumentNullException(nameof(tickable));
            TickList? list = TickListFor(tickable.TickerType);
            list?.RegisterThing(tickable);
        }

        public void DeRegisterAllTickabilityFor(ITickable tickable)
        {
            if (tickable == null) throw new ArgumentNullException(nameof(tickable));
            TickList? list = TickListFor(tickable.TickerType);
            list?.DeregisterThing(tickable);
        }

        public TickList? TickListFor(TickerType type)
        {
            switch (type)
            {
                case TickerType.Normal: return tickListNormal;
                case TickerType.Rare: return tickListRare;
                case TickerType.Long: return tickListLong;
                default: return null;
            }
        }

        /// <summary>Advances the simulation by exactly one tick regardless of speed or pause.</summary>
        public void DoSingleTick()
        {
            ticksGameInt++;
            int t = ticksGameInt;
            for (int i = 0; i < PreTickers.Count; i++) PreTickers[i](t);
            tickListNormal.Tick(t);
            tickListRare.Tick(t);
            tickListLong.Tick(t);
            for (int i = 0; i < PostTickers.Count; i++) PostTickers[i](t);
        }

        /// <summary>
        /// Converts real elapsed seconds into ticks at the current speed. At most
        /// <see cref="MaxTicksPerFrameFactor"/> × multiplier ticks run per call (RimWorld's per-frame cap, sized
        /// for ~60 fps) and any leftover backlog is dropped, so a slow frame slows the game rather than
        /// fast-forwarding it afterwards. Returns the number of ticks run.
        /// </summary>
        public int TickManagerUpdate(float deltaSeconds)
        {
            if (Paused) return 0;
            float multiplier = TickRateMultiplier;
            if (multiplier <= 0f) return 0;

            realTimeToTickThrough += Math.Min(deltaSeconds, 1f);
            float secondsPerTick = 1f / (GenTicks.TicksPerRealSecond * multiplier);
            int cap = (int)Math.Ceiling(multiplier * MaxTicksPerFrameFactor);

            int ran = 0;
            while (realTimeToTickThrough > 0f && ran < cap)
            {
                DoSingleTick();
                realTimeToTickThrough -= secondsPerTick;
                ran++;
            }
            if (realTimeToTickThrough > 0f)
            {
                realTimeToTickThrough = 0f;
            }
            return ran;
        }

        /// <summary>Debug/test helper: jumps the clock without ticking anything.</summary>
        public void DebugSetTicksGame(int newTicksGame)
        {
            ticksGameInt = newTicksGame;
        }

        public void ResetTickLists()
        {
            tickListNormal.Reset();
            tickListRare.Reset();
            tickListLong.Reset();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref ticksGameInt, "ticksGame");
            Scribe_Values.Look(ref gameStartAbsTick, "gameStartAbsTick");
            Scribe_Values.Look(ref curTimeSpeed, "curTimeSpeed", TimeSpeed.Normal);
            Scribe_Values.Look(ref prePauseTimeSpeed, "prePauseTimeSpeed", TimeSpeed.Normal);
        }
    }
}
