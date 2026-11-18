using System;
using System.Collections.Generic;
using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Noise;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Things;

namespace SimWorld.Weather
{
    /// <summary>
    /// The slow, map-wide work the environment does to cells a few at a time (RimWorld:
    /// <c>RimWorld.SteadyEnvironmentEffects</c>): snow falling on open ground while it snows and melting
    /// wherever it is above freezing — outdoors at the outdoor temperature, indoors at the room's; things left
    /// outside wearing down (<see cref="FinalDeteriorationRate"/>); rain thinning filth off open ground; and
    /// fires starting by themselves in a room hot enough to ignite what is in it
    /// (<see cref="AutoIgnitionTemperatureRange"/>).
    /// <para/>
    /// <b>The sweep.</b> As in RimWorld, each tick visits <c>ceil(area × </c><see cref="MapFractionCheckPerTick"/><c>)</c>
    /// cells, walking the whole map in a fixed shuffled order, so every cell is touched once every
    /// ~1/<see cref="MapFractionCheckPerTick"/> ticks (about 1,700) whatever the map's size. That is what makes
    /// snow lie unevenly in time across a map for a while, and what keeps the cost flat.
    /// <para/>
    /// <b>Determinism — three departures from RimWorld, all so this draws nothing from the ambient stream.</b>
    /// RimWorld seeds its snow noise with <c>Rand.Range(0, 651431)</c> the first time snow falls, and walks
    /// <c>map.cellsInRandomOrder</c>, which is shuffled from a seed taken off <c>Rand</c> too. Taking either
    /// draw here would shift every roll after the first snowfall in a run, for what is only the shape of a
    /// drift. Both are seeded from the map's own <see cref="Map.Map.uniqueID"/> instead, the same convention
    /// <see cref="WildPlantSpawner"/> follows, and both are stable across a save (RimWorld re-rolls its noise
    /// on load; this port does not need to). The third is every per-visit roll — deterioration, rain washing
    /// filth, auto-ignition — which RimWorld makes on <c>Rand</c>, one or two per thing per visit. Here each
    /// is a <see cref="RandomStream"/> seeded roll keyed on (map, tick, thing or cell, purpose), so it needs
    /// no saved state, survives a save unchanged, and leaves every other system's rolls where they were.
    /// <para/>
    /// <b>Rain putting fires out is not here, in RimWorld either.</b> RimWorld has no
    /// <c>RainFireExtinguisher</c>: each <c>Fire</c> rolls the rain itself in its complex calcs. This port
    /// already has that as <see cref="Fire.TryExtinguishFromRain"/>, driven map-wide from
    /// <see cref="WeatherManager.WeatherManagerTick"/> on the fire's own interval.
    /// <para/>
    /// <b>Not ported</b> from the same RimWorld tick: <c>RollForRainFire</c> (rain shorting out exposed
    /// electrical buildings; this port has no short circuit), the <c>ProtectedByEdifice</c> exemption (no
    /// building here has <c>preventDeteriorationOnTop</c> — there are no shelves), deterioration of a corpse's
    /// worn apparel, the heat-glow fleck (a renderer concern), the filth <c>DisappearAfterTicks</c> check
    /// (filth already expires itself in <see cref="Filth.Filth.TickRare"/>), and
    /// <c>GameConditionManager.DoSteadyEffects</c> (neither condition this port has — drought, flashstorm —
    /// does anything per cell, so the hook would have nothing to call).
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

        /// <summary>Deterioration factor for a thing under open sky (RimWorld: the <c>!roofed</c> term of
        /// <c>FinalDeteriorationRate</c>, "DeterioratingUnroofed"; recalled).</summary>
        public const float UnroofedDeteriorationFactor = 0.5f;

        /// <summary>Deterioration factor for a thing in a room that takes the outdoor temperature (RimWorld:
        /// "DeterioratingOutdoors"; recalled). An item in the open gets both halves, one under a lone roof
        /// tile out in the field gets only this one, and one indoors gets neither.</summary>
        public const float OutdoorsDeteriorationFactor = 0.5f;

