using System;
using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.Thoughts;

namespace SimWorld.Weather
{
    /// <summary>
    /// The weather over one map (RimWorld: <c>Verse.WeatherManager</c>): a current weather, the one it is
    /// still fading out of, and every rate a consumer reads lerped between the two.
    /// <para/>
    /// <b>Where it lives, and the one shared file this cost.</b> RimWorld hangs this off <c>Verse.Map</c> and
    /// so does this port — <c>Map.weatherManager</c>, constructed with the map's other managers, ticked from
    /// <c>Map.MapTick</c>, saved by <c>Map.ExposeData</c>. That is four lines in <c>Map/Map.cs</c>, a file
    /// several lanes share, and they were not avoidable the way the fire module avoided them: a fire is a
    /// <c>Thing</c>, so <c>ListerThings</c> already indexed it and already ticked it, while weather is not a
    /// thing anywhere and nothing existing would ever have called it. A weather system nothing ticks is
    /// precisely the dormant feature this lane was sent to fix, so the four lines are the honest price. (The
    /// tick reaches a running game for free: <c>Sim.Game.TickMaps</c> already calls <c>MapTick</c> on every
    /// live map.)
    /// <para/>
    /// <b>Per map, not per tile — and deliberately nothing at the god layer.</b> Weather exists only where a
    /// <c>Map</c> exists, which in this civilization-scale game means the settlement the player has actually
    /// opened (<c>World.Settlement.EnterMap</c> generates the interior lazily and most settlements are never
    /// opened at all). Simulating rain over hundreds of unvisited tiles would buy nothing: the things weather
    /// moves — a fire, a room's temperature, a plant's growth, somebody's mood — only exist on a generated
    /// map. See this module's report for why no <c>God/View</c> field was added either.
    /// <para/>
    /// <b>Cost.</b> Per map per tick: one decider comparison, one increment, and the outdoor-temperature
    /// recompute (two cosines). The two sweeps that touch every fire and every pawn are gated to intervals,
    /// and the fire sweep additionally to it actually raining.
    /// </summary>
    public sealed class WeatherManager : IExposable
    {
        /// <summary>How long a new weather takes to fully replace the old one (RimWorld:
        /// <c>WeatherManager.TransitionTicks</c>). Every rate below crosses linearly over this span.</summary>
        public const int TransitionTicks = 4000;

        /// <summary>How often a pawn caught in the open re-gains the weather's <see cref="WeatherDef.exposedThought"/>.
        /// Unsourced; the memory's own <c>durationDays</c> is what actually decides how long being rained on
        /// stays with somebody, and a pawn who goes inside simply stops refreshing it.</summary>
        public const int ExposedThoughtIntervalTicks = 500;

        /// <summary>The wind a map has before any weather touches it; every <see cref="WeatherDef.windSpeedFactor"/>
        /// is a multiple of this. Unitless, and 1 so that a weather's factor reads as the whole story.</summary>
        public const float BaseWindSpeed = 1f;

        private readonly Map.Map map;

        private WeatherDef? curWeather;
        private WeatherDef? lastWeather;
        private int curWeatherAge;

        private WeatherDecider decider;
        private MapClimate? climate;

        public WeatherManager(Map.Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            decider = new WeatherDecider(this);
        }

        public Map.Map Map => map;

        /// <summary>What it is doing now. Falls back to <see cref="WeatherDefOf.Clear"/> — a map starts clear,
        /// as RimWorld's does — and stays null only when no content is loaded at all, which every accessor
        /// below tolerates rather than throwing.</summary>
        public WeatherDef? CurWeather => curWeather ??= WeatherDefOf.Clear;

        /// <summary>What it was doing before the current transition started; the same as
        /// <see cref="CurWeather"/> once nothing is in transition.</summary>
        public WeatherDef? LastWeather => lastWeather ?? CurWeather;

        /// <summary>Ticks since the current weather started (RimWorld: <c>WeatherManager.curWeatherAge</c>).</summary>
        public int CurWeatherAge => curWeatherAge;

        public WeatherDecider Decider => decider;

        /// <summary>
        /// This map's climate, from its world tile. Resolved lazily and re-attempted until it succeeds, since
        /// a map is given its tile after construction and the world is reached through <see cref="Find"/>.
        /// Settable so a host — or a test — can hand a map a climate directly without a whole world behind it;
        /// null means "this map has no world tile", which is a supported state (see <see cref="MapClimate"/>).
        /// </summary>
        public MapClimate? Climate
        {
            get => climate ??= MapClimate.TryResolve(map);
            set => climate = value;
        }

        /// <summary>0 at the start of a transition, 1 once it has finished (RimWorld:
        /// <c>WeatherManager.TransitionLerpFactor</c>).</summary>
        public float TransitionLerpFactor =>
            curWeatherAge >= TransitionTicks ? 1f : (float)curWeatherAge / TransitionTicks;

        /// <summary>How hard it is raining, 0 to 1 (RimWorld: <c>WeatherManager.RainRate</c>). The number
        /// <c>Things.Fire.TryExtinguishFromRain</c> has been waiting for.</summary>
        public float RainRate => LerpedRate(static w => w.rainRate);

