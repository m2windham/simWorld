using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Thoughts;
using SimWorld.World;

namespace SimWorld.Weather
{
    /// <summary>
    /// One kind of weather (RimWorld: <c>Verse.WeatherDef</c>). A map always has exactly one current
    /// <see cref="WeatherDef"/> and, for the length of a transition, one previous one; every rate a consumer
    /// reads (<see cref="rainRate"/>, <see cref="snowRate"/>, wind, the multipliers) is lerped between the
    /// two by <see cref="WeatherManager"/>, so nothing ever steps discontinuously from dry to downpour.
    /// <para/>
    /// <b>Fields this port leaves out.</b> RimWorld's own WeatherDef also carries sky colours, overlay
    /// classes, an ambient sound, <c>eventMakers</c> (lightning strikes) and <c>perceivePriority</c>. All of
    /// those are presentation or need an event system this core does not have; the core is engine-free
    /// (CLAUDE.md) and a field nothing can read is the dormancy this module was briefed to avoid. Every
    /// field below has a live consumer, named on the field.
    /// <para/>
    /// <b>Constants.</b> RimWorld's shapes, recalled rather than decompiled in this sandbox; the content file
    /// says so too. Per CLAUDE.md the tests pin the behaviour these produce — rain puts fires out, snow needs
    /// cold, a desert is drier than a rainforest — never the literals.
    /// </summary>
    public class WeatherDef : Def
    {
        /// <summary>How long a spell of this weather lasts, in ticks (RimWorld: <c>WeatherDef.durationRange</c>).</summary>
        public IntRange durationRange = new IntRange(20000, 80000);

        /// <summary>0 dry, 1 downpour. Read by <see cref="WeatherManager.RainRate"/>, which drives fire
        /// extinguishment (<c>Things.FireUtility.ExtinguishFiresFromRain</c>).</summary>
        public float rainRate;

        /// <summary>0 none, 1 blizzard. Nothing accumulates snow in this port (there is no snow grid — see
        /// <see cref="WeatherManager.SnowRate"/>), so this is carried, lerped and exposed but drives no
        /// depth; it is the gate the weather itself is chosen through (<see cref="temperatureRange"/>).</summary>
        public float snowRate;

        /// <summary>Multiplies the map's base wind speed (RimWorld: <c>WeatherDef.windSpeedFactor</c>).</summary>
        public float windSpeedFactor = 1f;

        /// <summary>Added to the map's base wind speed before <see cref="windSpeedFactor"/> scales it.</summary>
        public float windSpeedOffset;

        /// <summary>Scales how fast a pawn walks under open sky (RimWorld: <c>WeatherDef.moveSpeedMultiplier</c>,
        /// read by <c>Pawn.TicksPerMove</c> — here <c>AI.Pawn_PathFollower.TicksPerMoveCardinal</c>).</summary>
        public float moveSpeedMultiplier = 1f;

        /// <summary>Scales ranged hit chance when either end of the shot is under open sky (RimWorld:
        /// <c>WeatherDef.accuracyMultiplier</c>, read by <c>Combat.ShotReport.factorFromWeather</c>).</summary>
        public float accuracyMultiplier = 1f;

        /// <summary>
        /// °C this weather adds to the outdoor temperature while it is overhead
        /// (<see cref="GenTemperature.OutdoorTemperatureAt"/>).
        /// <para/>
        /// <b>Translation.</b> RimWorld's weather does not move the thermometer at all: its temperature
        /// swings are <c>GameConditionDef</c>s (heat wave, cold snap), a system this codebase does not have —
        /// its <c>HeatWave</c>/<c>ColdSnap</c> IncidentDefs are still <c>IncidentWorker_Placeholder</c>. A
        /// small per-weather offset is carried here instead so "temperature offset" has one home rather than
        /// none; it is also the seam a real GameCondition system would add to rather than replace. Values are
        /// this port's own and deliberately small (a few degrees), pinned by direction, not magnitude.
        /// </summary>
        public float temperatureOffset;