        /// <summary>Visits a cell gets per day: 60,000 ticks × <see cref="MapFractionCheckPerTick"/>. RimWorld's
        /// literal <c>/ 36f</c> in <c>TryDoDeteriorate</c> (recalled), and what makes <c>DeteriorationRate</c>
        /// read as hit points per day.</summary>
        public const float VisitsPerDay = 36f;

        /// <summary>Below this a thing is not rolled for deterioration at all (RimWorld's <c>num &lt; 0.001f</c>).</summary>
        public const float MinDeteriorationRate = 0.001f;

        /// <summary>A room hotter than the minimum may set what is in it alight, most readily at the maximum
        /// (RimWorld: <c>AutoIgnitionTemperatureRange</c>, 240..1000 °C; recalled). Nothing in this port heats a
        /// room this far yet — heaters stop at their target, fire pushes no heat — so this is RimWorld's rule
        /// waiting on a heat source rather than one a run meets today.</summary>
        public static readonly FloatRange AutoIgnitionTemperatureRange = new FloatRange(240f, 1000f);

        /// <summary>Chance per visit at the top of <see cref="AutoIgnitionTemperatureRange"/> (RimWorld's
        /// <c>* 0.7f</c>; recalled), before the cell's own flammability is rolled.</summary>
        public const float AutoIgnitionMaxChance = 0.7f;

        private const int PurposeDeteriorate = 1;
        private const int PurposeRainWash = 2;
        private const int PurposeIgnite = 3;
        private const int PurposeIgniteFuel = 4;

        private readonly Map.Map map;
        private int cycleIndex;

        private int[]? cellsInRandomOrder;
        private Perlin? snowNoise;

        private float outdoorMeltAmount;
        private float snowRate;
        private float rainRate;
        private int tick;

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
            rainRate = map.weatherManager.RainRate;
            tick = Find.TickManager.TicksGame;

            int area = map.Area;
            int perTick = (int)Math.Ceiling(area * MapFractionCheckPerTick);

