using System;
using SimWorld.Building;
using SimWorld.Map;
using SimWorld.Noise;
using SimWorld.Sim;

namespace SimWorld.Weather
{
    /// <summary>
    /// The slow, map-wide work the environment does to cells a few at a time (RimWorld:
    /// <c>RimWorld.SteadyEnvironmentEffects</c>), trimmed to its snow half: snow falling on open ground while
    /// it snows, and melting wherever it is above freezing — outdoors at the outdoor temperature, indoors at
    /// the room's.
    /// <para/>
    /// <b>The sweep.</b> As in RimWorld, each tick visits <c>ceil(area × </c><see cref="MapFractionCheckPerTick"/><c>)</c>
    /// cells, walking the whole map in a fixed shuffled order, so every cell is touched once every
    /// ~1/<see cref="MapFractionCheckPerTick"/> ticks (about 1,700) whatever the map's size. That is what makes
    /// snow lie unevenly in time across a map for a while, and what keeps the cost flat.
    /// <para/>
    /// <b>Determinism — two departures from RimWorld, both so this draws nothing from the ambient stream.</b>
    /// RimWorld seeds its snow noise with <c>Rand.Range(0, 651431)</c> the first time snow falls, and walks
    /// <c>map.cellsInRandomOrder</c>, which is shuffled from a seed taken off <c>Rand</c> too. Taking either
    /// draw here would shift every roll after the first snowfall in a run, for what is only the shape of a
    /// drift. Both are seeded from the map's own <see cref="Map.Map.uniqueID"/> instead, the same convention
    /// <see cref="WildPlantSpawner"/> follows, and both are stable across a save (RimWorld re-rolls its noise
    /// on load; this port does not need to).
    /// <para/>
    /// <b>Not ported</b> from the same RimWorld tick, because each needs a system this core lacks or belongs
    /// to another lane: rain-started fires on exposed buildings, rain-scaled deterioration of items left
    /// outside, rain washing filth, auto-ignition in very hot rooms, and <c>GameConditionManager.DoSteadyEffects</c>.
    /// </summary>
    public sealed class SteadyEnvironmentEffects : IExposable
    {
        /// <summary>Fraction of the map's cells visited per tick (RimWorld: <c>MapFractionCheckPerTick</c>).</summary>
        public const float MapFractionCheckPerTick = 0.0006f;

        /// <summary>Depth added to a cell per visit at full snow rate, before the noise (RimWorld:
        /// <c>SnowFallRateFactor</c>).</summary>
        public const float SnowFallRateFactor = 0.046f;

        /// <summary>Depth melted per visit per degree above freezing (RimWorld: <c>SnowMeltRateFactor</c>; see
        /// <see cref="MeltAmountAt"/>).</summary>
        public const float SnowMeltRateFactor = 0.0058f;

        /// <summary>Below this snow rate nothing falls (RimWorld: the <c>snowRate &gt; 0.001f</c> test).</summary>
        public const float MinSnowRate = 0.001f;

        private readonly Map.Map map;
        private int cycleIndex;

        private int[]? cellsInRandomOrder;
        private Perlin? snowNoise;

        private float outdoorMeltAmount;
        private float snowRate;

        public SteadyEnvironmentEffects(Map.Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
        }

        /// <summary>
        /// How much snow one visit melts at <paramref name="temperature"/> °C (RimWorld:
        /// <c>SteadyEnvironmentEffects.MeltAmountAt</c>): nothing at or below freezing, a quadratic ramp to
        /// 10 °C so a mild day barely touches it, then linear.
        /// </summary>
        public static float MeltAmountAt(float temperature)
        {
            if (temperature < 0f) return 0f;
            if (temperature < 10f) return temperature * temperature * SnowMeltRateFactor * 0.1f;
            return temperature * SnowMeltRateFactor;
        }

        public void SteadyEnvironmentEffectsTick()
        {
            outdoorMeltAmount = MeltAmountAt(map.outdoorTemperature);
            snowRate = map.weatherManager.SnowRate;

            int area = map.Area;
            int perTick = (int)Math.Ceiling(area * MapFractionCheckPerTick);

            // Nothing on the ground and nothing falling: every visit below would be a no-op, so only the
            // cursor moves. Equivalent to running them, and what keeps this free on a warm map.
            bool idle = snowRate <= MinSnowRate && map.snowGrid.TotalDepth <= 0f;
            if (idle)
            {
                cycleIndex = (int)(((long)cycleIndex + perTick) % area);
                return;
            }

            int[] order = CellsInRandomOrder();
            for (int i = 0; i < perTick; i++)
            {
                if (cycleIndex >= area) cycleIndex = 0;
                DoCellSteadyEffects(map.cellIndices.IndexToCell(order[cycleIndex]));
                cycleIndex++;
            }
        }

        /// <summary>RimWorld: <c>SteadyEnvironmentEffects.DoCellSteadyEffects</c>, snow lines only.</summary>
        private void DoCellSteadyEffects(IntVec3 c)
        {
            Room? room = map.roomTracker.RoomAt(c);
            bool roofed = map.roofGrid.Roofed(c);
            bool usesOutdoorTemperature = room != null && room.Group.UsesOutdoorTemperature;

            if ((room == null || usesOutdoorTemperature) && outdoorMeltAmount > 0f)
            {
                map.snowGrid.AddDepth(c, -outdoorMeltAmount);
            }

            if (room != null && !roofed && snowRate > MinSnowRate)
            {
                AddFallenSnowAt(c, SnowFallRateFactor * snowRate);
            }

            if (room != null && !usesOutdoorTemperature)
            {
                float temperature = room.Temperature;
                if (temperature > 0f)
                {
                    float melt = MeltAmountAt(temperature);
                    if (melt > 0f) map.snowGrid.AddDepth(c, -melt);
                }
            }
        }

        /// <summary>RimWorld: <c>AddFallenSnowAt</c> — the fall scaled by low-frequency noise into 0.5..1, so
        /// snow drifts deeper in some places than others rather than lying as a flat sheet.</summary>
        private void AddFallenSnowAt(IntVec3 c, float baseAmount)
        {
            snowNoise ??= new Perlin(0.04, 2.0, 0.5, 5, NoiseSeed(), NoiseQuality.Standard);
            float value = (float)snowNoise.GetValue(c.x, 0.0, c.z);
            value = (value + 1f) * 0.5f;
            if (value < 0.5f) value = 0.5f;
            map.snowGrid.AddDepth(c, baseAmount * value);
        }

        /// <summary>RimWorld draws this from <c>Rand.Range(0, 651431)</c>; see the class doc for why the map's
        /// id stands in for it.</summary>
        private int NoiseSeed() => (int)((uint)(map.uniqueID * 2654435761u ^ 0x5EEDu) % 651431u);

        /// <summary>RimWorld: <c>map.cellsInRandomOrder</c> — a fixed shuffle of every cell index, built once
        /// on first use from a stream seeded off the map's id.</summary>
        private int[] CellsInRandomOrder()
        {
            if (cellsInRandomOrder != null) return cellsInRandomOrder;
            int n = map.cellIndices.NumGridCells;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            var stream = new RandomStream(unchecked(map.uniqueID * 31 + 0x51ED));
            for (int i = n - 1; i > 0; i--)
            {
                int j = stream.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            cellsInRandomOrder = order;
            return order;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref cycleIndex, "cycleIndex", 0);
        }
    }
}