        /// <summary>Outdoor temperatures this weather can occur at (RimWorld: <c>WeatherDef.temperatureRange</c>).
        /// This is what stops rain falling at -20 °C and snow at +30 °C without either one needing a biome
        /// list to say so.</summary>
        public FloatRange temperatureRange = new FloatRange(-999f, 999f);

        /// <summary>Whether this weather may follow itself (RimWorld: <c>WeatherDef.repeatable</c>).</summary>
        public bool repeatable;

        /// <summary>
        /// Base selection weight, before <see cref="biomeCommonalities"/>, <see cref="commonalityRainfallCurve"/>
        /// and the <see cref="temperatureRange"/> gate.
        /// <para/>
        /// <b>Translation, and why it is on this Def at all.</b> RimWorld keeps the table on the other side:
        /// <c>BiomeDef.weatherCommonalities</c> lists, per biome, every weather that biome allows and how
        /// often. That is one shared file per biome pack that every weather has to edit — exactly the
        /// contested edit CLAUDE.md tells a lane to invert ("it had the bench name its own work type instead
        /// and matched from the other side"). So the relation is inverted: each weather names the biomes it
        /// behaves differently in, and <c>World/BiomeDef.cs</c> and <c>BiomeDefs/Biomes.xml</c> are not
        /// touched at all. Encoding it as a base weight plus overrides (rather than RimWorld's exhaustive
        /// per-biome list) also means a biome added later needs no edit to any weather: it simply gets the
        /// base weights, which is a sane default rather than "no weather is possible here".
        /// </summary>
        public float commonality = 1f;

        /// <summary>Per-biome overrides of <see cref="commonality"/>; a biome listed at 0 never gets this
        /// weather. A biome not listed uses <see cref="commonality"/>.</summary>
        public List<BiomeWeatherCommonality>? biomeCommonalities;

        /// <summary>
        /// Optional multiplier over the map tile's annual rainfall in mm (RimWorld:
        /// <c>WeatherDef.commonalityRainfallFactor</c>, whose exact arithmetic could not be sourced here, so
        /// this port takes the same intent as an explicit curve: wet tiles see more rain, dry ones less).
        /// Null means rainfall does not affect how often this weather is chosen.
        /// </summary>
        public SimpleCurve? commonalityRainfallCurve;

        /// <summary>Memory gained by a pawn standing under open sky in this weather (RimWorld:
        /// <c>WeatherDef.exposedThought</c>). Null for weather nobody minds.</summary>
        public ThoughtDef? exposedThought;

        /// <summary>This weather's selection weight in <paramref name="biome"/>; <see cref="commonality"/>
        /// when the biome is null (a map with no world tile — see <see cref="MapClimate"/>) or unlisted.</summary>
        public float CommonalityIn(BiomeDef? biome)
        {
            if (biome != null && biomeCommonalities != null)
            {
                for (int i = 0; i < biomeCommonalities.Count; i++)
                {
                    if (biomeCommonalities[i].biome == biome) return biomeCommonalities[i].commonality;
                }
            }
            return commonality;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (rainRate < 0f) yield return "rainRate must not be negative.";
            if (snowRate < 0f) yield return "snowRate must not be negative.";
            if (commonality < 0f) yield return "commonality must not be negative.";
            if (durationRange.min <= 0 || durationRange.max < durationRange.min)
            {
                yield return "durationRange must be a positive ascending range of ticks.";
            }
            if (biomeCommonalities != null)
            {
                for (int i = 0; i < biomeCommonalities.Count; i++)
                {
                    if (biomeCommonalities[i].biome == null) yield return "biomeCommonalities entry has no biome.";
                    else if (biomeCommonalities[i].commonality < 0f) yield return "biomeCommonalities entry for " + biomeCommonalities[i].biome.defName + " is negative.";
                }
            }
        }
    }

    /// <summary>How often one weather is chosen in one biome — the inverted half of RimWorld's
    /// <c>BiomeDef.weatherCommonalities</c> (see <see cref="WeatherDef.commonality"/> for why it lives here).</summary>
    public sealed class BiomeWeatherCommonality
    {
        public BiomeDef biome = null!;

        public float commonality = 1f;
    }
}