            // No idle shortcut: the snow half skipped a warm, dry, snowless map, but things left outside
            // wear down in any weather, so any visit can do something.
            int[] order = CellsInRandomOrder();
            for (int i = 0; i < perTick; i++)
            {
                if (cycleIndex >= area) cycleIndex = 0;
                DoCellSteadyEffects(map.cellIndices.IndexToCell(order[cycleIndex]));
                cycleIndex++;
            }
        }

        /// <summary>RimWorld: <c>SteadyEnvironmentEffects.DoCellSteadyEffects</c>; see the class doc for the
        /// lines left out.</summary>
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

            if (room == null) return;

            TerrainDef terrain = map.terrainGrid.TerrainAt(c);
            IReadOnlyList<Thing> things = map.thingGrid.ThingsListAt(c);
            // Backwards, because thinning filth or wearing a thing away removes it from this very list.
            // RimWorld walks it forwards and so skips the thing after one that goes; the rolls here are keyed
            // by thing, not by position in the list, so the direction changes nothing else.
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (i >= things.Count) continue;
                Thing thing = things[i];
                if (thing is Filth.Filth filth)
                {
                    if (!roofed && filth.Props.rainWashes && rainRate > 0f
                        && RandomStream.ChanceSeeded(rainRate, SeedFor(thing.thingIDNumber, PurposeRainWash)))
                    {
                        filth.ThinFilth();
                    }
                }
                else
                {
                    TryDoDeteriorate(thing, roofed, usesOutdoorTemperature, terrain);
                }
            }

            if (!usesOutdoorTemperature)
            {
                float temperature = room.Temperature;
                if (temperature > 0f)
                {
                    float melt = MeltAmountAt(temperature);
                    if (melt > 0f) map.snowGrid.AddDepth(c, -melt);
                    if (temperature > AutoIgnitionTemperatureRange.min) TryAutoIgnite(c, temperature);
                }
            }
        }

        /// <summary>RimWorld: <c>TryDoDeteriorate</c> — one hit point off, at a chance that makes
        /// <see cref="FinalDeteriorationRate"/> come out as hit points per day.</summary>
        private void TryDoDeteriorate(Thing t, bool roofed, bool roomUsesOutdoorTemperature, TerrainDef terrain)
        {
            float rate = FinalDeteriorationRate(t, roofed, roomUsesOutdoorTemperature, terrain);
            if (rate < MinDeteriorationRate) return;
            if (!RandomStream.ChanceSeeded(rate / VisitsPerDay, SeedFor(t.thingIDNumber, PurposeDeteriorate))) return;
            t.TakeDamage(new DamageInfo(DeteriorationDefOf.Deterioration, 1f));
        }

        /// <summary>
        /// Hit points per day <paramref name="t"/> loses where it lies (RimWorld:
        /// <c>SteadyEnvironmentEffects.FinalDeteriorationRate</c>): its <c>DeteriorationRate</c> stat times
        /// the sum of <see cref="UnroofedDeteriorationFactor"/> under open sky,
        /// <see cref="OutdoorsDeteriorationFactor"/> in a room open to the outdoors, and the terrain's
        /// <see cref="TerrainDef.extraDeteriorationFactor"/>. Nothing indoors on dry ground wears at all.
        /// <para/>
        /// RimWorld 1.0 scaled deterioration by the rain instead (<c>Lerp(1, 5, rainRate)</c>); the factors
        /// above replaced that from 1.1 on, as recalled, and are what is ported — rain does not speed it.
        /// </summary>
        public static float FinalDeteriorationRate(Thing t, bool roofed, bool roomUsesOutdoorTemperature, TerrainDef? terrain)
        {
            if (!t.def.CanEverDeteriorate) return 0f;
            float stat = t.GetStatValue(DeteriorationDefOf.DeteriorationRate);
            if (stat <= 0f) return 0f;
            float factor = 0f;
            if (!roofed) factor += UnroofedDeteriorationFactor;
            if (roomUsesOutdoorTemperature) factor += OutdoorsDeteriorationFactor;
            if (terrain != null) factor += terrain.extraDeteriorationFactor;
            if (factor <= 0f) return 0f;
            return stat * factor;
        }

        /// <summary>RimWorld: the auto-ignition lines of <c>DoCellSteadyEffects</c> — a chance rising across
        /// <see cref="AutoIgnitionTemperatureRange"/> to <see cref="AutoIgnitionMaxChance"/>, then the cell's
        /// own <see cref="FireUtility.ChanceToStartFireIn"/>, then a fire at <see cref="Fire.MinFireSize"/>.</summary>
        private void TryAutoIgnite(IntVec3 c, float temperature)
        {
            int cellIndex = map.cellIndices.CellToIndex(c);
            float heat = GenMath.InverseLerp(AutoIgnitionTemperatureRange.min, AutoIgnitionTemperatureRange.max, temperature);
            if (RandomStream.ValueSeeded(SeedFor(cellIndex, PurposeIgnite)) >= heat * AutoIgnitionMaxChance) return;
            if (!RandomStream.ChanceSeeded(FireUtility.ChanceToStartFireIn(c, map), SeedFor(cellIndex, PurposeIgniteFuel))) return;
            FireUtility.TryStartFireIn(c, map, Fire.MinFireSize);
        }

        /// <summary>A seed unique to (map, tick, key, purpose), mixed the way <see cref="WildPlantSpawner"/>
        /// mixes its own; <see cref="RandomStream"/>'s seeded statics hash it before use.</summary>
        private int SeedFor(int key, int purpose)
        {
            unchecked
            {
                uint hash = (uint)map.uniqueID * 2654435761u;
                hash ^= (uint)tick * 2246822519u;
                hash ^= (uint)key * 3266489917u;
                hash ^= (uint)purpose * 668265263u;
                return (int)hash;
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