        /// <summary>How hard it is snowing, 0 to 1. Nothing accumulates it: this port has no snow grid, no
        /// <c>SnowDepth</c> and no movement or plant consequence from lying snow — see this module's report.
        /// It is exposed because snow is what a cold map does instead of raining, and the choice between the
        /// two is real (<see cref="WeatherDef.temperatureRange"/>).</summary>
        public float SnowRate => LerpedRate(static w => w.snowRate);

        /// <summary>
        /// Current wind speed, as <see cref="BaseWindSpeed"/> scaled and offset by the weather (RimWorld:
        /// <c>WeatherManager.WindSpeed</c>). Ported and lerped, with no consumer in this codebase yet — there
        /// is no wind turbine, no ambient sound and no sky to move — so it is exposed for the first thing that
        /// grows one rather than left to be re-derived. RimWorld's base wind is itself a wandering value; that
        /// wander is not ported, because it would mean a <c>RandomStream</c> draw every tick, on every map,
        /// that nothing whatsoever would read.
        /// </summary>
        public float WindSpeed => LerpedRate(static w => w.windSpeedOffset + w.windSpeedFactor * BaseWindSpeed);

        /// <summary>°C this weather is adding to the outdoor temperature right now.</summary>
        public float TemperatureOffset => LerpedRate(static w => w.temperatureOffset);

        /// <summary>Multiplier on a pawn's walking speed under open sky (RimWorld:
        /// <c>WeatherManager.CurMoveSpeedMultiplier</c>).</summary>
        public float CurMoveSpeedMultiplier => LerpedRate(static w => w.moveSpeedMultiplier, 1f);

        /// <summary>Multiplier on ranged hit chance when either end of the shot is under open sky (RimWorld:
        /// <c>WeatherManager.CurWeatherAccuracyMultiplier</c>).</summary>
        public float CurWeatherAccuracyMultiplier => LerpedRate(static w => w.accuracyMultiplier, 1f);

        private float LerpedRate(Func<WeatherDef, float> rate, float fallback = 0f)
        {
            WeatherDef? cur = CurWeather;
            if (cur == null) return fallback;
            WeatherDef prev = lastWeather ?? cur;
            return GenMath.Lerp(rate(prev), rate(cur), TransitionLerpFactor);
        }

        /// <summary>Starts a new weather, fading out of the current one (RimWorld: <c>WeatherManager.TransitionTo</c>).</summary>
        public void TransitionTo(WeatherDef newWeather)
        {
            if (newWeather == null) throw new ArgumentNullException(nameof(newWeather));
            lastWeather = CurWeather;
            curWeather = newWeather;
            curWeatherAge = 0;
        }

        /// <summary>
        /// One tick of weather (RimWorld: <c>WeatherManager.WeatherManagerTick</c>): let the decider decide,
        /// age the current weather, then apply what weather does — the outdoor temperature every tick, and
        /// the two sweeps on their own intervals.
        /// </summary>
        public void WeatherManagerTick()
        {
            decider.WeatherDeciderTick();
            curWeatherAge++;

            UpdateOutdoorTemperature();

            int ticks = Find.TickManager.TicksGame;
            // Fire's own complex-calc cadence, so a fire gets exactly one rain roll per interval whether the
            // roll is made by the fire (RimWorld) or, as here, map-wide on the fire module's behalf.
            if (ticks % Fire.ComplexCalcsInterval == 0) ExtinguishFiresFromRain();
            if (ticks % ExposedThoughtIntervalTicks == 0) GiveExposedThoughts();
        }

        /// <summary>
        /// Drives <see cref="Map.Map.outdoorTemperature"/> from the tile's climate, the season, the hour and
        /// whatever is overhead. A map with no world tile is left alone entirely: its outdoor temperature is
        /// whatever its owner set, exactly as it was before this module existed, so nothing that was tuned
        /// against a fixed temperature quietly starts drifting.
        /// </summary>
        private void UpdateOutdoorTemperature()
        {
            MapClimate? c = Climate;
            if (c == null) return;
            map.outdoorTemperature = GenTemperature.OutdoorTemperatureAt(c, Find.TickManager.TicksGame, TemperatureOffset);
        }

        /// <summary>Rolls every unroofed fire against the rain — the call site
        /// <c>Things.FireUtility.ExtinguishFiresFromRain</c> was written for and left waiting on.</summary>
        private void ExtinguishFiresFromRain()
        {
            float rainRate = RainRate;
            if (rainRate <= 0f) return;
            FireUtility.ExtinguishFiresFromRain(map, rainRate);
        }

        /// <summary>Gives every pawn standing under open sky the current weather's
        /// <see cref="WeatherDef.exposedThought"/> (RimWorld does this from this same tick).</summary>
        private void GiveExposedThoughts()
        {
            ThoughtDef? thought = CurWeather?.exposedThought;
            if (thought == null) return;

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.Dead) continue;
                MemoryThoughtHandler? memories = pawn.needs.mood?.thoughts.memories;
                if (memories == null) continue;
                if (GenGrid.Roofed(pawn.Position, map)) continue;
                memories.TryGainMemory(thought);
            }
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref curWeather, "curWeather");
            Scribe_Defs.Look(ref lastWeather, "lastWeather");
            Scribe_Values.Look(ref curWeatherAge, "curWeatherAge", 0);

            WeatherDecider? d = decider;
            Scribe_Deep.Look(ref d, "weatherDecider", this);
            decider = d ?? new WeatherDecider(this);
        }
    }
}
