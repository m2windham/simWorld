using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Weather
{
    /// <summary>
    /// Chooses what the weather does next, and when (RimWorld: <c>Verse.WeatherDecider</c>, one per map
    /// alongside its <see cref="WeatherManager"/>).
    /// <para/>
    /// <b>How a weather is chosen.</b> Every <see cref="WeatherDef"/> is weighted by
    /// <see cref="CurrentWeatherCommonality"/> and one is drawn by weight through the seeded stream
    /// (<see cref="Rand.Current"/>) — never <c>System.Random</c>, so a given seed always produces the same
    /// weather history (CLAUDE.md). The weight folds in three gates, all RimWorld's:
    /// <list type="bullet">
    /// <item><description>the biome, through <see cref="WeatherDef.CommonalityIn"/> — a desert rains less
    /// than a rainforest;</description></item>
    /// <item><description>the tile's annual rainfall, through <see cref="WeatherDef.commonalityRainfallCurve"/>;</description></item>
    /// <item><description>the map's current outdoor temperature, through <see cref="WeatherDef.temperatureRange"/>
    /// — which is what makes a cold map snow and a warm one rain, without either needing to be listed
    /// anywhere.</description></item>
    /// </list>
    /// <b>Not ported: <c>DisableRainFor</c>.</b> RimWorld's decider can be told to hold the rain off for a
    /// while (its storyteller uses it so a fire incident is not quenched the moment it lands). Nothing in
    /// this codebase would ever call it — the incidents that would (<c>HeatWave</c>, <c>ColdSnap</c>,
    /// <c>Flashstorm</c>) are still <c>IncidentWorker_Placeholder</c> — and an entry point with no caller is
    /// the dormancy this module was sent to remove, not add. It is a three-line addition the day an incident
    /// worker needs it.
    /// </summary>
    public sealed class WeatherDecider : IExposable
    {
        /// <summary>How long the weather a map starts with lasts before the first real choice is made.
        /// Unsourced (RimWorld's own initial value is a field default this port could not read); it only
        /// decides when the first transition happens, and the test that matters asserts that one does.</summary>
        public const int InitialWeatherDurationTicks = 10000;

        private readonly WeatherManager manager;

        private int curWeatherDuration = InitialWeatherDurationTicks;

        public WeatherDecider(WeatherManager manager)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>Ticks the current weather is due to last.</summary>
        public int CurWeatherDuration => curWeatherDuration;

        public void WeatherDeciderTick()
        {
            if (manager.CurWeatherAge < curWeatherDuration) return;
            StartNextWeather();
        }

        /// <summary>Picks the next weather and transitions to it (RimWorld: <c>WeatherDecider.StartNextWeather</c>).
        /// Public because it is also the honest way for anything else — a test, a future incident — to force
        /// the weather to move on rather than reaching into the manager's fields.</summary>
        public void StartNextWeather()
        {
            WeatherDef? next = ChooseNextWeather();
            if (next == null)
            {
                // Nothing is eligible at this temperature in this biome (content could make that happen).
                // Sit out another spell rather than re-rolling every tick for the rest of the game.
                curWeatherDuration = InitialWeatherDurationTicks;
                return;
            }
            curWeatherDuration = Rand.RangeInclusive(next.durationRange.min, next.durationRange.max);
            manager.TransitionTo(next);
        }

        /// <summary>The next weather, or null when the content offers none that can occur here.</summary>
        public WeatherDef? ChooseNextWeather()
        {
            IReadOnlyList<WeatherDef> all = DefDatabase<WeatherDef>.AllDefsListForReading;
            if (all.Count == 0) return null;
            return GenCollection.TryRandomElementByWeight(all, CurrentWeatherCommonality, Rand.Current, out WeatherDef picked)
                ? picked
                : null;
        }

        /// <summary>
        /// How likely <paramref name="weather"/> is to be chosen right now, 0 meaning it cannot be
        /// (RimWorld: <c>WeatherDecider.CurrentWeatherCommonality</c>).
        /// </summary>
        public float CurrentWeatherCommonality(WeatherDef weather)
        {
            if (weather == null) throw new ArgumentNullException(nameof(weather));
            if (!weather.repeatable && ReferenceEquals(weather, manager.CurWeather)) return 0f;

            // The map's own outdoor temperature, exactly as RimWorld gates on map.mapTemperature.OutdoorTemp.
            // On a map with a world tile that number is already the season/hour/weather model's own output
            // (GenTemperature); on one without, it is whatever its owner set — which is still the right thing
            // to ask, and is what lets a bare map decide between rain and snow at all.
            if (!weather.temperatureRange.Includes(manager.Map.outdoorTemperature)) return 0f;

            MapClimate? climate = manager.Climate;
            float commonality = weather.CommonalityIn(climate?.biome);
            if (commonality <= 0f) return 0f;

            if (weather.commonalityRainfallCurve != null && climate != null)
            {
                commonality *= weather.commonalityRainfallCurve.Evaluate(climate.rainfall);
            }
            return Math.Max(0f, commonality);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref curWeatherDuration, "curWeatherDuration", InitialWeatherDurationTicks);
        }
    }
}
