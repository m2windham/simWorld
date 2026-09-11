using System;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Weather
{
    /// <summary>
    /// What a map's world tile says about its climate, resolved once and cached
    /// (RimWorld: <c>Map.Biome</c> / <c>Map.TileInfo</c>, which are properties on Map straight through to
    /// <c>Find.WorldGrid[tile]</c>).
    /// <para/>
    /// <b>Why this is a class and not four fields on <c>Map</c>.</b> A <see cref="Map.Map"/> carries a tile
    /// <i>index</i> and nothing else about the world (<c>MapGenerator</c> sets <c>map.tile</c> after
    /// construction); the world itself is reached through <see cref="Find.World"/>. That makes the lookup
    /// late — and optional: a map built straight from <c>new Map.Map(...)</c>, which is what most of this
    /// codebase's tests do, has no tile and no world behind it. Resolution therefore returns null rather than
    /// throwing, and <see cref="WeatherManager"/> treats "no climate" as a real, supported state: weather
    /// still happens (biome-independent commonalities), but nothing pretends to know the tile's temperature,
    /// so <see cref="Map.Map.outdoorTemperature"/> is left exactly as its owner set it.
    /// </summary>
    public sealed class MapClimate
    {
        /// <summary>The tile's biome, or null if world generation never assigned one.</summary>
        public BiomeDef? biome;

        /// <summary>Annual mean temperature, °C (<see cref="Tile.temperature"/>).</summary>
        public float averageTemperature;

        /// <summary>Annual rainfall, mm (<see cref="Tile.rainfall"/>).</summary>
        public float rainfall;

        /// <summary>Degrees north (positive) or south (negative); drives the size of the seasonal swing.</summary>
        public float latitude;

        /// <summary>Degrees east/west; shifts the local day, exactly as <see cref="GenDate"/> uses it.</summary>
        public float longitude;

        /// <summary>The climate of <paramref name="map"/>'s world tile, or null when it has none — no world is
        /// current, the map was never given a tile, or the tile index is not in this world's grid.</summary>
        public static MapClimate? TryResolve(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            World.World? world = Find.World;
            if (world == null || world.grid == null) return null;
            if (map.tile < 0 || map.tile >= world.grid.TilesCount) return null;

            Tile tile = world.grid.Tiles[map.tile];
            (double latitude, double longitude) = world.grid.LongLatOf(map.tile);
            return new MapClimate
            {
                biome = tile.biome,
                averageTemperature = tile.temperature,
                rainfall = tile.rainfall,
                latitude = (float)latitude,
                longitude = (float)longitude,
            };
        }
    }

    /// <summary>Tuning for the temperature cycles (RimWorld: <c>RimWorld.TemperatureTuning</c>).</summary>
    public static class TemperatureTuning
    {
        /// <summary>Seasonal swing (°C either side of the annual mean) by distance from the equator,
        /// 0 at the equator to 1 at a pole. RimWorld's own curve shape, recalled not decompiled: tropics
        /// barely have seasons, high latitudes swing hugely. Pinned by the trend, never the numbers.</summary>
        public static readonly SimpleCurve SeasonalTempVariationCurve = new SimpleCurve
        {
            { 0f, 3f },
            { 0.1f, 4f },
            { 1f, 28f },
        };

        /// <summary>°C the sun adds at noon and takes away at midnight (RimWorld: the <c>7f</c> in
        /// <c>GenTemperature.OffsetFromSunCycle</c>).</summary>
        public const float DailyTempVariationAmplitude = 7f;

        /// <summary>
        /// Ticks the seasonal cycle runs ahead of the calendar, so the year does not begin at midwinter
        /// (RimWorld: the <c>+300000</c> — five days — inside <c>OffsetFromSeasonCycle</c>). With it, the
        /// coldest point of a northern year falls in late Decembary and the warmest in Jugust, which is
        /// exactly the band <see cref="SeasonUtility.GetSeason"/> already calls Winter and Summer — the two
        /// models agree rather than each having its own idea of when summer is.
        /// </summary>
        public const int SeasonPhaseOffsetTicks = 5 * GenDate.TicksPerDay;
    }

    /// <summary>
    /// Outdoor temperature over the year and over the day (RimWorld: <c>Verse.GenTemperature</c> plus the
    /// tile-temperature cache <c>RimWorld.Planet.TileTemperaturesComp</c>, which this port does not need — it
    /// only ever asks for the map it is standing on, so there is nothing to cache across tiles).
    /// <para/>
    /// <b>What this extends rather than duplicates.</b> <see cref="Map.Map.outdoorTemperature"/> already
    /// existed, already fed rooms (<c>Building.RoomTracker</c>), plant growth
    /// (<c>Building.PlantUtility.GrowthRateFactor_Temperature</c>) and food spoilage
    /// (<c>Things.CompRottable</c>) — and was a flat settable number because, in its own words, "no
    /// biome/season model exists yet". One does now, and this is it; the field, its consumers and its Scribe
    /// entry are untouched. Nothing here is a second notion of temperature.
    /// <para/>
    /// <b>Deliberately not ported.</b> RimWorld also adds <c>OffsetFromDailyRandomVariation</c>, a
    /// per-day-per-tile random wobble. Its shape could not be sourced here and no consumer needs it; a
    /// guessed random term would only make the sequence harder to reason about for no behaviour anyone can
    /// point at. Everything below is a pure function of tick and tile: no randomness, so no
    /// <c>RandomStream</c> draw and nothing for a save/load to get out of step with.
    /// </summary>
    public static class GenTemperature
    {
        private const float TwoPi = 6.28318530718f;

        /// <summary>How far this latitude's temperature swings either side of its annual mean. Negative in the
        /// southern hemisphere, which is how one cosine serves both hemispheres (RimWorld does the same).</summary>
        public static float SeasonalShiftAmplitudeAt(float latitude)
        {
            float amplitude = TemperatureTuning.SeasonalTempVariationCurve.Evaluate(Math.Min(1f, Math.Abs(latitude) / 90f));
            return latitude >= 0f ? amplitude : -amplitude;
        }

        /// <summary>°C the season adds at <paramref name="absTicks"/> (RimWorld: <c>GenTemperature.OffsetFromSeasonCycle</c>).</summary>
        public static float OffsetFromSeasonCycle(long absTicks, float latitude, float longitude)
        {
            float yearPct = GenDate.YearPercent(absTicks + TemperatureTuning.SeasonPhaseOffsetTicks, longitude);
            return -(float)Math.Cos(yearPct * TwoPi) * SeasonalShiftAmplitudeAt(latitude);
        }

        /// <summary>°C the sun adds at <paramref name="absTicks"/>: warmest at local noon, coldest at local
        /// midnight (RimWorld: <c>GenTemperature.OffsetFromSunCycle</c>).</summary>
        public static float OffsetFromSunCycle(long absTicks, float longitude)
        {
            float dayPct = (float)GenDate.DayTick(absTicks, longitude) / GenDate.TicksPerDay;
            return (float)Math.Cos((dayPct - 0.5f) * TwoPi) * TemperatureTuning.DailyTempVariationAmplitude;
        }

        /// <summary>The annual mean plus the season, the sun and whatever is overhead — the number
        /// <see cref="Map.Map.outdoorTemperature"/> is set to.</summary>
        public static float OutdoorTemperatureAt(MapClimate climate, long absTicks, float weatherOffset)
        {
            if (climate == null) throw new ArgumentNullException(nameof(climate));
            return climate.averageTemperature
                + OffsetFromSeasonCycle(absTicks, climate.latitude, climate.longitude)
                + OffsetFromSunCycle(absTicks, climate.longitude)
                + weatherOffset;
        }
    }
}
